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
/// Backs the Wallets screen: the record list, its search, and the one dialog
/// that adds, edits and previews a record.
/// </summary>
/// <remarks>
/// The VPS screen's shape, because the job is the same job - a paged,
/// searchable list over the encrypted store, with the pickers' options
/// gathered when a dialog opens rather than held from the first load.
///</remarks>
public sealed partial class WalletViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>Records per request. Enough to fill the grid with a little to spare.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// How long the search box waits for typing to settle, for the same reason
    /// as every other directory.
    /// </summary>
    private const int SearchDebounceMs = 250;

    private readonly IWalletDirectory _directory;

    private readonly IPersonDirectory _people;

    private readonly DispatcherQueue? _dispatcher;

    /// <summary>
    /// The first-page load, whoever started it, so the view can join a load
    /// that is already running instead of starting a second one.
    /// </summary>
    private Task? _firstLoad;

    private CancellationTokenSource? _loadCts;

    private CancellationTokenSource? _searchCts;

    private int _loadGeneration;

    private bool _loadingMore;

    private WalletRecord? _pendingDelete;

    // The panels' booleans are computed from this, so every change has to
    // carry them along - see the VPS directory for the panel that never heard
    // a load finish.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _recordState = DirectoryState.Loading;

    private bool _isDialogOpen;

    private bool _hasMore;

    public WalletViewModel(
        IWalletDirectory directory,
        IPersonDirectory people)
    {
        _directory = directory;
        _people = people;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>The records on screen, newest first.</summary>
    public ObservableCollection<WalletRecord> Records { get; } = new();

    /// <summary>The form behind the dialog, in whichever mode it is in.</summary>
    public WalletDialogViewModel Dialog { get; } = new();

    /// <summary>True while the first page is on its way.</summary>
    public bool IsLoading => RecordState == DirectoryState.Loading;

    /// <summary>True when the store itself refused.</summary>
    public bool IsError => RecordState == DirectoryState.Error;

    /// <summary>True when there is nothing to show and nothing being searched.</summary>
    public bool IsEmpty => RecordState == DirectoryState.Empty;

    /// <summary>True when the search excluded everything.</summary>
    public bool HasNoMatches =>
        RecordState == DirectoryState.Ready && Records.Count == 0;

    /// <summary>The search over the brand, the number and the note.</summary>
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
    public WalletRecord? PendingDelete
    {
        get => _pendingDelete;
        private set => SetProperty(ref _pendingDelete, value);
    }

    /// <inheritdoc />
    [ObservableProperty]
    private bool _isConfirmingDelete;

    /// <inheritdoc />
    public string ConfirmDeleteTitle => "Delete this wallet?";

    /// <inheritdoc />
    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.Wallet} will be removed. VPS records that were paid "
              + "with it keep their history - their pointer clears, they do not go. "
              + "This cannot be undone.";

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
    /// Loads the first page unless it is already loading or loaded, so a
    /// directory whose records were fetched earlier renders them on its first
    /// frame instead of flashing empty and re-querying.
    /// </summary>
    public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();

    private async Task LoadCoreAsync(bool showLoading)
    {
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
            (IReadOnlyList<WalletRecord> page, int total) = await _directory
                .GetPageWithTotalAsync(0, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                return;
            }

            Records.ClearSafely();
            foreach (WalletRecord record in page)
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
            AppLog.Error("WalletViewModel.LoadCoreAsync failed", error);
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
            (IReadOnlyList<WalletRecord> page, int total) = await _directory
                .GetPageWithTotalAsync(Records.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (WalletRecord record in page)
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
            AppLog.Error("WalletViewModel.LoadMoreAsync failed", error);
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
    /// Opens the form for a new record, with the picker's options gathered
    /// first so nothing is fetched between the click and the panel.
    /// </summary>
    public async Task AddWalletAsync()
    {
        WalletChoiceOptions? choices = await GatherChoicesAsync();
        if (choices is null)
        {
            return;
        }

        Dialog.BeginAdd(choices);
        IsDialogOpen = true;
    }

    /// <summary>Opens the form on a stored record.</summary>
    /// <param name="record">The record to edit.</param>
    public async Task EditWalletAsync(WalletRecord record)
    {
        WalletChoiceOptions? choices = await GatherChoicesAsync();
        if (choices is null)
        {
            return;
        }

        Dialog.BeginEdit(record, choices);
        IsDialogOpen = true;
    }

    /// <summary>Shows the record read-only, with no store reads at all.</summary>
    /// <param name="record">The record to show.</param>
    public void PreviewRecord(WalletRecord record)
    {
        Dialog.BeginPreview(record);
        IsDialogOpen = true;
    }

    /// <summary>Closes the dialog and abandons whatever was in it.</summary>
    public void CloseDialog()
    {
        IsDialogOpen = false;
    }

    /// <summary>
    /// Commits the form: the record in one write, then the list refreshes so
    /// the card shows what the store now holds.
    /// </summary>
    public async Task CommitDialogAsync()
    {
        if (!Dialog.CanSave)
        {
            return;
        }

        WalletRecord record = Dialog.BuildRecord();

        try
        {
            if (record.Id == 0)
            {
                await _directory.AddAsync(record).ConfigureAwait(true);
            }
            else if (!await _directory.UpdateAsync(record).ConfigureAwait(true))
            {
                // The record was deleted while the form was open. The form
                // closes and the list shows the store's truth.
                IsDialogOpen = false;
                await LoadAsync().ConfigureAwait(true);
                return;
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Saving the wallet record failed", error);
            return;                     // the form stays open; the work is still in it
        }

        IsDialogOpen = false;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The person picker's option list, gathered once per open. Null means the
    /// store refused - the caller keeps the dialog shut.
    /// </summary>
    private async Task<WalletChoiceOptions?> GatherChoicesAsync()
    {
        try
        {
            IReadOnlyList<Person> people = await _people.GetOptionsAsync().ConfigureAwait(true);
            return new WalletChoiceOptions(people);
        }
        catch (Exception error)
        {
            AppLog.Error("Gathering the wallet picker's options failed", error);
            RecordState = DirectoryState.Error;
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Delete
    // ------------------------------------------------------------------

    /// <summary>Asks for the confirm dialog over a record.</summary>
    /// <param name="record">The record the user asked to remove.</param>
    public void RequestDelete(WalletRecord record)
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
            AppLog.Error("Deleting the wallet record failed", error);
            RecordState = DirectoryState.Error;
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }
}
