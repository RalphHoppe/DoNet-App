using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Models;
using DoNet.Services;

namespace DoNet.ViewModels;

/// <summary>
/// The world the map draws: its fixed size and the latitude band it covers.
/// </summary>
/// <remarks>
/// The same two numbers live in the renderer (tools/render_world_map.py) and
/// the screen's math simulator (tools/simulate_map_screen.py); if one
/// changes, all three change together or the markers stop landing on their
/// countries.
/// </remarks>
public static class MapWorld
{
    /// <summary>The world's width, in world pixels.</summary>
    public const double Width = 2560;

    /// <summary>The world's height, in world pixels. The plate is 2.5:1.</summary>
    public const double Height = 1024;

    /// <summary>The northernmost latitude the plate carries.</summary>
    public const double LatTop = 84.0;

    /// <summary>The southernmost latitude the plate carries.</summary>
    public const double LatBottom = -60.0;

    /// <summary>How many times closer than the whole-earth fit the view may go.</summary>
    public const double ZoomHeadroom = 5.0;
}

/// <summary>What a marker on the map is standing for.</summary>
public enum MapMarkerKind
{
    /// <summary>A main VPS, in the all-mains overview.</summary>
    Main,

    /// <summary>Several main VPSs in one country, waiting for a choice.</summary>
    Cluster,

    /// <summary>Any VPS in an open network, the main included.</summary>
    Node,
}

/// <summary>
/// One marker on the map, in the world's own coordinates.
/// </summary>
/// <remarks>
/// The position is the country's anchor - the top of the country's shape -
/// placed once, when the scene is built. Nothing moves it afterwards: zoom
/// and pan move the view over the world, never the markers, which is the
/// whole difference between this map and the ones before it.
/// </remarks>
public sealed partial class MapMarkerItem : ObservableObject
{
    /// <summary>The marker's job, which decides its size and its ring.</summary>
    public MapMarkerKind Kind { get; init; }

    /// <summary>The marker's horizontal position, in world pixels.</summary>
    public double WorldX { get; init; }

    /// <summary>The marker's vertical position, in world pixels.</summary>
    public double WorldY { get; init; }

    /// <summary>The callout's word: the country, or the record's own name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The country whose flag the tile carries, when it has one.</summary>
    public string? FlagCountry { get; init; }

    /// <summary>The record behind the marker, when there is exactly one.</summary>
    public VpsRecord? Record { get; init; }

    /// <summary>The records behind a cluster marker, in pick order.</summary>
    public IReadOnlyList<VpsRecord>? Cluster { get; init; }

    /// <summary>How many mains a cluster marker holds.</summary>
    public int ClusterCount => Cluster?.Count ?? 0;

    /// <summary>True when this marker's record is the one being read.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One drawn connection between two markers, in world pixels.</summary>
public sealed class MapArcItem
{
    /// <summary>The connection's start.</summary>
    public double X1 { get; init; }

    /// <summary>The connection's start's height.</summary>
    public double Y1 { get; init; }

    /// <summary>The connection's end.</summary>
    public double X2 { get; init; }

    /// <summary>The connection's end's height.</summary>
    public double Y2 { get; init; }

    /// <summary>True when the connection runs deeper than a main's direct child.</summary>
    public bool Deep { get; init; }
}

/// <summary>
/// One row of the map panel's list: a VPS in words. Every member defaults or
/// is optional, because the XAML compiler constructs the class bare for its
/// metadata, which a required member would refuse (CS9035, in generated code).
/// </summary>
public sealed partial class MapRowItem : ObservableObject
{
    /// <summary>The record the row speaks for; the panel's rows always carry one.</summary>
    public VpsRecord? Record { get; init; }

    /// <summary>The row's title: the VPS's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The row's note: the country, or the honest absence of one.</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>The country whose flag the row carries, when it has one.</summary>
    public string? FlagCountry { get; init; }

    /// <summary>The row's right-hand note: what the record is to the others.</summary>
    public string Meta { get; init; } = string.Empty;

    /// <summary>How deep in the network the record sits; the main is level zero.</summary>
    public int Level { get; init; }

