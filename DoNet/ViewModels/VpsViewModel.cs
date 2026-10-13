using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Models;
using DoNet.Services;
using Microsoft.UI.Dispatching;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the VPS screen: the record list, its search, and the one dialog that
/// adds, edits and previews a record.
/// </summary>
/// <remarks>
/// The accounts directory's shape, because the job is the same job - a paged,
/// searchable list over the encrypted store - with the VPS's two additions:
/// the pickers' options are gathered when a dialog opens rather than held from
/// the first load, and a delete has to reparent the records connected to the
/// one being removed.
///</remarks>
public sealed partial class VpsViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>Records per request. Enough to fill the grid with a little to spare.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// How long the search box waits for typing to settle. Querying per keystroke on
    /// an encrypted store is the difference between a search box and a stutter.
    /// </summary>
    private const int SearchDebounceMs = 250;

    private readonly IVpsDirectory _directory;

    private readonly IPersonDirectory _people;

    private readonly IAccountDirectory _accounts;

    private readonly DispatcherQueue? _dispatcher;

    /// <summary>
    /// The first-page load, whoever started it. Held so the view can join a load
    /// that is already running instead of starting a second one.
    /// </summary>
    private Task? _firstLoad;

    private CancellationTokenSource? _loadCts;

    private CancellationTokenSource? _searchCts;

    private int _loadGeneration;

    private bool _loadingMore;

    private VpsRecord? _pendingDelete;

    private DirectoryState _recordState = DirectoryState.Loading;

    private bool _isDialogOpen;


    private bool _hasMore;

    public VpsViewModel(
        IVpsDirectory directory,
        IPersonDirectory people,
        IAccountDirectory accounts)
    {
        _directory = directory;
        _people = people;
        _accounts = accounts;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>The records on screen, newest first.</summary>
    public ObservableCollection<VpsRecord> Records { get; } = new();

    /// <summary>The form behind the dialog, in whichever mode it is in.</summary>
    public VpsDialogViewModel Dialog { get; } = new();

    /// <summary>The record list's state, for the surface's panels.</summary>
    public DirectoryState RecordState
    {
        get => _recordState;
        private set => SetProperty(ref _recordState, value);
    }

    /// <summary>True while the first page is on its way.</summary>
    public bool IsLoading => RecordState == DirectoryState.Loading;

    /// <summary>True when the store itself refused.</summary>
    public bool IsError => RecordState == DirectoryState.Error;

    /// <summary>True when there is nothing to show and nothing being searched.</summary>
    public bool IsEmpty => RecordState == DirectoryState.Empty;

    /// <summary>True when the search excluded everything.</summary>
    public bool HasNoMatches =>
        RecordState == DirectoryState.Ready && Records.Count == 0;

    /// <summary>The search over names, system fields, country and note.</summary>
    [ObservableProperty]
    private string _searchText;

    /// <summary>True while another page can still be fetched.</summary>
    public bool HasMore
    {
        get => _hasMore;
        private set => SetProperty(ref _hasMore, value);
    }

    /// <summary>True while the dialog is on screen.</summary>
    public bool IsDialogOpen
    {
        get => _isDialogOpen;
        private set => SetProperty(ref _isDialogOpen, value);
    }

    /// <summary>The record awaiting the confirm dialog's answer.</summary>
    public VpsRecord? PendingDelete
    {
        get => _pendingDelete;
        private set => SetProperty(ref _pendingDelete, value);
    }

    /// <inheritdoc />
    [ObservableProperty]
    private bool _isConfirmingDelete;

    /// <inheritdoc />
    public string ConfirmDeleteTitle => "Delete this VPS?";

    /// <inheritdoc />
    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.Name} and its system information will be removed. "
              + "VPS records connected to it become their own main. This cannot be undone.";

    /// <summary>Returns the directory to its pre-unlock condition. Called when the app locks.</summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        // The next unlock has to fetch again rather than join this one.
        _firstLoad = null;

        Records.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        RecordState = DirectoryState.Loading;

        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>Loads the first page. Safe to call again; a second call cancels the first.</summary>
    [RelayCommand]
    public Task LoadAsync() => _firstLoad = LoadCoreAsync(showLoading: true);

    /// <summary>
    /// Loads the first page unless it is already loading or loaded.
    /// </summary>
    /// <remarks>
    /// What the view calls when it appears, so a directory whose records were
    /// fetched earlier renders them on its first frame instead of flashing empty
    /// and re-querying.
    /// </remarks>
    public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();

    private async Task LoadCoreAsync(bool showLoading)
    {
        // Cancelled but deliberately not disposed, for the reason the accounts
        // directory documents: the superseded load still holds its token.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        int generation = unchecked(++_loadGeneration);

        if (showLoading)
        {
            RecordState = DirectoryState.Loading;
        }

        try
        {
            (IReadOnlyList<VpsRecord> page, int total) = await _directory
                .GetPageWithTotalAsync(0, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                // A newer load owns the state now.
                return;
            }

            Records.ClearSafely();
            foreach (VpsRecord record in page)
            {
                Records.Add(record);
            }

            HasMore = Records.Count < total;

            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            RecordState = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("VpsViewModel.LoadCoreAsync failed", error);
            HasMore = false;
            RecordState = DirectoryState.Error;
            Records.ClearSafely();
        }
    }

    /// <summary>Fetches the next page as the end of the list comes into reach.</summary>
    public async Task LoadMoreAsync()
    {
        if (_loadingMore || !HasMore || RecordState != DirectoryState.Ready)
        {
            return;
        }

        _loadingMore = true;

        int generation = _loadGeneration;
        CancellationToken token = _loadCts?.Token ?? CancellationToken.None;

        try
        {
            (IReadOnlyList<VpsRecord> page, int total) = await _directory
                .GetPageWithTotalAsync(Records.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (VpsRecord record in page)
            {
                Records.Add(record);
            }

            HasMore = Records.Count < total;
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the list.
        }
        catch (Exception error)
        {
            AppLog.Error("VpsViewModel.LoadMoreAsync failed", error);
            HasMore = false;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    /// <summary>Queries after the typing stops, not on every character.</summary>
    partial void OnSearchTextChanged(string value)
    {
        _ = value;

        // Not disposed, for the same reason as the load source above.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        _ = SearchAfterPauseAsync(_searchCts.Token);
    }

    private async Task SearchAfterPauseAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;                     // another character arrived; that call owns it
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await LoadCoreAsync(showLoading: false).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------
    // The dialog
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the form for a new record, with the pickers' options gathered first
    /// so nothing is fetched between the click and the panel.
    /// </summary>
    public async Task AddVpsAsync()
    {
        VpsChoiceOptions? choices = await GatherChoicesAsync();
        if (choices is null)
        {
            return;
        }

        Dialog.BeginAdd(choices);
        IsDialogOpen = true;
    }

    /// <summary>Opens the form on a stored record.</summary>
    /// <param name="record">The record to edit; its system row rides along.</param>
    public async Task EditVpsAsync(VpsRecord record)
    {
        VpsChoiceOptions? choices = await GatherChoicesAsync();
        if (choices is null)
        {
            return;
        }

        Dialog.BeginEdit(record, record.System ?? new VpsSystemInfo { VpsId = record.Id }, choices);
        IsDialogOpen = true;
    }

    /// <summary>Opens the form read-only, for the card's open action.</summary>
    /// <param name="record">The record to show.</param>
    public void PreviewRecord(VpsRecord record)
    {
        Dialog.BeginPreview(record, record.System ?? new VpsSystemInfo { VpsId = record.Id });
        IsDialogOpen = true;
    }

    /// <summary>Closes the dialog without saving.</summary>
    public void CloseDialog() => IsDialogOpen = false;

    /// <summary>
    /// Commits the form: the record and its system row in one write, then the
    /// list refreshes so the card shows what the store now holds.
    /// </summary>
    public async Task CommitDialogAsync()
    {
        if (!Dialog.CanSave)
        {
            return;
        }

        VpsRecord record = Dialog.BuildRecord();
        VpsSystemInfo system = Dialog.BuildSystem();

        try
        {
            if (record.Id == 0)
            {
                await _directory.AddAsync(record, system).ConfigureAwait(true);
            }
            else if (!await _directory.UpdateAsync(record, system).ConfigureAwait(true))
            {
                // The record was deleted while the form was open, so there is
                // nothing to save into. The form closes and the list shows the
                // store's truth rather than a save that silently did nothing.
                IsDialogOpen = false;
                await LoadAsync().ConfigureAwait(true);
                return;
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Saving the VPS record failed", error);
            return;                     // the form stays open; the user's work is still in it
        }

        IsDialogOpen = false;

        // A save can change the record's name, which the node picker and other
        // cards show; the first page is the whole answer for that.
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The three directories' option lists plus every VPS, in one pass. Null
    /// means the store refused - the caller keeps the dialog shut.
    /// </summary>
    private async Task<VpsChoiceOptions?> GatherChoicesAsync()
    {
        try
        {
            IReadOnlyList<Person> people = await _people.GetOptionsAsync().ConfigureAwait(true);
            IReadOnlyList<Website> websites =
                await _accounts.GetWebsiteOptionsAsync().ConfigureAwait(true);
            IReadOnlyList<Account> accounts = await _accounts.GetOptionsAsync().ConfigureAwait(true);
            IReadOnlyList<VpsRecord> vps = await _directory.GetAllAsync().ConfigureAwait(true);

            return new VpsChoiceOptions(people, websites, accounts, vps);
        }
        catch (Exception error)
        {
            AppLog.Error("Gathering the VPS pickers' options failed", error);
            RecordState = DirectoryState.Error;
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Delete
    // ------------------------------------------------------------------

    /// <summary>Asks for the confirm dialog over a record.</summary>
    /// <param name="record">The record the user asked to remove.</param>
    public void RequestDelete(VpsRecord record)
    {
        PendingDelete = record;
        IsConfirmingDelete = true;
        OnPropertyChanged(nameof(ConfirmDeleteBody));
    }

    /// <inheritdoc />
    public void CancelDelete()
    {
        IsConfirmingDelete = false;
        PendingDelete = null;
    }

    /// <inheritdoc />
    public async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not { } doomed)
        {
            IsConfirmingDelete = false;
            return;
        }

        IsConfirmingDelete = false;
        PendingDelete = null;

        try
        {
            await _directory.DeleteAsync(doomed.Id).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error("Deleting the VPS record failed", error);
            RecordState = DirectoryState.Error;
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }
}
