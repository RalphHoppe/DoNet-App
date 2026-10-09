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
/// Backs the Accounts directory: the grid, the search box, the dialog and the
/// delete confirmation.
/// </summary>
/// <remarks>
/// The same shape as the other two directories, deliberately. The one addition is the
/// list of websites the dialog's picker offers, fetched alongside each page for the
/// same reason the payment catalog is: a form that queries when it opens puts a round
/// trip between the click and the panel.
/// </remarks>
public sealed partial class AccountsViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>Records per request. Enough to fill the grid with a little to spare.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// How long the search box waits for typing to settle. Querying per keystroke on
    /// an encrypted store is the difference between a search box and a stutter.
    /// </summary>
    private const int SearchDebounceMs = 250;

    private readonly IAccountDirectory _directory;
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

    /// <summary>The websites the dialog offers, fetched once per load.</summary>
    private IReadOnlyList<Website> _websiteOptions = Array.Empty<Website>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _state = DirectoryState.Loading;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private bool _isConfirmingDelete;
    [ObservableProperty] private Account? _pendingDelete;
    [ObservableProperty] private bool _hasMore;

    public AccountsViewModel(IAccountDirectory directory, IEncryptedStore store)
    {
        _directory = directory;
        _store = store;
        Dialog = new AccountDialogViewModel();

        // Constructed on the UI thread; null if that ever stops being true, which
        // IdleWork treats as "do not schedule".
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<Account> Accounts { get; } = new();

    public AccountDialogViewModel Dialog { get; }

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasNoMatches => State == DirectoryState.Ready && Accounts.Count == 0;

    public string ConfirmDeleteTitle => "Delete this account?";

    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.DisplayName} and its password will be removed. "
              + "This cannot be undone.";

    /// <summary>Returns the directory to its pre-unlock condition. Called when the app locks.</summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        // The next unlock has to fetch again rather than join this one.
        _firstLoad = null;

        Accounts.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        _websiteOptions = Array.Empty<Website>();
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
    /// Scheduled from the home screen's idle window, so the records are usually in
    /// hand by the time the rail reaches Accounts.
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
            // reports a genuine failure through the directory's error state. Letting
            // this escape would instead leave the task that the view awaits faulted,
            // with nothing having moved the directory off its loading state.
            AppLog.Error("Warming the store before the first page failed", error);
        }

        await LoadCoreAsync(showLoading: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the first page unless it is already loading or loaded.
    /// </summary>
    /// <remarks>
    /// What the view calls when it appears, so a directory whose records were fetched
    /// during an earlier screen's animation renders them on its first frame instead of
    /// flashing empty and re-querying.
    /// </remarks>
    public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();

    private async Task LoadCoreAsync(bool showLoading)
    {
        // Cancelled but deliberately not disposed. The superseded load is still
        // awaiting a query that holds its token, and CancellationToken.Register -
        // which the SQLite provider calls to wire up interrupt - throws
        // ObjectDisposedException once the source behind the token is gone. These
        // sources never touch WaitHandle and own no unmanaged resource, so letting
        // the collector take them costs nothing and removes the race outright.
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
            (IReadOnlyList<Account> page, int total) =
                await _directory.GetPageWithTotalAsync(0, PageSize, SearchText, token)
                    .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                // Leaving the state alone is deliberate. Every path that gets here
                // has a newer owner: either a later LoadCoreAsync that will set its
                // own state, or Reset, which parked it on Loading on purpose.
                return;
            }

            Accounts.ClearSafely();
            foreach (Account account in page)
            {
                Accounts.Add(account);
            }

            HasMore = Accounts.Count < total;

            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            State = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;

            // The dialog needs the website list the moment one opens, and fetching it
            // then would put a query between the click and the panel.
            _websiteOptions = await _directory.GetWebsiteOptionsAsync(token).ConfigureAwait(true);

            if (HasMore)
            {
                IdleWork.OnUiIdle(
                    _dispatcher,
                    "Prefetching the next page of accounts",
                    () => LoadMoreAsync().Observe("Prefetching the next page of accounts"));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("AccountsViewModel.LoadCoreAsync failed", error);
            // Reach the terminal state first: a handler that mutates the same
            // collection that just threw can throw again and escape the method,
            // which would strand the directory on its loading state.
            HasMore = false;
            State = DirectoryState.Error;
            Accounts.ClearSafely();
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
            (IReadOnlyList<Account> page, int total) = await _directory
                .GetPageWithTotalAsync(Accounts.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (Account account in page)
            {
                Accounts.Add(account);
            }

            HasMore = Accounts.Count < total;
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the list.
        }
        catch (Exception error)
        {
            AppLog.Error("AccountsViewModel.LoadMoreAsync failed", error);
            HasMore = false;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    public void OpenPreview(Account account)
    {
        Dialog.ShowPreview(account, _websiteOptions);
        IsDialogOpen = true;
    }

    public void OpenEdit(Account account)
    {
        Dialog.ShowEdit(account, _websiteOptions);
        IsDialogOpen = true;
    }

    [RelayCommand]
    public void AddAccount()
    {
        Dialog.ShowAdd(_websiteOptions);
        IsDialogOpen = true;
    }

    public void CloseDialog() => IsDialogOpen = false;

    /// <summary>Preview turns into edit in place; add and edit commit and close.</summary>
    public async Task CommitDialogAsync()
    {
        if (Dialog.IsPreview)
        {
            Dialog.SwitchToEdit();
            return;
        }

        // An account with no website is not a weaker record, it is a meaningless one,
        // and the store would reject it with a foreign key error the user cannot read.
        // Holding the dialog open leaves them on the field that needs attention.
        if (!Dialog.CanSave)
        {
            return;
        }

        Account account = Dialog.ToAccount();

        try
        {
            if (Dialog.Mode == AccountDialogMode.Add)
            {
                await _directory.AddAsync(account).ConfigureAwait(true);
            }
            else
            {
                await _directory.UpdateAsync(account).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("AccountsViewModel.CommitDialogAsync failed", error);
            State = DirectoryState.Error;
            IsDialogOpen = false;
            return;
        }

        IsDialogOpen = false;
        await LoadAsync().ConfigureAwait(true);
    }

    public void RequestDelete(Account account)
    {
        PendingDelete = account;
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
        Account? target = PendingDelete;

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
            AppLog.Error("AccountsViewModel.ConfirmDeleteAsync failed", error);
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
            return;                     // another character arrived; that call owns it
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await LoadCoreAsync(showLoading: false).ConfigureAwait(true);
    }
}