    /// <summary>True when the row's record is the one being read.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One read-only cell of the panel's detail section. Every member defaults,
/// because the XAML compiler constructs the class bare for its metadata.
/// </summary>
public sealed class MapDetailItem
{
    /// <summary>The cell's label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The cell's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The cell's attached unit, e.g. a port number beside its protocol.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>The cell's line icon.</summary>
    public string Icon { get; init; } = "Info";

    /// <summary>True when the value is masked until revealed.</summary>
    public bool IsSecret { get; init; }

    /// <summary>True when the value is worth a copy button.</summary>
    public bool ShowCopy { get; init; }
}

/// <summary>
/// One row of the chooser: a main VPS inside a clustered country. Every member
/// defaults or is optional, because the XAML compiler constructs the class
/// bare for its metadata.
/// </summary>
public sealed class MapChooserRow
{
    /// <summary>The record the row picks; the chooser's rows always carry one.</summary>
    public VpsRecord? Record { get; init; }

    /// <summary>The row's title: the VPS's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The row's note: how much hangs off the record.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The country whose flag the row carries.</summary>
    public string? FlagCountry { get; init; }
}

/// <summary>
/// The open chooser: the small panel that appears over a country whose main
/// VPSs cannot all be the tile.
/// </summary>
public sealed class MapChooserState
{
    /// <summary>The marker the chooser belongs to, for the view to sit beside.</summary>
    public MapMarkerItem? Anchor { get; init; }

    /// <summary>The country the choice is happening in.</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>The country whose flag the chooser's header carries.</summary>
    public string? FlagCountry { get; init; }

    /// <summary>The rows to pick from.</summary>
    public IReadOnlyList<MapChooserRow> Rows { get; init; } = Array.Empty<MapChooserRow>();
}

/// <summary>
/// Backs the VPS map screen: the world with the mains tiled on it, the
/// network under whichever main is opened, and the panel's words beside
/// both.
/// </summary>
/// <remarks>
/// <para>
/// The view model builds scenes, not pixels. Each transition - the overview
/// arriving, a network opening, a selection trading - produces a whole new
/// set of markers, arcs, rows and details, and swaps them in at once. The
/// view never has to reconcile a half-updated world, because it is never
/// shown one.
/// </para>
/// <para>
/// Positions are in the world's own coordinates and are final the moment
/// they are built: the country's anchor, placed once. Zoom and pan belong
/// to the view and the platform's scroller, and no marker ever moves for
/// them - the bug where a dot left its country for the edge of the screen
/// cannot happen here, because nothing recomputes a marker's position
/// after it is placed.
/// </para>
///</remarks>
public sealed partial class NetworkMapViewModel : ObservableObject
{
    private readonly IVpsDirectory _directory;

    /// <summary>The whole forest, rebuilt each open.</summary>
    private List<MapNode> _roots = new();

    /// <summary>Every node by its record's id, for chain walks.</summary>
    private Dictionary<int, MapNode> _nodesById = new();

    /// <summary>
    /// The record the open network belongs to, or null in the overview: a
    /// click on that same main is a selection, not a re-opening.
    /// </summary>
    private int? _networkMainId;

    /// <summary>The record whose details the panel is showing, when one is.</summary>
    private int? _selectedRecordId;

    /// <summary>The open scene's markers, by their record's id.</summary>
    private Dictionary<int, MapMarkerItem> _markersByRecordId = new();

    /// <summary>The open scene's rows, by their record's id.</summary>
    private Dictionary<int, MapRowItem> _rowsByRecordId = new();

    public NetworkMapViewModel(IVpsDirectory directory)
    {
        _directory = directory;
    }

    // ------------------------------------------------------------------
    // The scene, as the view receives it
    // ------------------------------------------------------------------

    /// <summary>True while the map screen is on show.</summary>
    [ObservableProperty]
    private bool _isDialogOpen;

    /// <summary>True while the records are being read.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when the records could not be read.</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>True when there are no VPS records at all.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>True while a network, rather than the overview, is on show.</summary>
    [ObservableProperty]
    private bool _isFocused;

