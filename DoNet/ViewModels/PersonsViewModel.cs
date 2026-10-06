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
/// Backs the Persons directory.
/// </summary>
/// <remarks>
/// The states are mutually exclusive and derived from one <see cref="State"/> field
/// rather than from a handful of independent booleans, because independent booleans
/// drift - two of them end up true and the screen shows a spinner on top of an error.
/// The view binds to the computed flags, so only one branch can ever be visible.
/// </remarks>
public sealed partial class PersonsViewModel : ObservableObject
{
    private readonly IPersonDirectory _directory;

    /// <summary>
    /// Everything the source returned. <see cref="People"/> is this list filtered by the
    /// search box, so clearing the search restores the full set without another fetch.
    /// </summary>
    private IReadOnlyList<PersonPreview> _all = Array.Empty<PersonPreview>();

    /// <summary>Cancels an in-flight load if the screen reloads before it finishes.</summary>
    private CancellationTokenSource? _loadCts;

    public PersonsViewModel(IPersonDirectory directory)
    {
        _directory = directory;
    }

    /// <summary>The cards currently on screen, after filtering.</summary>
    public ObservableCollection<PersonPreview> People { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DirectoryState _state = DirectoryState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private string _searchText = string.Empty;

    /// <summary>The message shown by the error state. Never the raw exception text.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    /// <summary>No records exist at all - distinct from a search that matched nothing.</summary>
    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasCards => State == DirectoryState.Ready && People.Count > 0;

    /// <summary>
    /// Records exist but the current search excluded every one of them. Kept separate
    /// from <see cref="IsEmpty"/> so the empty state never claims the directory is empty
    /// when it is really the filter that is too narrow.
    /// </summary>
    public bool HasNoMatches => State == DirectoryState.Ready && People.Count == 0;

    /// <summary>
    /// Loads the directory. Safe to call repeatedly; a second call cancels the first.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        State = DirectoryState.Loading;
        ErrorMessage = string.Empty;

        try
        {
            IReadOnlyList<PersonPreview> loaded =
                await _directory.GetPreviewsAsync(token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _all = loaded;
            ApplyFilter();
            State = _all.Count == 0 ? DirectoryState.Empty : DirectoryState.Ready;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load. The newer one owns the state.
        }
        catch (Exception)
        {
            // The exception text is for a log, not for the screen. Showing a user a
            // provider stack trace tells them nothing they can act on.
            _all = Array.Empty<PersonPreview>();
            People.Clear();
            ErrorMessage = "The directory could not be opened.";
            State = DirectoryState.Error;
        }
    }

    /// <summary>Retry button on the error state.</summary>
    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    /// <summary>
    /// Opens the full record. Double-click on a card is wired to this.
    /// </summary>
    /// <remarks>
    /// Intentionally does nothing: the full record form has not been designed yet.
    /// The gesture, the command and the parameter are all in place, so adding that
    /// screen later is a navigation call in this method and no change to the view.
    /// </remarks>
    [RelayCommand]
    private void OpenPerson(PersonPreview? person)
    {
        _ = person;
    }

    /// <summary>
    /// The ADD PERSON button.
    /// </summary>
    /// <remarks>Also intentionally inert, for the same reason.</remarks>
    [RelayCommand]
    private void AddPerson()
    {
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value;

        if (State is DirectoryState.Loading or DirectoryState.Error)
        {
            return;
        }

        ApplyFilter();
    }

    /// <summary>
    /// Rebuilds <see cref="People"/> from <see cref="_all"/> for the current search term.
    /// </summary>
    private void ApplyFilter()
    {
        string term = SearchText?.Trim() ?? string.Empty;

        People.Clear();

        foreach (PersonPreview person in _all.Where(p => p.Matches(term)))
        {
            People.Add(person);
        }

        // People is a collection, so changing its contents raises no property change for
        // the flags that depend on its count. Raise them by hand.
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(HasNoMatches));
    }
}
