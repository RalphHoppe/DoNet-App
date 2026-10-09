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

/// <summary>
/// Backs the Websites directory and the dialogs over it.
/// </summary>
/// <remarks>
/// The same shape as <see cref="PersonsViewModel"/> - one state field rather than
/// several booleans, a debounced search, generation-guarded paging - because the two
/// screens behave identically and divergence between them would be a bug, not a
/// feature. A singleton, so the grid and the modals at the HomePage root share state.
/// </remarks>
public sealed partial class WebsitesViewModel : ObservableObject, IDeleteConfirmHost
{
    /// <summary>Records per request. Enough to fill the grid with a little to spare.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// How long the search box waits for typing to settle. Querying per keystroke on
    /// an encrypted store is the difference between a search box and a stutter.
    /// </summary>
    private const int SearchDebounceMs = 250;

    private readonly IWebsiteDirectory _directory;
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

    /// <summary>The catalog, fetched once per load so the dialogs open instantly.</summary>
    private IReadOnlyList<string> _methodOptions = PaymentMethodCatalog.BuiltIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _state = DirectoryState.Loading;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private bool _isConfirmingDelete;
    [ObservableProperty] private Website? _pendingDelete;
    [ObservableProperty] private bool _hasMore;

    /// <summary>
    /// The payment methods a delete confirmation is currently about, if any. The
    /// same confirmation dialog serves websites and methods, so this and
    /// <see cref="PendingDelete"/> are mutually exclusive by construction: asking
    /// for one clears the other.
    /// </summary>
    private IReadOnlyList<string>? _pendingMethodDelete;

    /// <summary>The confirmation body for the pending method delete, composed when it was asked for.</summary>
    private string _pendingMethodDeleteBody = string.Empty;

    public WebsitesViewModel(IWebsiteDirectory directory, IEncryptedStore store)
    {
        _directory = directory;
        _store = store;
        Dialog = new WebsiteDialogViewModel();

        // Constructed on the UI thread; null if that ever stops being true, which
        // IdleWork treats as "do not schedule".
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<Website> Websites { get; } = new();

    public WebsiteDialogViewModel Dialog { get; }

    /// <summary>
    /// The website the dialog most recently created, or null if the last dialog
    /// session did not add one.
    /// </summary>
    /// <remarks>
    /// Read by the account dialog, which offers to create a website from its own
    /// site picker and needs to know, when the website dialog closes back onto it,
    /// which record to select. It is a hand-off between two singletons rather than
    /// an event because exactly one consumer cares, once, at a moment it already
    /// knows to look.
    /// </remarks>
    public Website? LastAddedWebsite { get; private set; }

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasNoMatches => State == DirectoryState.Ready && Websites.Count == 0;

    public string ConfirmDeleteTitle => _pendingMethodDelete is { Count: > 0 }
        ? "Delete these payment methods?"
        : "Delete this website?";

    public string ConfirmDeleteBody =>
        _pendingMethodDelete is { Count: > 0 } ? _pendingMethodDeleteBody
        : PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.DisplayName} and every field on the record will be removed. "
              + "This cannot be undone.";

    /// <summary>Returns the directory to its pre-unlock condition. Called when the app locks.</summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        // The next unlock has to fetch again rather than join this one.
        _firstLoad = null;