    /// <summary>The markers of the current scene, in the world's own coordinates.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapMarkerItem>? _markers;

    /// <summary>The connections of the current scene, in the world's own coordinates.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapArcItem>? _arcs;

    /// <summary>The panel's rows: every main in the overview, the chain in a network.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapRowItem>? _rows;

    /// <summary>The panel's detail cells for the selected VPS.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapDetailItem>? _details;

    /// <summary>The open chooser, or null when none is open.</summary>
    [ObservableProperty]
    private MapChooserState? _chooser;

    /// <summary>The selected VPS's name, for the panel's detail card.</summary>
    [ObservableProperty]
    private string _selectedName = string.Empty;

    /// <summary>The selected VPS's country's flag, when it has one.</summary>
    [ObservableProperty]
    private string? _selectedFlagCountry;

    /// <summary>The selected VPS's country as a line of text.</summary>
    [ObservableProperty]
    private string _selectedCountryLabel = string.Empty;

    /// <summary>The selected VPS's place in the network, as a line of text.</summary>
    [ObservableProperty]
    private string _selectedSummary = string.Empty;

    /// <summary>How many mains could not be placed, for the footnote.</summary>
    [ObservableProperty]
    private int _offMapCount;

    /// <summary>True when the selected VPS has a flag to show.</summary>
    public bool HasSelectedFlag => SelectedFlagCountry is not null;

    /// <summary>True when any main could not be placed, for the footnote's visibility.</summary>
    public bool HasOffMap => OffMapCount > 0;

    /// <summary>The footnote itself: how many records are not on the map, and why.</summary>
    public string OffMapNote => $"{OffMapCount} not on the map - no country set";

    partial void OnSelectedFlagCountryChanged(string? value)
        => OnPropertyChanged(nameof(HasSelectedFlag));

