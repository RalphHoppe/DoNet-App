using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Models;
using DoNet.Services;
using Microsoft.UI.Dispatching;

namespace DoNet.ViewModels;

/// <summary>What the directory is currently showing.</summary>
public enum DirectoryState
{
    Loading,
    Ready,
    Empty,
    Error,
}

/// <summary>
/// Backs the Persons directory and the dialogs that open over it.
/// </summary>
/// <remarks>
/// A singleton, so the directory survives rail switches and so the dialog hosted at the
/// HomePage root and the grid inside the content plate are driven by the same object.
///
/// The states are derived from one <see cref="State"/> field rather than several
/// independent booleans. Independent booleans drift until two are true at once and the
/// screen renders a spinner on top of an error.
/// </remarks>
public sealed partial class PersonsViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>
    /// Records fetched per request. Enough to fill the grid at the design size with a
    /// little to spare, so the first scroll is not immediately another round trip.
    /// </summary>
    public const int PageSize = 12;

    private readonly IPersonDirectory _directory;
    private readonly IEncryptedStore _store;

    /// <summary>
    /// The first-page load, whoever started it. Held so the view can join a preload
    /// that is already running instead of starting a second one.
    /// </summary>
    private Task? _firstLoad;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Bumped by every fresh load. Paging reads it before it asks for a page and
    /// again before it appends one; if the number moved in between, a newer load owns
    /// the list and the page in hand belongs to a query nobody is looking at.
    /// </summary>
    private int _loadGeneration;

    private readonly DispatcherQueue? _dispatcher;

    /// <summary>Guards against two overlapping "load the next page" requests.</summary>
    private bool _loadingMore;

    public PersonsViewModel(IPersonDirectory directory, IEncryptedStore store)
    {
        _directory = directory;
        _store = store;
        Dialog = new PersonDialogViewModel();

        // Captured here because this is constructed on the UI thread; null if that
        // ever stops being true, which IdleWork treats as "do not schedule".
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>The records currently realised on screen.</summary>
    public ObservableCollection<Person> People { get; } = new();

    /// <summary>Drives the person dialog in all three of its modes.</summary>
    public PersonDialogViewModel Dialog { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _state = DirectoryState.Loading;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isDialogOpen;

    [ObservableProperty] private bool _isConfirmingDelete;

    /// <summary>The record the confirmation is about.</summary>
    [ObservableProperty] private Person? _pendingDelete;

    /// <summary>True while more records exist beyond what has been fetched.</summary>
    [ObservableProperty] private bool _hasMore;

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    /// <summary>
    /// Records exist but the search excluded all of them. Separate from
    /// <see cref="IsEmpty"/> so the empty state never claims the directory is empty
    /// when it is the filter that is too narrow.
    /// </summary>
    public bool HasNoMatches => State == DirectoryState.Ready && People.Count == 0;

    public string ConfirmDeleteTitle => "Delete this person?";

    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.DisplayName} and every field on the record will be removed. "
              + "This cannot be undone.";

    /// <summary>
    /// Opens the store and fetches the first page before the directory is on screen.
    /// </summary>
    /// <remarks>
    /// Called while the welcome screen is animating, so roughly 2.6 seconds the user
    /// is already watching covers both the encrypted open and the first query. It
    /// fetches the page as well as opening the store - warming the connection alone
    /// still left the directory to query on arrival, which is the empty moment the
    /// home screen used to show after every unlock.
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
    /// How long the search box waits for typing to settle before it queries.
    /// </summary>
    /// <remarks>
    /// Querying on every keystroke means a round trip per character, and on an
    /// encrypted store that is the difference between a search box and a stutter.
    /// 250ms is below the threshold where a pause reads as the app being slow, and
    /// above a fast typist's inter-key interval, so a typed word is one query.
    /// </remarks>
    private const int SearchDebounceMs = 250;

    /// <summary>
    /// Returns the directory to its pre-unlock condition. Called when the app locks.
    /// </summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        // The next unlock has to fetch again rather than join this one.
        _firstLoad = null;

        People.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        State = DirectoryState.Loading;

        // Not awaited: locking must feel instant, and the vault has already dropped the
        // key by the time this runs. Observed so a failure to close is still recorded
        // rather than surfacing later as an unobserved task exception.
        _store.CloseAsync().Observe("Closing the store on lock");

        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>Loads the first page. Safe to call again; a second call cancels the first.</summary>
    [RelayCommand]
    public Task LoadAsync() => _firstLoad = LoadCoreAsync(showLoading: true);

    /// <summary>
    /// Loads the first page unless it is already loading or loaded.
    /// </summary>
    /// <remarks>
    /// What the view calls when it appears. The records are usually already here,
    /// fetched while an earlier screen was animating, and calling <see cref="LoadAsync"/>
    /// unconditionally would throw them away and re-query - which is what made the
    /// directory appear empty for a moment after every unlock before snapping to its
    /// contents. Returning the in-flight task rather than starting a second one also
    /// means a view that opens mid-preload simply waits for it.
    /// </remarks>
    public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();

    /// <summary>
    /// Loads the first page.
    /// </summary>
    /// <param name="showLoading">
    /// Whether to drop to the loading state first. False while the user is searching:
    /// replacing the results with a spinner on every refinement makes the screen flash
    /// and is slower to read than letting the old results sit for one more moment.
    /// </param>
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
            // One trip for the page and its total. Two calls meant taking the store's
            // lock twice for a single screen refresh.
            (IReadOnlyList<Person> page, int total) =
                await _directory.GetPageWithTotalAsync(0, PageSize, SearchText, token)
                    .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                // Leaving the state alone is deliberate. Every path that gets here
                // has a newer owner: either a later LoadCoreAsync that will set its
                // own state, or Reset, which parked it on Loading on purpose.
                return;
            }

            People.ClearSafely();
            foreach (Person person in page)
            {
                People.Add(person);
            }

            HasMore = People.Count < total;

            // "Empty" means the store holds nothing at all. With a search term in play
            // an empty result is a filtering outcome, not an empty directory.
            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            State = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;

            // While the user reads the first screenful, fetch the next one. The grid
            // already pages when the scroll comes within three quarters of a viewport
            // of the end, but that still makes the first flick wait on a query; this
            // way the records are usually there before the scrollbar moves. Low
            // priority, so it never competes with the rendering of what just arrived.
            if (HasMore)
            {
                IdleWork.OnUiIdle(
                    _dispatcher,
                    "Prefetching the next page of records",
                    () => LoadMoreAsync().Observe("Prefetching the next page of records"));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("PersonsViewModel.LoadCoreAsync failed", error);
            // Reach the terminal state first: a handler that mutates the same
            // collection that just threw can throw again and escape the method,
            // which would strand the directory on its loading state.
            HasMore = false;
            State = DirectoryState.Error;
            People.ClearSafely();
        }
    }

    /// <summary>
    /// Fetches the next page. Called as the grid approaches the end of what it has.
    /// </summary>
    public async Task LoadMoreAsync()
    {
        if (_loadingMore || !HasMore || State != DirectoryState.Ready)
        {
            return;
        }

        _loadingMore = true;

        // Captured before the await. If a search replaces the list while this page is
        // in flight, appending it would drop records from the previous query
        // underneath the results of the new one - rows that match nothing the user
        // typed, in a list they did not ask for.
        int generation = _loadGeneration;
        CancellationToken token = _loadCts?.Token ?? CancellationToken.None;

        try
        {
            (IReadOnlyList<Person> page, int total) = await _directory
                .GetPageWithTotalAsync(People.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (Person person in page)
            {
                People.Add(person);
            }

            HasMore = People.Count < total;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the list now.
        }
        catch (Exception error)
        {
            AppLog.Error("PersonsViewModel.LoadMoreAsync failed", error);
            // A failed page does not invalidate what is already on screen; stop asking.
            HasMore = false;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    // --- dialogs ----------------------------------------------------------

    /// <summary>Double-click on a card.</summary>
    public void OpenPreview(Person person)
    {
        Dialog.ShowPreview(person);
        IsDialogOpen = true;
    }

    /// <summary>The ADD PERSON button.</summary>
    [RelayCommand]
    private void AddPerson()
    {
        Dialog.ShowAdd();
        IsDialogOpen = true;
    }

    /// <summary>Edit, from a card's menu.</summary>
    public void OpenEdit(Person person)
    {
        Dialog.ShowEdit(person);
        IsDialogOpen = true;
    }

    public void CloseDialog() => IsDialogOpen = false;

    /// <summary>
    /// The dialog's primary button. In preview it switches to editing in place; in the
    /// other two modes it commits.
    /// </summary>
    public async Task CommitDialogAsync()
    {
        if (Dialog.IsPreview)
        {
            Dialog.SwitchToEdit();
            return;
        }

        Person person = Dialog.ToPerson();

        try
        {
            if (Dialog.Mode == PersonDialogMode.Add)
            {
                await _directory.AddAsync(person).ConfigureAwait(true);
            }
            else
            {
                await _directory.UpdateAsync(person).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("PersonsViewModel.CommitDialogAsync failed", error);
            State = DirectoryState.Error;
            IsDialogOpen = false;
            return;
        }

        IsDialogOpen = false;
        await LoadAsync().ConfigureAwait(true);
    }

    // --- delete -----------------------------------------------------------

    /// <summary>Delete, from a card's menu. Asks first; the action is irreversible.</summary>
    public void RequestDelete(Person person)
    {
        PendingDelete = person;
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
        Person? person = PendingDelete;
        IsConfirmingDelete = false;
        PendingDelete = null;

        if (person is null)
        {
            return;
        }

        try
        {
            await _directory.DeleteAsync(person.Id).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error("PersonsViewModel.ConfirmDeleteAsync failed", error);
            State = DirectoryState.Error;
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Queries after the typing stops, not on every character.
    /// </summary>
    /// <remarks>
    /// Each keystroke cancels the previous wait, so a burst of typing produces exactly
    /// one query. The cancellation token also reaches the query itself, so a search
    /// that is already in flight when the next character arrives is abandoned rather
    /// than left to finish and overwrite newer results.
    /// </remarks>
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