        Websites.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        _pendingMethodDelete = null;
        LastAddedWebsite = null;
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
    /// Scheduled from the home screen's idle window, so the records for the Sites tab
    /// are usually already in hand by the time the rail reaches it.
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
    /// What the view calls when it appears. The records are usually already here,
    /// fetched while an earlier screen was animating, and calling <see cref="LoadAsync"/>
    /// unconditionally would throw them away and re-query - which is what made the
    /// directory appear empty for a moment after every unlock before snapping to its
    /// contents. Returning the in-flight task rather than starting a second one also
    /// means a view that opens mid-preload simply waits for it.
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
            (IReadOnlyList<Website> page, int total) =
                await _directory.GetPageWithTotalAsync(0, PageSize, SearchText, token)
                    .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _loadGeneration)
            {
                // Leaving the state alone is deliberate. Every path that gets here
                // has a newer owner: either a later LoadCoreAsync that will set its
                // own state, or Reset, which parked it on Loading on purpose.
                return;
            }

            Websites.ClearSafely();
            foreach (Website website in page)
            {
                Websites.Add(website);
            }

            HasMore = Websites.Count < total;

            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            State = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;

            // The dialogs need the catalog the moment one opens, and fetching it then
            // would put a query between the click and the panel.
            _methodOptions = await _directory.GetPaymentMethodsAsync(token).ConfigureAwait(true);

            if (HasMore)
            {
                IdleWork.OnUiIdle(
                    _dispatcher,
                    "Prefetching the next page of websites",
                    () => LoadMoreAsync().Observe("Prefetching the next page of websites"));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("WebsitesViewModel.LoadCoreAsync failed", error);
            // Reach the terminal state first: a handler that mutates the same
            // collection that just threw can throw again and escape the method,
            // which would strand the directory on its loading state.
            HasMore = false;
            State = DirectoryState.Error;
            Websites.ClearSafely();
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
            (IReadOnlyList<Website> page, int total) = await _directory
                .GetPageWithTotalAsync(Websites.Count, PageSize, SearchText, token)
                .ConfigureAwait(true);

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (Website website in page)
            {
                Websites.Add(website);
            }

            HasMore = Websites.Count < total;
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the list.
        }
        catch (Exception error)
        {
            AppLog.Error("WebsitesViewModel.LoadMoreAsync failed", error);
            HasMore = false;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    public void OpenPreview(Website website)
    {
        Dialog.ShowPreview(website, _methodOptions);
        IsDialogOpen = true;
    }

    public void OpenEdit(Website website)
    {
        Dialog.ShowEdit(website, _methodOptions);
        IsDialogOpen = true;
    }

    [RelayCommand]
    public void AddWebsite()
    {
        Dialog.ShowAdd(_methodOptions);
        IsDialogOpen = true;
    }

    public void CloseDialog() => IsDialogOpen = false;

    /// <summary>
    /// Preview turns into edit in place; add and edit commit and close.
    /// </summary>
    public async Task CommitDialogAsync()
    {
        if (Dialog.IsPreview)
        {
            Dialog.SwitchToEdit();
            return;
        }

        Website website = Dialog.ToWebsite();

        // Cleared before the save so a failure leaves no ghost: a consumer that
        // watches for the dialog closing would otherwise find a record that was
        // never stored.
        LastAddedWebsite = null;

        try
        {
            if (Dialog.Mode == WebsiteDialogMode.Add)
            {
                LastAddedWebsite = await _directory.AddAsync(website).ConfigureAwait(true);
            }
            else
            {
                await _directory.UpdateAsync(website).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("WebsitesViewModel.CommitDialogAsync failed", error);
            State = DirectoryState.Error;
            IsDialogOpen = false;
            return;
        }

        IsDialogOpen = false;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Saves a method the user typed so it is offered next time.
    /// </summary>
    /// <remarks>
    /// Not awaited by the caller: the chip appears the moment it is typed, and whether
    /// the catalog write succeeded does not change what is on screen now. Observed so
    /// a failure is logged rather than lost.
    /// </remarks>
    public void RememberMethod(string method)
    {
        _methodOptions = PaymentMethodCatalog.Merge(new[] { method }.AsEnumerable().Concat(_methodOptions));

        _directory.RememberPaymentMethodAsync(method)
            .Observe($"Saving the payment method '{method}'");
    }

    public void RequestDelete(Website website)
    {
        _pendingMethodDelete = null;
        PendingDelete = website;
        OnPropertyChanged(nameof(ConfirmDeleteTitle));
        OnPropertyChanged(nameof(ConfirmDeleteBody));
        IsConfirmingDelete = true;
    }

    /// <summary>
    /// Asks for confirmation before deleting payment methods from the catalog.
    /// </summary>
    /// <remarks>
    /// The count of websites using them is part of the question, so it is gathered
    /// before the dialog opens; the confirmation is the one place the user gets to
    /// see how far a delete reaches, and it cannot say "used by 4 websites" after
    /// they have already clicked yes.
    /// </remarks>
    public async Task RequestMethodDeleteAsync(IReadOnlyList<string> methods)
    {
        int usingCount;
        try
        {
            usingCount = await _directory.CountWebsitesUsingAsync(methods).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error("Counting the websites using a payment method failed", error);
            return;
        }

        int count = methods.Count;
        string websites = usingCount == 1 ? "1 website" : $"{usingCount} websites";

        _pendingMethodDeleteBody = count switch
        {
            1 when usingCount == 0 =>
                $"\u201C{methods[0]}\u201D will no longer be offered as a payment method. "
                + "This cannot be undone.",
            1 =>
                $"\u201C{methods[0]}\u201D is used by {websites}. It will be removed from "
                + "those records and no longer offered. This cannot be undone.",
            _ when usingCount == 0 =>
                $"{count} payment methods will no longer be offered. This cannot be undone.",
            _ =>
                $"{count} payment methods are used by {websites}. They will be removed "
                + "from those records and no longer offered. This cannot be undone.",
        };

        PendingDelete = null;
        _pendingMethodDelete = methods;
        OnPropertyChanged(nameof(ConfirmDeleteTitle));
        OnPropertyChanged(nameof(ConfirmDeleteBody));
        IsConfirmingDelete = true;
    }

    /// <summary>
    /// Renames a payment method everywhere: the catalog, every website that lists
    /// it, and the open dialog.
    /// </summary>
    public async Task RenamePaymentMethodAsync(string oldName, string newName)
    {
        try
        {
            await _directory.RenamePaymentMethodAsync(oldName, newName).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error($"Renaming the payment method '{oldName}' failed", error);
            State = DirectoryState.Error;
            return;
        }

        _methodOptions = await _directory.GetPaymentMethodsAsync().ConfigureAwait(true);
        Dialog.RenameMethod(oldName, newName);
        PaymentMethodsChanged?.Invoke();

        // The cards behind the dialog still show the old name on their chips.
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Raised after the payment catalog changes through a rename or a confirmed
    /// delete - not through the add box, which the dialog already knows about.
    /// </summary>
    /// <remarks>
    /// The website dialog has states of its own around these actions (the tap
    /// targets, the rename popup) that belong to the view, not the view model. This
    /// is how it learns the action went through.
    /// </remarks>
    public event Action? PaymentMethodsChanged;

    public void CancelDelete()
    {
        IsConfirmingDelete = false;
        PendingDelete = null;
        _pendingMethodDelete = null;
    }

    public async Task ConfirmDeleteAsync()
    {
        Website? target = PendingDelete;
        IReadOnlyList<string>? methods = _pendingMethodDelete;

        IsConfirmingDelete = false;
        PendingDelete = null;
        _pendingMethodDelete = null;

        if (methods is { Count: > 0 })
        {
            try
            {
                await _directory.DeletePaymentMethodsAsync(methods).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                AppLog.Error("Deleting payment methods failed", error);
                State = DirectoryState.Error;
                return;
            }

            _methodOptions = await _directory.GetPaymentMethodsAsync().ConfigureAwait(true);
            Dialog.RemoveMethods(methods);
            PaymentMethodsChanged?.Invoke();

            await LoadAsync().ConfigureAwait(true);
            return;
        }

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
            AppLog.Error("WebsitesViewModel.ConfirmDeleteAsync failed", error);
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
