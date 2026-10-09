using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IPersonDirectory _people;
    private readonly IAccountDirectory _accounts;
    private readonly WebsitesViewModel _websites;
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

    /// <summary>The type whose records are on screen, when drilled in.</summary>
    private Service? _selectedType;

    /// <summary>
    /// The type the structure designer was opened for. Kept apart from
    /// <see cref="_selectedType"/> because the designer opens before the first
    /// drill-in, not after it.
    /// </summary>
    private Service? _designerTarget;

    /// <summary>The drilled type's structure.</summary>
    private ServiceDefinition? _definition;

    /// <summary>The relationship options for the drilled type, loaded on entry.</summary>
    private ServiceChoiceOptions _choices = new(
        new Dictionary<ServiceFieldKind, ServiceChoiceSource>());

    private Task? _recordsLoad;
    private CancellationTokenSource? _recordCts;
    private CancellationTokenSource? _recordSearchCts;
    private int _recordGeneration;
    private bool _loadingMoreRecords;

    /// <summary>The record a delete confirmation is about, when it is a record.</summary>
    private ServiceRecord? _pendingRecordDelete;

    /// <summary>The composed body for the pending type delete, records included.</summary>
    private string _pendingTypeDeleteBody = string.Empty;

    /// <summary>The composed body for the pending record delete.</summary>
    private string _pendingRecordDeleteBody = string.Empty;

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

    [ObservableProperty] private bool _isDrilled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordLoading))]
    [NotifyPropertyChangedFor(nameof(IsRecordError))]
    [NotifyPropertyChangedFor(nameof(IsRecordEmpty))]
    [NotifyPropertyChangedFor(nameof(HasRecordNoMatches))]
    private DirectoryState _recordState = DirectoryState.Loading;

    [ObservableProperty] private string _recordSearchText = string.Empty;
    [ObservableProperty] private bool _isRecordDialogOpen;
    [ObservableProperty] private bool _isDesignerOpen;
    [ObservableProperty] private bool _hasMoreRecords;

    public ServicesViewModel(
        IServiceDirectory directory,
        IEncryptedStore store,
        IPersonDirectory people,
        IAccountDirectory accounts,
        WebsitesViewModel websites)
    {
        _directory = directory;
        _store = store;
        _people = people;
        _accounts = accounts;
        _websites = websites;
        Dialog = new ServiceDialogViewModel();
        Designer = new ServiceDesignerViewModel();
        RecordDialog = new ServiceRecordDialogViewModel();

        // The record form can create a website from its own pickers, and has to
        // notice when one comes back. Both are singletons, so this subscription is
        // for the life of the process and never duplicates.
        _websites.PropertyChanged += OnWebsitesPropertyChanged;

        // Constructed on the UI thread; null if that ever stops being true, which
        // IdleWork treats as "do not schedule".
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<Service> Services { get; } = new();

    public ObservableCollection<ServiceRecord> Records { get; } = new();

    public ServiceDialogViewModel Dialog { get; }

    public ServiceDesignerViewModel Designer { get; }

    public ServiceRecordDialogViewModel RecordDialog { get; }

    /// <summary>The type whose records are on screen, when drilled in.</summary>
    public Service? SelectedType => _selectedType;

    /// <summary>The drilled type's structure, or null at the type list.</summary>
    public ServiceDefinition? Structure => _definition;

    /// <summary>The relationship options for the drilled type's pickers and cards.</summary>
    public ServiceChoiceOptions Choices => _choices;

    public bool IsRecordLoading => RecordState == DirectoryState.Loading;

    public bool IsRecordError => RecordState == DirectoryState.Error;

    public bool IsRecordEmpty => RecordState == DirectoryState.Empty;

    public bool HasRecordNoMatches => RecordState == DirectoryState.Ready && Records.Count == 0;

    public bool IsLoading => State == DirectoryState.Loading;

    public bool IsError => State == DirectoryState.Error;

    public bool IsEmpty => State == DirectoryState.Empty;

    public bool HasNoMatches => State == DirectoryState.Ready && Services.Count == 0;

    public string ConfirmDeleteTitle => _pendingRecordDelete is not null
        ? $"Delete this {_selectedType?.DisplayName.ToLowerInvariant() ?? "record"}?"
        : "Delete this service?";

    public string ConfirmDeleteBody =>
        _pendingRecordDelete is not null
            ? _pendingRecordDeleteBody
            : PendingDelete is null
                ? string.Empty
                : _pendingTypeDeleteBody;

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

        // Out of the drilled type as well: its records are decrypted data and the
        // lock screen must not sit one Back away from them.
        _recordCts?.Cancel();
        _recordSearchCts?.Cancel();
        _recordsLoad = null;
        _selectedType = null;
        _designerTarget = null;
        _definition = null;
        _choices = new ServiceChoiceOptions(
            new Dictionary<ServiceFieldKind, ServiceChoiceSource>());
        Records.ClearSafely();
        SetProperty(ref _recordSearchText, string.Empty, nameof(RecordSearchText));
        HasMoreRecords = false;
        IsDrilled = false;
        IsRecordDialogOpen = false;
        IsDesignerOpen = false;
        _pendingRecordDelete = null;
        RecordState = DirectoryState.Loading;

        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasRecordNoMatches));
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

    /// <summary>
    /// There is no preview mode for a type card: opening a type is the designer or
    /// its records, and the card already shows everything a preview dialog would.
    /// </summary>
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

    /// <summary>
    /// Asks for confirmation before deleting a service type, records and all.
    /// </summary>
    /// <remarks>
    /// The record count is part of the question, so it is gathered before the
    /// dialog opens - the confirmation is the one place the reach of a delete is
    /// spelled out, and it cannot say "and its 12 records" after the fact.
    /// </remarks>
    public async Task RequestDeleteAsync(Service service)
    {
        int count;
        try
        {
            count = await _directory.CountRecordsAsync(service.Id).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error("Counting a service type's records failed", error);
            return;
        }

        _pendingTypeDeleteBody = count == 0
            ? $"{service.DisplayName} will be removed from the catalog. "
              + "This cannot be undone."
            : $"{service.DisplayName}, its {count} record(s) and their tables will be "
              + "removed. This cannot be undone.";

        _pendingRecordDelete = null;
        PendingDelete = service;
        OnPropertyChanged(nameof(ConfirmDeleteTitle));
        OnPropertyChanged(nameof(ConfirmDeleteBody));
        IsConfirmingDelete = true;
    }

    /// <summary>
    /// Opens a service type: its records if it has been designed, the designer if
    /// it has not.
    /// </summary>
    /// <remarks>
    /// This is "the first time you open it". A type with no structure has nothing
    /// to show, so the first open is the moment to ask what it should hold - and
    /// the designed structure is what every later open reads.
    /// </remarks>
    public async Task OpenTypeAsync(Service service)
    {
        ServiceDefinition? definition;
        try
        {
            definition = await _directory.GetDefinitionAsync(service.Id).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error($"Opening the '{service.DisplayName}' type failed", error);
            State = DirectoryState.Error;
            return;
        }

        if (definition?.IsConfigured == true)
        {
            await EnterTypeAsync(service, definition).ConfigureAwait(true);
        }
        else
        {
            // The first open of a type is the designer, not the records: there is
            // no structure to show yet, so the open becomes the question "what
            // should a record of this type hold?"
            _designerTarget = service;
            Designer.ShowSetup(service.DisplayName);
            IsDesignerOpen = true;
        }
    }

    /// <summary>Reopens the designer over a type that already has a structure.</summary>
    [RelayCommand]
    public async Task EditStructureAsync()
    {
        if (_selectedType is not { } type)
        {
            return;
        }

        try
        {
            ServiceDefinition? definition =
                await _directory.GetDefinitionAsync(type.Id).ConfigureAwait(true);

            _designerTarget = type;
            Designer.ShowEdit(type.DisplayName, definition ?? new ServiceDefinition());
        }
        catch (Exception error)
        {
            AppLog.Error($"Loading the '{type.DisplayName}' structure failed", error);
            return;
        }

        IsDesignerOpen = true;
    }

    /// <summary>
    /// Saves the designed structure, then lands in the type's records.
    /// </summary>
    /// <remarks>
    /// From the first-open flow this is the whole journey: design the type, arrive
    /// in it. From a later edit it refreshes the records instead, because the
    /// records are already on screen and a field added to the structure is a field
    /// their cards should start showing.
    /// </remarks>
    public async Task SaveStructureAsync()
    {
        if (_designerTarget is not { } type || !Designer.CanSave)
        {
            return;
        }

        ServiceDefinition definition = Designer.ToDefinition();
        definition.Id = type.Id;

        try
        {
            await _directory.SaveDefinitionAsync(definition).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            AppLog.Error($"Saving the '{type.DisplayName}' structure failed", error);
            return;
        }

        // The store stamped it on the first save; carry the moment here too, so the
        // in-memory copy answers IsConfigured the way the stored row now does.
        definition.ConfiguredAt ??= DateTimeOffset.Now;

        IsDesignerOpen = false;

        if (_selectedType is { } drilled && drilled.Id == type.Id)
        {
            // A later edit, over records already on screen.
            _definition = definition;
            OnPropertyChanged(nameof(Structure));

            await LoadRecordsAsync().ConfigureAwait(true);
        }
        else
        {
            // The first save of a type: the structure exists, so the open that
            // asked for it completes by arriving in the records.
            await EnterTypeAsync(type, definition).ConfigureAwait(true);
        }
    }

    /// <summary>Returns from a type's records to the type list.</summary>
    [RelayCommand]
    public void GoBack()
    {
        IsDrilled = false;
        _recordCts?.Cancel();
        _recordsLoad = null;
        Records.ClearSafely();
        RecordState = DirectoryState.Loading;
    }

    private async Task EnterTypeAsync(Service service, ServiceDefinition definition)
    {
        // The relationship options, before any card or picker needs them. Three
        // small queries on an open connection - a personal vault, not a warehouse.
        try
        {
            IReadOnlyList<Person> people = await _people.GetOptionsAsync().ConfigureAwait(true);
            IReadOnlyList<Website> websites =
                (await _accounts.GetWebsiteOptionsAsync().ConfigureAwait(true));
            IReadOnlyList<Account> accounts = await _accounts.GetOptionsAsync().ConfigureAwait(true);

            _choices = BuildChoices(people, websites, accounts);
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the relationship options failed", error);
            RecordState = DirectoryState.Error;
            return;
        }

        _selectedType = service;
        _definition = definition;
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(Structure));
        OnPropertyChanged(nameof(Choices));

        IsDrilled = true;

        await LoadRecordsAsync().ConfigureAwait(true);
    }

    /// <summary>Loads a type's records. Safe to call again; a second call cancels the first.</summary>
    public Task LoadRecordsAsync()
        => _recordsLoad = LoadRecordsCoreAsync(showLoading: true);

    /// <summary>
    /// Loads the records unless a load is already running or finished.
    /// </summary>
    /// <remarks>
    /// The records layer sits in the tree from the start, so its Loaded fires at
    /// start-up too - without the guard, the app would query the records of type
    /// 0 and file the empty answer as the state of a screen nobody is on.
    /// </remarks>
    public Task EnsureRecordsLoadedAsync()
        => _selectedType is null ? Task.CompletedTask : _recordsLoad ??= LoadRecordsAsync();

    private async Task LoadRecordsCoreAsync(bool showLoading)
    {
        if (_selectedType is null)
        {
            return;
        }

        // Cancelled but not disposed, for the same reason as the type list's source.
        _recordCts?.Cancel();
        _recordCts = new CancellationTokenSource();
        CancellationToken token = _recordCts.Token;

        int generation = unchecked(++_recordGeneration);

        if (showLoading)
        {
            RecordState = DirectoryState.Loading;
        }

        try
        {
            (IReadOnlyList<ServiceRecord> page, int total) =
                await _directory.GetRecordPageWithTotalAsync(
                    _selectedType?.Id ?? 0, 0, PageSize, RecordSearchText, token)
                    .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _recordGeneration)
            {
                return;
            }

            Records.ClearSafely();
            foreach (ServiceRecord record in page)
            {
                Records.Add(record);
            }

            HasMoreRecords = Records.Count < total;

            bool searching = !string.IsNullOrWhiteSpace(RecordSearchText);
            RecordState = total == 0 && !searching
                ? DirectoryState.Empty
                : DirectoryState.Ready;

            if (HasMoreRecords)
            {
                IdleWork.OnUiIdle(
                    _dispatcher,
                    "Prefetching the next page of records",
                    () => LoadMoreRecordsAsync().Observe("Prefetching the next page of records"));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the state now.
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.LoadRecordsCoreAsync failed", error);
            HasMoreRecords = false;
            RecordState = DirectoryState.Error;
            Records.ClearSafely();
        }
    }

    /// <summary>Fetches the next page of records as the end of the list nears.</summary>
    public async Task LoadMoreRecordsAsync()
    {
        if (_loadingMoreRecords || !HasMoreRecords || RecordState != DirectoryState.Ready)
        {
            return;
        }

        _loadingMoreRecords = true;

        int generation = _recordGeneration;
        CancellationToken token = _recordCts?.Token ?? CancellationToken.None;

        try
        {
            (IReadOnlyList<ServiceRecord> page, int total) = await _directory
                .GetRecordPageWithTotalAsync(
                    _selectedType?.Id ?? 0, Records.Count, PageSize, RecordSearchText, token)
                .ConfigureAwait(true);

            if (generation != _recordGeneration || token.IsCancellationRequested)
            {
                return;
            }

            foreach (ServiceRecord record in page)
            {
                Records.Add(record);
            }

            HasMoreRecords = Records.Count < total;
        }
        catch (OperationCanceledException)
        {
            // A newer load owns the list.
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.LoadMoreRecordsAsync failed", error);
            HasMoreRecords = false;
        }
        finally
        {
            _loadingMoreRecords = false;
        }
    }

    [RelayCommand]
    private Task RetryRecordsAsync() => LoadRecordsAsync();

    /// <summary>Opens the add-record form for the drilled type.</summary>
    [RelayCommand]
    public void AddRecord()
    {
        if (_definition is not { } definition || _selectedType is not { } type)
        {
            return;
        }

        RecordDialog.ShowAdd(definition, type.DisplayName, _choices);
        IsRecordDialogOpen = true;
    }

    /// <summary>Opens a record with its table rows, in preview.</summary>
    public async Task OpenRecordPreviewAsync(ServiceRecord record)
    {
        if (_definition is not { } definition || _selectedType is not { } type)
        {
            return;
        }

        try
        {
            IReadOnlyList<ServiceRecord> rows =
                await _directory.GetTableRowsAsync(record.Id).ConfigureAwait(true);

            RecordDialog.ShowPreview(record, rows, definition, type.DisplayName, _choices);
        }
        catch (Exception error)
        {
            AppLog.Error("Loading a record's table rows failed", error);
            return;
        }

        IsRecordDialogOpen = true;
    }

    /// <summary>Opens a record with its table rows, in the edit form.</summary>
    public async Task OpenRecordEditAsync(ServiceRecord record)
    {
        if (_definition is not { } definition || _selectedType is not { } type)
        {
            return;
        }

        try
        {
            IReadOnlyList<ServiceRecord> rows =
                await _directory.GetTableRowsAsync(record.Id).ConfigureAwait(true);

            RecordDialog.ShowEdit(record, rows, definition, type.DisplayName, _choices);
        }
        catch (Exception error)
        {
            AppLog.Error("Loading a record's table rows failed", error);
            return;
        }

        IsRecordDialogOpen = true;
    }

    public void CloseRecordDialog() => IsRecordDialogOpen = false;

    /// <summary>
    /// Preview turns into edit in place; add and edit commit and close.
    /// </summary>
    public async Task CommitRecordDialogAsync()
    {
        if (RecordDialog.IsPreview)
        {
            RecordDialog.SwitchToEdit();
            return;
        }

        if (!RecordDialog.CanSave)
        {
            return;
        }

        RecordDialog.ToRecord(out ServiceRecord record, out List<ServiceRecord> rows);
        record.ServiceTypeId = _selectedType?.Id ?? 0;

        try
        {
            if (RecordDialog.Mode == ServiceRecordDialogMode.Add)
            {
                await _directory.AddRecordAsync(record, rows).ConfigureAwait(true);
            }
            else
            {
                await _directory.UpdateRecordAsync(record, rows).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("ServicesViewModel.CommitRecordDialogAsync failed", error);
            RecordState = DirectoryState.Error;
            IsRecordDialogOpen = false;
            return;
        }

        IsRecordDialogOpen = false;
        await LoadRecordsAsync().ConfigureAwait(true);
    }

    /// <summary>Asks for confirmation before deleting one record.</summary>
    public void RequestDeleteRecord(ServiceRecord record)
    {
        _pendingRecordDeleteBody =
            $"{(string.IsNullOrWhiteSpace(record.Title) ? $"Record {record.IdDisplay}" : record.Title)} "
            + "and its table rows will be removed. This cannot be undone.";

        PendingDelete = null;
        _pendingRecordDelete = record;
        OnPropertyChanged(nameof(ConfirmDeleteTitle));
        OnPropertyChanged(nameof(ConfirmDeleteBody));
        IsConfirmingDelete = true;
    }

    /// <summary>
    /// Brings a website created over the record form back into its pickers.
    /// </summary>
    /// <remarks>
    /// The options are re-read rather than patched, and the new site is selected by
    /// the dialog itself - it is the only one that knows which of its pickers the
    /// user went away from.
    /// </remarks>
    private async void OnWebsitesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WebsitesViewModel.IsDialogOpen)
            || _websites.IsDialogOpen
            || !IsRecordDialogOpen)
        {
            return;
        }

        try
        {
            IReadOnlyList<Website> websites =
                await _accounts.GetWebsiteOptionsAsync().ConfigureAwait(true);

            Dictionary<ServiceFieldKind, ServiceChoiceSource> sources = new(_choices.Sources)
            {
                [ServiceFieldKind.Website] = WebsiteChoices(websites),
            };

            _choices = new ServiceChoiceOptions(sources);
            OnPropertyChanged(nameof(Choices));

            RecordDialog.RefreshChoices(_choices);
        }
        catch (Exception error)
        {
            // The form still works with the options it had.
            AppLog.Error("Refreshing the record form's website options failed", error);
        }
    }

    private ServiceChoiceOptions BuildChoices(
        IReadOnlyList<Person> people,
        IReadOnlyList<Website> websites,
        IReadOnlyList<Account> accounts)
    {
        Dictionary<ServiceFieldKind, ServiceChoiceSource> sources = new()
        {
            [ServiceFieldKind.Person] = PersonChoices(people),
            [ServiceFieldKind.Website] = WebsiteChoices(websites),
            [ServiceFieldKind.Account] = AccountChoices(accounts),
        };

        return new ServiceChoiceOptions(sources);
    }

    private static ServiceChoiceSource PersonChoices(IReadOnlyList<Person> people)
    {
        List<string> labels = new(people.Count);
        Dictionary<string, int> ids = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> byId = new();

        foreach (Person person in people)
        {
            string label = person.DisplayName;

            if (ids.TryAdd(label, person.Id))
            {
                labels.Add(label);
                byId[person.Id] = label;
            }
        }

        return new ServiceChoiceSource(labels, ids, byId);
    }

    private static ServiceChoiceSource WebsiteChoices(IReadOnlyList<Website> websites)
    {
        List<string> labels = new(websites.Count);
        Dictionary<string, int> ids = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> byId = new();

        foreach (Website website in websites)
        {
            string label = website.PickerLabel;

            if (ids.TryAdd(label, website.Id))
            {
                labels.Add(label);
                byId[website.Id] = label;
            }
        }

        return new ServiceChoiceSource(labels, ids, byId);
    }

    private static ServiceChoiceSource AccountChoices(IReadOnlyList<Account> accounts)
    {
        List<string> labels = new(accounts.Count);
        Dictionary<string, int> ids = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> byId = new();

        foreach (Account account in accounts)
        {
            string label = string.IsNullOrWhiteSpace(account.Username)
                ? account.WebsiteDisplay
                : $"{account.Username} · {account.WebsiteDisplay}";

            if (ids.TryAdd(label, account.Id))
            {
                labels.Add(label);
                byId[account.Id] = label;
            }
        }

        return new ServiceChoiceSource(labels, ids, byId);
    }

    public void CancelDelete()
    {
        IsConfirmingDelete = false;
        PendingDelete = null;
    }

    public async Task ConfirmDeleteAsync()
    {
        Service? target = PendingDelete;
        ServiceRecord? record = _pendingRecordDelete;

        IsConfirmingDelete = false;
        PendingDelete = null;
        _pendingRecordDelete = null;

        if (record is not null)
        {
            try
            {
                await _directory.DeleteRecordAsync(record.Id).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                AppLog.Error("ServicesViewModel.ConfirmDeleteAsync failed", error);
                RecordState = DirectoryState.Error;
                return;
            }

            await LoadRecordsAsync().ConfigureAwait(true);
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
            AppLog.Error("ServicesViewModel.ConfirmDeleteAsync failed", error);
            State = DirectoryState.Error;
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Queries the records after the typing stops, not on every character.</summary>
    partial void OnRecordSearchTextChanged(string value)
    {
        _ = value;

        _recordSearchCts?.Cancel();
        _recordSearchCts = new CancellationTokenSource();

        _ = SearchRecordsAfterPauseAsync(_recordSearchCts.Token);
    }

    private async Task SearchRecordsAfterPauseAsync(CancellationToken token)
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
            await LoadRecordsCoreAsync(showLoading: false).ConfigureAwait(true);
        }
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
