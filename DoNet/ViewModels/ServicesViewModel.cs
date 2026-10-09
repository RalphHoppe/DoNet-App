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
/// Backs the Services directory: the grid, the search box, the dialog and the
/// delete confirmation.
/// </summary>
/// <remarks>
/// The same shape as the other three directories, deliberately. The catalog is
/// small and will stay small, but consistency here is not about scale: four
/// directories that behave identically is zero new behaviour to learn, and the one
/// that differs is the one that feels broken. A singleton, so the grid and the
/// modal at the HomePage root share state.
///</remarks>
public sealed partial class ServicesViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>Records per request. Enough to fill the grid with a little to spare.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// How long the search box waits for typing to settle. Querying per keystroke on
    /// an encrypted store is the difference between a search box and a stutter.
    /// </summary>
    private const int SearchDebounceMs = 250;

    private readonly IServiceDirectory _directory;
    private readonly IEncryptedStore _store;
    private readonly DispatcherQueue? _dispatcher;

    /// <summary>
    /// The first-page load, whoever started it. Held so the view can join a preload
    /// that is already running instead of starting a second one.
    /// </summary>
    private Task? _firstLoad;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    private int _loadGeneration;
    private bool _loadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _state = DirectoryState.Loading;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private bool _isConfirmingDelete;
    [ObservableProperty] private Service? _pendingDelete;
    [ObservableProperty] private bool _hasMore;

    public ServicesViewModel(IServiceDirectory directory, IEncryptedStore store)
    {
        _directory = directory;
        _store = store;
        Dialog = new ServiceDialogViewModel();

        // Constructed on the UI thread; null if that ever stops being true, which
        // IdleWork treats as "do not schedule".
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<Service> Services { get; } = new();

    public ServiceDialogViewModel Dialog { get; }

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasNoMatches => State == DirectoryState.Ready && Services.Count == 0;

    public string ConfirmDeleteTitle => "Delete this service?";

    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.DisplayName} will be removed from the catalog. "
              + "This cannot be undone.";

    /// <summary>Returns the directory to its pre-unlock condition. Called when the app locks.</summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        // The next unlock has to fetch again rather than join this one.
        _firstLoad = null;

        Services.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        State = DirectoryState.Loading;

        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>Loads the first page. Safe to call again; a second call cancels the first.</summary>
    [RelayCommand]
    public Task LoadAsync() => _firstLoad = LoadCoreAsync(showLoading: true);

    /// <summary>
    /// Opens the store and fetches the first page before the directory is on screen.
    /// </summary>
    /// <remarks>
    /// Scheduled from the home screen's idle window, so the Services tab is usually
    /// already in hand by the time the rail reaches it.
    /// </remarks>
    public Task PreloadAsync() => _firstLoad ??= PreloadCoreAsync();

    private async Task PreloadCoreAsync()
    {
        try
        {
            await _store.WarmUpAsync().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            // Logged, then carried on with deliberately. The warm-up is an
            // optimisation; LoadCoreAsync opens the store itself if it has to, and it
            // reports a genuine failure through the directory's error state.
            AppLog.Error("Warming the store before the first page failed", error);
        }

        await LoadCoreAsync(showLoading: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the first page unless it is already loading or loaded.
    /// </summary>
    public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();

    private async Task LoadCoreAsync(bool showLoading)
    {
        // Cancelled but deliberately not disposed, for the same reason as every
        // other directory: the superseded load is still awaiting a query that holds
        // its token.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        int generation = unchecked(++_loadGeneration);

        if (showLoading)
        {
            State = DirectoryState.Loading;
        }

        try
        {
            (IReadOnlyList<Service> page, int total) =
                await _directory.GetPageWithTotalAsync(0, PageSize, SearchText, token)
                    .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                // A newer load owns the state now.
                return;
            }

            Services.ClearSafely();
            foreach (Service service in page)
            {
                Services.Add(service);
            }

            HasMore = Services.Count < total;

            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            State = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;

            if (HasMore)
            {
                IdleWork.OnUiIdle(
                    _dispatcher,
                    "Prefetching the next page of services",
                    () => LoadMoreAsync().Observe("Prefetching the next page of services"));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.LoadCoreAsync failed", error);
            // Reach the terminal state first: a handler that mutates the same
            // collection that just threw can throw again and escape the method.
            HasMore = false;
            State = DirectoryState.Error;
            Services.ClearSafely();
        }
    }

    /// <summary>Fetches the next page as the end of the list comes into reach.</summary>
    public async Task LoadMoreAsync()
    {
        if (_loadingMore || !HasMore || State != DirectoryState.Ready)
        {
            return;
        }

        _loadingMore = true;

        int generation = _loadGeneration;
        CancellationToken token = _loadCts?.Token ?? CancellationToken.None;

        try
        {
            (IReadOnlyList<Service> page, int total) = await _directory
                .GetPageWithTotalAsync(Services.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (Service service in page)
            {
                Services.Add(service);
            }

            HasMore = Services.Count < total;
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the list.
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.LoadMoreAsync failed", error);
            HasMore = false;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    public void OpenPreview(Service service)
    {
        Dialog.ShowPreview(service);
        IsDialogOpen = true;
    }

    public void OpenEdit(Service service)
    {
        Dialog.ShowEdit(service);
        IsDialogOpen = true;
    }

    [RelayCommand]
    public void AddService()
    {
        Dialog.ShowAdd();
        IsDialogOpen = true;
    }

    public void CloseDialog() => IsDialogOpen = false;

    /// <summary>
    /// Preview turns into edit in place; add and edit commit and close.
    /// </summary>
    /// <remarks>
    /// A blank name holds the dialog open rather than saving a nameless type - the
    /// same silent hold the account dialog uses for a missing website, and for the
    /// same reason: the field that needs attention is the one still on screen.
    /// </remarks>
    public async Task CommitDialogAsync()
    {
        if (Dialog.IsPreview)
        {
            Dialog.SwitchToEdit();
            return;
        }

        if (!Dialog.CanSave)
        {
            return;
        }

        Service service = Dialog.ToService();

        // A second "vps" under a different casing is not a second service type. The
        // store is asked rather than the loaded page, because the page is a page.
        int exceptId = Dialog.Mode == ServiceDialogMode.Edit ? service.Id : 0;

        try
        {
            if (await _directory.NameInUseAsync(service.Name, exceptId).ConfigureAwait(true))
            {
                // Hold the form open with everything still typed. Closing here would
                // either save a duplicate or silently throw the edit away, and of the
                // three outcomes that is the only one nothing recovers from.
                return;
            }

            if (Dialog.Mode == ServiceDialogMode.Add)
            {
                await _directory.AddAsync(service).ConfigureAwait(true);
            }
            else
            {
                await _directory.UpdateAsync(service).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.CommitDialogAsync failed", error);
            State = DirectoryState.Error;
            IsDialogOpen = false;
            return;
        }

        IsDialogOpen = false;
        await LoadAsync().ConfigureAwait(true);
    }

    public void RequestDelete(Service service)
    {
        PendingDelete = service;
        OnPropertyChanged(nameof(ConfirmDeleteTitle));
        OnPropertyChanged(nameof(ConfirmDeleteBody));
        IsConfirmingDelete = true;
    }

    public void CancelDelete()
    {
        IsConfirmingDelete = false;
        PendingDelete = null;
    }

    public async Task ConfirmDeleteAsync()
    {
        Service? target = PendingDelete;

        IsConfirmingDelete = false;
        PendingDelete = null;

        if (target is null)
        {
            return;
        }

        try
        {
            await _directory.DeleteAsync(target.Id).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.ConfirmDeleteAsync failed", error);
            State = DirectoryState.Error;
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

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
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await LoadCoreAsync(showLoading: false).ConfigureAwait(true);
        }
    }
}
