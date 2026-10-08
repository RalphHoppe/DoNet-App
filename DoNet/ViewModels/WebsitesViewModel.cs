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

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasNoMatches => State == DirectoryState.Ready && Websites.Count == 0;

    public string ConfirmDeleteTitle => "Delete this website?";

    public string ConfirmDeleteBody =>
        PendingDelete is null
            ? string.Empty
            : $"{PendingDelete.DisplayName} and every field on the record will be removed. "
              + "This cannot be undone.";

    /// <summary>Returns the directory to its pre-unlock condition. Called when the app locks.</summary>
    public void Reset()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();

        Websites.ClearSafely();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        State = DirectoryState.Loading;

        OnPropertyChanged(nameof(HasNoMatches));
    }

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(showLoading: true);

    private async Task LoadCoreAsync(bool showLoading)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
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

        try
        {
            if (Dialog.Mode == WebsiteDialogMode.Add)
            {
                await _directory.AddAsync(website).ConfigureAwait(true);
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
        PendingDelete = website;
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
        Website? target = PendingDelete;

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

        _searchCts?.Cancel();
        _searchCts?.Dispose();
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