    partial void OnOffMapCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOffMap));
        OnPropertyChanged(nameof(OffMapNote));
    }

    /// <summary>
    /// Opens the map: the screen shows at once, the records are read, and the
    /// overview lands when they arrive.
    /// </summary>
    public async Task OpenAsync()
    {
        IsDialogOpen = true;
        IsLoading = true;
        IsError = false;
        IsEmpty = false;

        try
        {
            IReadOnlyList<VpsRecord> records = await _directory.GetAllAsync()
                .ConfigureAwait(true);

            BuildForest(records);
            RenderOverview();
        }
        catch (Exception error)
        {
            AppLog.Error("Reading the VPS records for the map failed", error);
            _roots = new List<MapNode>();
            _nodesById = new Dictionary<int, MapNode>();
            IsError = true;
            Markers = null;
            Arcs = null;
            Rows = null;
            Details = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Closes the map.</summary>
    public void Close() => IsDialogOpen = false;

    /// <summary>
    /// Closes the map, as the rail's back button asks: the same close, as a
    /// command the button can carry while the map is the screen.
    /// </summary>
    [RelayCommand]
    private void CloseMap() => Close();

    /// <summary>
    /// Returns the map to its pre-unlock condition. Called when the app locks.
    /// </summary>
    public void Reset()
    {
        IsDialogOpen = false;
        IsLoading = false;
        IsError = false;
        IsEmpty = false;
        IsFocused = false;
        _networkMainId = null;
        _selectedRecordId = null;
        Chooser = null;
        Markers = null;
        Arcs = null;
        Rows = null;
        Details = null;
    }

    // ------------------------------------------------------------------
    // The two scenes
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the overview: one tile per country that holds a main, a count
    /// chip on the countries that hold several, and a row in the panel's
    /// list for every main there is.
    /// </summary>
    private void RenderOverview()
    {
        IsFocused = false;
        _networkMainId = null;
        _selectedRecordId = null;
        Chooser = null;

        List<MapMarkerItem> markers = new();
        List<MapRowItem> rows = new();
        Dictionary<int, MapMarkerItem> markersById = new();
        Dictionary<int, MapRowItem> rowsById = new();
        int offMap = 0;

        foreach (IGrouping<string, MapNode> group in _roots
                     .GroupBy(node => node.Record.Country.Trim())
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            string country = group.Key;

            if (CountryAnchors.For(country) is not { } anchor)
            {
                offMap += group.Count();
                continue;
            }

            (double x, double y) = Place(anchor);
            List<MapNode> mains = group.OrderBy(node => node.Record.Id).ToList();

            MapMarkerItem marker = new()
            {
                Kind = mains.Count == 1 ? MapMarkerKind.Main : MapMarkerKind.Cluster,
                WorldX = x,
                WorldY = y,
                Name = country,
                FlagCountry = country,
                Record = mains.Count == 1 ? mains[0].Record : null,
                Cluster = mains.Count == 1 ? null : mains.Select(node => node.Record).ToList(),
            };

            markers.Add(marker);

            if (marker.Record is { } single)
            {
                markersById[single.Id] = marker;
            }

            foreach (MapNode main in mains)
            {
                MapRowItem row = new()
                {
                    Record = main.Record,
                    Name = string.IsNullOrWhiteSpace(main.Record.Name) ? "VPS" : main.Record.Name,
                    Country = country,
                    FlagCountry = country,
                    Meta = main.TotalCount == 0
                        ? "No connected VPSs yet"
                        : $"{main.TotalCount} connected VPS{(main.TotalCount == 1 ? string.Empty : "s")}",
                    Level = 0,
                };

                rows.Add(row);
                rowsById[main.Record.Id] = row;
            }
        }

        OffMapCount = offMap;
        IsEmpty = _roots.Count == 0;

        _markersByRecordId = markersById;
        _rowsByRecordId = rowsById;

        Markers = markers;
        Arcs = null;
        Rows = rows;
        Details = null;
        SelectedName = string.Empty;
        SelectedFlagCountry = null;
        SelectedCountryLabel = string.Empty;
        SelectedSummary = string.Empty;
    }

    /// <summary>
    /// Builds the network view of one main: its whole chain as tiles, joined
    /// by arcs; the chain as the panel's list; the main's own details in the
    /// panel's card.
    /// </summary>
    private void RenderNetwork(MapNode main)
    {
        IsFocused = true;
        _networkMainId = main.Record.Id;
        Chooser = null;

        List<MapMarkerItem> markers = new();
        List<MapArcItem> arcs = new();
        List<MapRowItem> rows = new();
        Dictionary<int, MapMarkerItem> markersById = new();
        Dictionary<int, MapRowItem> rowsById = new();

        // The chain, depth first, in reading order. Records that share an
        // anchor step onto a deterministic diagonal - a shared country never
        // stacks its markers into one invisible pile, and a solitary marker
        // never moves off its anchor.
        Dictionary<(double X, double Y), int> taken = new();

        void Walk(MapNode node, int level)
        {
            VpsRecord record = node.Record;
            string country = record.Country.Trim();
            (double x, double y) = CountryAnchors.For(country) is { } anchor
                ? Place(anchor)
                : level == 0
                    ? (MapWorld.Width / 2, MapWorld.Height * 0.55)
                    : Place(CountryAnchors.For(node.Parent!.Record.Country.Trim())
                        ?? (latitude: 0, longitude: 0));

            (double X, double Y) key = (Math.Round(x, 1), Math.Round(y, 1));
            int n = taken.TryGetValue(key, out int count) ? count : 0;
            taken[key] = n + 1;

            MapMarkerItem marker = new()
            {
                Kind = MapMarkerKind.Node,
                WorldX = x + (n * 13),
                WorldY = y + (n * 13),
                Name = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
                FlagCountry = country.Length == 0 ? null : country,
                Record = record,
            };

            markers.Add(marker);
            markersById[record.Id] = marker;

            MapRowItem row = new()
            {
                Record = record,
                Name = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
                Country = country.Length == 0 ? "No country set" : country,
                FlagCountry = country.Length == 0 ? null : country,
                Meta = level == 0
                    ? (node.TotalCount == 0
                        ? "Main \u00B7 no connected VPSs"
                        : $"Main \u00B7 {node.Children.Count} direct \u00B7 {node.TotalCount} in the network")
                    : (string.IsNullOrWhiteSpace(record.NodeName)
                        ? "Connected VPS"
                        : $"Connected to {record.NodeName}")
                      + (node.Children.Count > 0
                          ? $" \u00B7 {node.Children.Count} of its own"
                          : string.Empty),
                Level = level,
            };

            rows.Add(row);
            rowsById[record.Id] = row;

            foreach (MapNode child in node.Children.OrderBy(child => child.Record.Id))
            {
                Walk(child, level + 1);
            }
        }

        Walk(main, 0);

        // The arcs, between each record and its parent, each taking the
        // short way across the world.
        foreach (MapMarkerItem marker in markers)
        {
            if (marker.Record is not { } record
                || record.NodeVpsId is not { } parentId
                || !markersById.TryGetValue(parentId, out MapMarkerItem? parent)
                || parent == marker)
            {
                continue;
            }

            bool deep = marker.Record.NodeVpsId != main.Record.Id;

            if (Math.Abs(marker.WorldX - parent.WorldX) > MapWorld.Width / 2)
            {
                double direction = marker.WorldX > parent.WorldX ? -1 : 1;
                arcs.Add(new MapArcItem
                {
                    X1 = parent.WorldX,
                    Y1 = parent.WorldY,
                    X2 = marker.WorldX + (direction * MapWorld.Width),
                    Y2 = marker.WorldY,
                    Deep = deep,
                });
                arcs.Add(new MapArcItem
                {
                    X1 = parent.WorldX - (direction * MapWorld.Width),
                    Y1 = parent.WorldY,
                    X2 = marker.WorldX,
                    Y2 = marker.WorldY,
                    Deep = deep,
                });
            }
            else
            {
                arcs.Add(new MapArcItem
                {
                    X1 = parent.WorldX,
                    Y1 = parent.WorldY,
                    X2 = marker.WorldX,
                    Y2 = marker.WorldY,
                    Deep = deep,
                });
            }
        }

        _markersByRecordId = markersById;
        _rowsByRecordId = rowsById;

        Markers = markers;
        Arcs = arcs;
        Rows = rows;

        SelectRecord(main.Record);
    }

    /// <summary>
    /// A country's anchor as a world position: the top of the country's
    /// shape, on the plate's own latitude band.
    /// </summary>
    private static (double X, double Y) Place((double Latitude, double Longitude) anchor)
    {
        double nx = (anchor.Longitude + 180.0) / 360.0;
        double ny = (MapWorld.LatTop - anchor.Latitude) / (MapWorld.LatTop - MapWorld.LatBottom);
        return (nx * MapWorld.Width, ny * MapWorld.Height);
    }

    // ------------------------------------------------------------------
    // Clicks
    // ------------------------------------------------------------------

    /// <summary>
    /// A marker was clicked: a cluster opens its chooser, a main in the
    /// overview opens its network, anything else trades the selection.
    /// </summary>
    public void ActivateMarker(MapMarkerItem marker)
    {
        switch (marker.Kind)
        {
            case MapMarkerKind.Cluster:
                if (marker.Cluster is { Count: > 1 } cluster)
                {
                    OpenChooser(marker, cluster);
                }
                break;

            default:
                if (marker.Record is { } record)
                {
                    if (!IsFocused)
                    {
                        OpenNetwork(record);
                    }
                    else if (record.Id == _networkMainId && _nodesById.TryGetValue(record.Id, out MapNode? main))
                    {
                        // The network's own main, clicked while its story is
                        // already open: the click selects it, rather than
                        // re-opening the network and replaying its arrival.
                        SelectRecord(record);
                    }
                    else
                    {
                        SelectRecord(record);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// A panel row was clicked: in the overview it opens the record's
    /// network, in a network it trades the selection.
    /// </summary>
    public void ActivateRow(MapRowItem row)
    {
        if (row.Record is not { } record)
        {
            return;
        }

        if (!IsFocused)
        {
            OpenNetwork(record);
        }
        else
        {
            SelectRecord(record);
        }
    }

    /// <summary>Opens the network of whichever main the record is.</summary>
    private void OpenNetwork(VpsRecord record)
    {
        if (_nodesById.TryGetValue(record.Id, out MapNode? main))
        {
            RenderNetwork(main);
        }
    }

    /// <summary>
    /// Trades the panel's story to a record: its card, its details, and the
    /// teal rings that mark it on the page and in the list.
    /// </summary>
    public void SelectRecord(VpsRecord record)
    {
        _selectedRecordId = record.Id;

        foreach (KeyValuePair<int, MapMarkerItem> pair in _markersByRecordId)
        {
            pair.Value.IsSelected = pair.Key == record.Id;
        }

        foreach (KeyValuePair<int, MapRowItem> pair in _rowsByRecordId)
        {
            pair.Value.IsSelected = pair.Key == record.Id;
        }

        SelectedName = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name;
        SelectedFlagCountry = string.IsNullOrWhiteSpace(record.Country) ? null : record.Country.Trim();
        SelectedCountryLabel = string.IsNullOrWhiteSpace(record.Country)
            ? "No country set"
            : record.Country;

        if (_nodesById.TryGetValue(record.Id, out MapNode? node))
        {
            SelectedSummary = record.NodeVpsId is null
                ? (node.TotalCount == 0
                    ? "No connected VPSs yet"
                    : $"{node.Children.Count} direct \u00B7 {node.TotalCount} in the network")
                : (string.IsNullOrWhiteSpace(record.NodeName)
                    ? "Connected VPS"
                    : $"Connected to {record.NodeName}")
                  + (node.Children.Count > 0
                      ? $" \u00B7 {node.Children.Count} of its own"
                      : string.Empty);
        }
        else
        {
            SelectedSummary = string.Empty;
        }

        Details = BuildDetails(record);
    }

    /// <summary>
    /// The panel's detail cells for a record: the preview's own fields, with
    /// the never-filled ones left out rather than shown blank.
    /// </summary>
    private static IReadOnlyList<MapDetailItem> BuildDetails(VpsRecord record)
    {
        VpsSystemInfo system = record.System ?? new VpsSystemInfo();
        List<MapDetailItem> cells = new();

        void Add(string label, string value, string icon,
            string unit = "", bool secret = false, bool copy = false)
        {
            if (value.Trim().Length > 0 || unit.Trim().Length > 0)
            {
                cells.Add(new MapDetailItem
                {
                    Label = label,
                    Value = value,
                    Unit = unit,
                    Icon = icon,
                    IsSecret = secret,
                    ShowCopy = copy,
                });
            }
        }

        Add("IP ADDRESS", system.Ip, "Globe", copy: true);
        Add("PORT", system.Port, "Key", unit: system.PortNumber, copy: true);
        Add("OS", system.Os, "Info", copy: true);
        Add("USERNAME", system.Username, "User", copy: true);
        Add("USER PASSWORD", system.UserPassword, "Lock", secret: true, copy: true);
        Add("ROOT USER", system.RootUser, "User", copy: true);
        Add("ROOT PASSWORD", system.RootPassword, "Lock", secret: true, copy: true);
        Add("CPU", system.Cpu, "Info");
        Add("MEMORY (RAM)", system.Memory, "Info");
        Add("DISK", system.Disk, "Info");
        Add("BANDWIDTH", system.Bandwidth, "Info");
        Add("OUT DATE", record.OutDate, "Calendar");

        if (record.CycleLabel.Length > 0)
        {
            Add("CYCLE", record.CycleLabel, "Clock");
        }

        if (record.Balance is { } balance)
        {
            Add(
                "BALANCE",
                $"{balance:0.##} {record.Currency}".Trim(),
                "Bitcoin",
                unit: record.BalanceBtc is { } btc ? $"{btc:0.########} BTC" : string.Empty);
        }

        return cells;
    }

    /// <summary>Returns the map to the overview, with its view of the whole earth.</summary>
    public void BackToOverview() => RenderOverview();

    /// <summary>A tap on the map itself, away from any marker: the chooser steps aside.</summary>
    public void HandleBackgroundTap() => Chooser = null;

    /// <summary>The chooser closes when the view moves, so it never floats loose of its marker.</summary>
    public void CloseChooser() => Chooser = null;

    // ------------------------------------------------------------------
    // The chooser
    // ------------------------------------------------------------------

    /// <summary>Opens the chooser over a cluster marker.</summary>
    private void OpenChooser(MapMarkerItem marker, IReadOnlyList<VpsRecord> cluster)
    {
        Chooser = new MapChooserState
        {
            Anchor = marker,
            Country = marker.FlagCountry ?? string.Empty,
            FlagCountry = marker.FlagCountry,
            Rows = cluster
                .Select(record => new MapChooserRow
                {
                    Record = record,
                    Name = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
                    Detail = _nodesById.TryGetValue(record.Id, out MapNode? node) && node.TotalCount > 0
                        ? $"{node.TotalCount} connected VPS{(node.TotalCount == 1 ? string.Empty : "s")}"
                        : "No connected VPSs",
                    FlagCountry = marker.FlagCountry,
                })
                .ToList(),
        };
    }

    /// <summary>One of the chooser's rows was picked: open that main's network.</summary>
    public void ChooseMain(VpsRecord record)
    {
        Chooser = null;
        OpenNetwork(record);
    }

    // ------------------------------------------------------------------
    // The forest
    // ------------------------------------------------------------------

    /// <summary>One record's place in the node chain.</summary>
    private sealed class MapNode
    {
        /// <summary>The record this node is.</summary>
        public required VpsRecord Record { get; init; }

        /// <summary>The records hanging directly off this one.</summary>
        public List<MapNode> Children { get; } = new();

        /// <summary>The node this one hangs off, or null at the top.</summary>
        public MapNode? Parent { get; set; }

        /// <summary>How deep the node sits; a main is zero.</summary>
        public int Level { get; set; }

        /// <summary>How many records hang off this one, at any depth.</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// Assembles the records into the forest of mains and their chains.
    /// </summary>
    /// <remarks>
    /// A record whose node points at a record that is not there, or around in
    /// a circle, is promoted to a main: chains like that are the store's to
    /// repair, and hiding the records would look like the map losing them.
    /// </remarks>
    private void BuildForest(IReadOnlyList<VpsRecord> records)
    {
        Dictionary<int, MapNode> nodes = new();

        foreach (VpsRecord record in records)
        {
            nodes[record.Id] = new MapNode { Record = record };
        }

        List<MapNode> roots = new();

        foreach (MapNode node in nodes.Values)
        {
            int? parent = node.Record.NodeVpsId;

            if (parent is { } parentId
                && parentId != node.Record.Id
                && nodes.TryGetValue(parentId, out MapNode? above))
            {
                above.Children.Add(node);
                node.Parent = above;
            }
            else
            {
                roots.Add(node);
            }
        }

        // Whatever the roots cannot reach is a circle or hangs off one. Each
        // unreachable node is detached from its parent and stands as a main
        // itself; the pieces that legitimately hung off it come along.
        HashSet<MapNode> reached = new();
        Stack<MapNode> walking = new(roots);

        while (walking.Count > 0)
        {
            MapNode current = walking.Pop();

            if (!reached.Add(current))
            {
                continue;
            }

            foreach (MapNode child in current.Children)
            {
                walking.Push(child);
            }
        }

        foreach (MapNode node in nodes.Values.Where(node => !reached.Contains(node)))
        {
            node.Parent?.Children.Remove(node);
            node.Parent = null;
            roots.Add(node);
        }

        foreach (MapNode root in roots)
        {
            CountLevels(root, level: 0);
        }

        _roots = roots;
        _nodesById = nodes;
    }

    /// <summary>Sets the levels and counts of a subtree.</summary>
    private static int CountLevels(MapNode node, int level)
    {
        node.Level = level;

        int total = 0;

        foreach (MapNode child in node.Children)
        {
            total += 1 + CountLevels(child, level + 1);
        }

        node.TotalCount = total;
        return total;
    }
}
