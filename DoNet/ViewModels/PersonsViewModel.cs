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
public sealed partial class PersonsViewModel : ObservableObject
{
    /// <summary>
    /// Records fetched per request. Enough to fill the grid at the design size with a
    /// little to spare, so the first scroll is not immediately another round trip.
    /// </summary>
    public const int PageSize = 12;

    private readonly IPersonDirectory _directory;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    /// <summary>Guards against two overlapping "load the next page" requests.</summary>
    private bool _loadingMore;

    public PersonsViewModel(IPersonDirectory directory)
    {
        _directory = directory;
        Dialog = new PersonDialogViewModel();
    }

    /// <summary>The records currently realised on screen.</summary>
    public ObservableCollection<Person> People { get; } = new();

    /// <summary>Drives the person dialog in all three of its modes.</summary>
    public PersonDialogViewModel Dialog { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasCards))]
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

    public bool HasCards => State == DirectoryState.Ready && People.Count > 0;

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
    /// Opens the store before the directory is on screen. Called while the welcome
    /// screen is animating, so the open happens during something the user is already
    /// watching instead of showing as a delay here.
    /// </summary>
    public Task PreloadAsync() => _directory.WarmUpAsync();

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

        People.Clear();
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
        HasMore = false;
        IsDialogOpen = false;
        IsConfirmingDelete = false;
        PendingDelete = null;
        State = DirectoryState.Loading;

        (_directory as Services.PersonDirectoryService)?.Reset();

        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>Loads the first page. Safe to call again; a second call cancels the first.</summary>
    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(showLoading: true);

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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

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

            if (token.IsCancellationRequested)
            {
                return;
            }

            People.Clear();
            foreach (Person person in page)
            {
                People.Add(person);
            }

            HasMore = People.Count < total;

            // "Empty" means the store holds nothing at all. With a search term in play
            // an empty result is a filtering outcome, not an empty directory.
            bool searching = !string.IsNullOrWhiteSpace(SearchText);
            State = total == 0 && !searching ? DirectoryState.Empty : DirectoryState.Ready;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, which owns the state now.
        }
        catch (Exception)
        {
            People.Clear();
            HasMore = false;
            State = DirectoryState.Error;
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
        try
        {
            (IReadOnlyList<Person> page, int total) = await _directory
                .GetPageWithTotalAsync(People.Count, PageSize, SearchText)
                .ConfigureAwait(true);

            foreach (Person person in page)
            {
                People.Add(person);
            }

            HasMore = People.Count < total;
        }
        catch (Exception)
        {
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
        catch (Exception)
        {
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
        catch (Exception)
        {
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
            return;                     // another character arrived; that call owns it
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await LoadCoreAsync(showLoading: false).ConfigureAwait(true);
    }
}
