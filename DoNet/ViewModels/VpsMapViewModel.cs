using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Contracts;
using DoNet.Models;
using DoNet.Services;

namespace DoNet.ViewModels;

/// <summary>What a dot on the map is standing for.</summary>
public enum MapPinKind
{
    /// <summary>One main VPS, in the all-mains overview.</summary>
    Main,

    /// <summary>Several main VPSs in one country, waiting for a choice.</summary>
    Cluster,

    /// <summary>The main a click opened the network of.</summary>
    TreeMain,

    /// <summary>A VPS connected into the open network.</summary>
    Node,
}

/// <summary>
/// One dot on the map, and everything the dot shows and knows.
/// </summary>
/// <remarks>
/// <para>
/// The position is carried twice, on purpose. <see cref="Nx"/> and
/// <see cref="Ny"/> are the anchor in the map's own terms - where on the
/// earth the dot belongs, 0 to 1 across the equirectangular plate - set once
/// with the data. <see cref="X"/> and <see cref="Y"/> are pixels, filled by
/// the layout pass whenever the map is sized, resized, or its view changes.
/// The anchor is the truth; the pixels are just the anchor wearing the
/// current size.
///</para>
/// <para>
/// Dots that share an anchor - children in the same country as their parent,
/// for instance - are spread by the layout pass into a row under it, so no
/// two records ever sit drawn on top of each other.
///</para>
///</remarks>
public sealed partial class MapPin : ObservableObject
{
    /// <summary>The dot's job, which decides its size and its styling.</summary>
    public MapPinKind Kind { get; init; }

    /// <summary>The dot's diameter in pixels, from its kind and level.</summary>
    public int DotSize { get; init; }

    /// <summary>The arrival order, for the staggered entrance.</summary>
    public int Index { get; init; }

    /// <summary>The words under the dot: a country in the overview, a name in a network.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The country whose flag the dot carries, or null when the record has none.</summary>
    public string? FlagCountry { get; init; }

    /// <summary>How deep in the network the dot sits; the main is level zero.</summary>
    public int Level { get; init; }

    /// <summary>The record behind the dot, when there is exactly one.</summary>
    public VpsRecord? Record { get; init; }

    /// <summary>The records behind a cluster dot, in pick order.</summary>
    public IReadOnlyList<VpsRecord>? Cluster { get; init; }

    /// <summary>The tooltip's first line: the country, or the honest absence of one.</summary>
    public string TooltipTitle { get; init; } = string.Empty;

    /// <summary>The tooltip's second line: the record's own name.</summary>
    public string TooltipDetail { get; init; } = string.Empty;

    /// <summary>The tooltip's third line: what the dot is in relation to the others.</summary>
    public string TooltipMeta { get; init; } = string.Empty;

    /// <summary>Where on the earth the dot is anchored, 0 to 1 west to east.</summary>
    public double Nx { get; init; }

    /// <summary>Where on the earth the dot is anchored, 0 to 1 north to south.</summary>
    public double Ny { get; init; }

    /// <summary>The dot's horizontal centre in pixels, once laid out.</summary>
    [ObservableProperty]
    private double _x;

    /// <summary>The dot's vertical centre in pixels, once laid out.</summary>
    [ObservableProperty]
    private double _y;

    /// <summary>Whether the tooltip opens above the dot rather than under it.</summary>
    [ObservableProperty]
    private bool _tooltipAbove = true;

    /// <summary>
    /// How far the label sits below its rest under the pin's tip, when the
    /// nudge had to step it past another label.
    /// </summary>
    public double LabelDrop { get; set; }
}

/// <summary>
/// One drawn connection between two dots, in pixels: a segment of the line
/// from a parent to its child.
/// </summary>
/// <remarks>
/// A connection whose shortest way across the map crosses the edge where
/// longitude turns over is drawn as two segments, one leaving each side -
/// which is why a connection is segments, plural, and not one line.
///</remarks>
public sealed partial class MapLine : ObservableObject
{
    /// <summary>The segment's start, in pixels.</summary>
    [ObservableProperty]
    private double _x1;

    /// <summary>The segment's start, in pixels.</summary>
    [ObservableProperty]
    private double _y1;

    /// <summary>The segment's end, in pixels.</summary>
    [ObservableProperty]
    private double _x2;

    /// <summary>The segment's end, in pixels.</summary>
    [ObservableProperty]
    private double _y2;
}

/// <summary>One row of the chooser: a main VPS a country holds several of.</summary>
public sealed class ChooserRow
{
    /// <summary>The record the row stands for.</summary>
    public required VpsRecord Record { get; init; }

    /// <summary>The row's title: the VPS's name.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The row's note: how much hangs off it.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The country whose flag the row carries.</summary>
    public string? FlagCountry { get; init; }
}

/// <summary>
/// The chooser itself: the small panel that opens over a country whose main
/// VPSs cannot all be the dot.
/// </summary>
public sealed partial class MapChooser : ObservableObject
{
    /// <summary>The country the choice is happening in.</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>The country whose flag the header carries.</summary>
    public string? FlagCountry { get; init; }

    /// <summary>The rows to pick from.</summary>
    public IReadOnlyList<ChooserRow> Rows { get; init; } = Array.Empty<ChooserRow>();

    /// <summary>The panel's left edge in pixels, once laid out.</summary>
    [ObservableProperty]
    private double _x;

    /// <summary>The panel's top edge in pixels, once laid out.</summary>
    [ObservableProperty]
    private double _y;
}

/// <summary>
/// Backs the VPS map: the whole earth with the mains dotted on it, and the
/// network under whichever main gets clicked.
///</summary>
/// <remarks>
/// <para>
/// The data is the records themselves, read once when the map opens - a modal
/// map cannot race the forms that edit what it shows. The structure is the
/// node chain the VPS form already keeps: a record with no node above it is a
/// main; the rest hang off their <see cref="VpsRecord.NodeVpsId"/> as deep as
/// the chain goes. A chain that points at a missing record, or walks in a
/// circle, is promoted to a main of its own rather than quietly vanishing -
/// a record the user can see in the list should be a record they can see on
/// the map.
///</para>
/// <para>
/// The two views are the two things the user asked the map for. The overview
/// dots the mains, one dot per country; a country holding several wears a
/// badge with the count and opens the chooser, because a pile of overlapping
/// dots is not a choice. Clicking a dot trades the overview for that main's
/// network: every record under it, at its own country's anchor, joined to
/// its parent by a line - the main carrying its own size, its ring and its
/// tag, so it can never be mistaken for one of its own children.
///</para>
/// <para>
/// Positions are computed here and only here, so the dots, the lines and the
/// chooser can never disagree about where anything is: the view hands over
/// its size and reads back pixels. Lines take the short way across the map -
/// a connection from New Zealand to Hawaii crosses the edge where the map
/// ends and restarts, and is drawn as a segment leaving each side.
///</para>
///</remarks>
public sealed partial class VpsMapViewModel : ObservableObject
{
    /// <summary>The view's zoom limits: one is the whole plate, eight the closest.</summary>
    private const double MaxZoom = 8;

    /// <summary>One step of the zoom buttons.</summary>
    private const double ZoomStep = 1.5;

    /// <summary>The dot diameters, by job.</summary>
    private const int MainDot = 34;

    private const int ClusterDot = 34;

    private const int TreeMainDot = 40;

    private const int NodeDot = 28;

    /// <summary>The horizontal gap between dots spread into a row under a shared anchor.</summary>
    private const double SpreadSpacing = 64;

    /// <summary>How far under the anchor a spread row sits.</summary>
    private const double SpreadDrop = 56;

    private readonly IVpsDirectory _directory;

    /// <summary>The whole forest, rebuilt each open.</summary>
    private List<MapNode> _roots = new();

    /// <summary>Every node by its record's id, for focus lookups.</summary>
    private Dictionary<int, MapNode> _nodesById = new();

    /// <summary>The connections of the current view, as pin pairs.</summary>
    private List<(MapPin Parent, MapPin Child, int Level)> _edges = new();

    /// <summary>The pin the open chooser belongs to.</summary>
    private MapPin? _chooserPin;

    /// <summary>The host's size, the last time the view reported it.</summary>
    private (double Width, double Height) _hostSize;

    /// <summary>The map's rectangle inside the host at rest, in pixels.</summary>
    private (double X, double Y, double W, double H) _mapRect;

    /// <summary>The view's pan, in pixels, away from the resting rectangle.</summary>
    private double _panX;

    /// <summary>The view's pan, in pixels, away from the resting rectangle.</summary>
    private double _panY;

    /// <summary>
    /// Whether a network was built before the view had a size to be fitted
    /// into, and the fit is still owed.
    /// </summary>
    private bool _fitPending;

    public VpsMapViewModel(IVpsDirectory directory)
    {
        _directory = directory;
    }

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    /// <summary>True while the dialog is on screen.</summary>
    [ObservableProperty]
    private bool _isDialogOpen;

    /// <summary>True while the records are being read.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when the store refused.</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>True when there are no records at all to plot.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>True while a network is open rather than the overview.</summary>
    [ObservableProperty]
    private bool _isFocused;

    /// <summary>True while the view can go closer.</summary>
    public bool CanZoomIn => Zoom < MaxZoom - 0.001;

    /// <summary>True while the view can pull back further.</summary>
    public bool CanZoomOut => Zoom > 1.0 + 0.001;

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// The plate's rectangle under the current view, in host pixels: what the
    /// image draws into, and the space the dots and lines are placed in.
    /// </summary>
    /// <remarks>
    /// This changes exactly when the view transform does - a resize, a zoom,
    /// a pan - so its change notification is the view's one cue to re-seat
    /// everything it holds in host pixels: the image, the dots, the
    /// connections, the chooser.
    /// </remarks>
    public (double X, double Y, double W, double H) ImageRect
        => _hostSize.Width <= 0
            ? (0, 0, 0, 0)
            : (_mapRect.X + _panX, _mapRect.Y + _panY, _mapRect.W * Zoom, _mapRect.H * Zoom);

    /// <summary>The open chooser, or null when none is open.</summary>
    [ObservableProperty]
    private MapChooser? _chooser;

    /// <summary>The focused main's name, for the header card.</summary>
    [ObservableProperty]
    private string _focusedName = string.Empty;

    /// <summary>The focused main's country's flag, when it has one.</summary>
    [ObservableProperty]
    private string? _focusedFlagCountry;

    /// <summary>The focused main's country as a line of text.</summary>
    [ObservableProperty]
    private string _focusedCountryLabel = string.Empty;

    /// <summary>The focused main's counts, as a line of text.</summary>
    [ObservableProperty]
    private string _focusedSummary = string.Empty;

    /// <summary>The view's zoom: one is the whole plate, MaxZoom the closest.</summary>
    [ObservableProperty]
    private double _zoom = 1.0;

    /// <summary>How many mains could not be placed, for the footer's note.</summary>
    [ObservableProperty]
    private int _offMapCount;

    /// <summary>True when any main could not be placed, for the note's visibility.</summary>
    public bool HasOffMap => OffMapCount > 0;

    /// <summary>The footnote itself: how many records are not on the map, and why.</summary>
    public string OffMapNote => $"{OffMapCount} not on the map - no country set";

    partial void OnOffMapCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOffMap));
        OnPropertyChanged(nameof(OffMapNote));
    }

    /// <summary>The dots of the current view.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapPin>? _pins;

    /// <summary>The direct connections, as pixel segments.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapLine>? _lines;

    /// <summary>The deeper connections, as pixel segments.</summary>
    [ObservableProperty]
    private IReadOnlyList<MapLine>? _deepLines;

    /// <summary>True while the chooser panel should be on show.</summary>
    public bool HasChooser => Chooser is not null;

    partial void OnChooserChanged(MapChooser? value)
        => OnPropertyChanged(nameof(HasChooser));

    // ------------------------------------------------------------------
    // Open, close, reset
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the map: the panel shows at once, the records are read, and the
    /// overview lands when they arrive.
    /// </summary>
    public async Task OpenAsync()
    {
        IsDialogOpen = true;
        IsLoading = true;
        IsError = false;
        IsEmpty = false;

        CloseChooser();
        ResetView();

        try
        {
            IReadOnlyList<VpsRecord> records = await _directory.GetAllAsync()
                .ConfigureAwait(true);

            BuildForest(records);
            BuildOverview();
        }
        catch (Exception error)
        {
            AppLog.Error("Reading the VPS records for the map failed", error);
            _roots = new List<MapNode>();
            _nodesById = new Dictionary<int, MapNode>();
            IsError = true;
            Pins = null;
            Lines = null;
            DeepLines = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Closes the map.</summary>
    public void Close() => IsDialogOpen = false;

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
        CloseChooser();
        Pins = null;
        Lines = null;
        DeepLines = null;
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

    // ------------------------------------------------------------------
    // The two views
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the overview: one dot per country that holds a main, a badge on
    /// the countries that hold several.
    /// </summary>
    private void BuildOverview()
    {
        IsFocused = false;
        FocusedName = string.Empty;
        FocusedFlagCountry = null;
        FocusedCountryLabel = string.Empty;
        FocusedSummary = string.Empty;

        List<MapPin> pins = new();
        int offMap = 0;
        int index = 0;

        foreach (IGrouping<string, MapNode> group in _roots
                     .GroupBy(node => node.Record.Country.Trim())
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            string country = group.Key;

            if (country.Length == 0)
            {
                offMap += group.Count();
                continue;
            }

            (double latitude, double longitude)? anchor = CountryAnchors.For(country);

            if (anchor is null)
            {
                offMap += group.Count();
                continue;
            }

            (double nx, double ny) = ToNormalized(anchor.Value);
            List<MapNode> mains = group.OrderBy(node => node.Record.Id).ToList();

            MapPin pin = mains.Count == 1
                ? MainPin(mains[0], country, nx, ny, index++)
                : ClusterPin(mains, country, nx, ny, index++);

            pins.Add(pin);
        }

        OffMapCount = offMap;
        IsEmpty = _roots.Count == 0;
        _edges = new List<(MapPin, MapPin, int)>();

        // Laid out before they are published, so the dots are seated the
        // moment the view builds them rather than arriving at the corner and
        // waiting for the next resize to be placed.
        Layout(pins);

        Pins = pins;
    }

    /// <summary>The overview dot for a country with one main.</summary>
    private static MapPin MainPin(MapNode main, string country, double nx, double ny, int index)
        => new()
        {
            Kind = MapPinKind.Main,
            DotSize = MainDot,
            Index = index,
            Label = country.ToUpperInvariant(),
            FlagCountry = country,
            Record = main.Record,
            Nx = nx,
            Ny = ny,
            TooltipTitle = country,
            TooltipDetail = string.IsNullOrWhiteSpace(main.Record.Name)
                ? "Main VPS"
                : main.Record.Name,
            TooltipMeta = main.TotalCount == 0
                ? "No connected VPSs yet"
                : $"{main.TotalCount} connected VPS{(main.TotalCount == 1 ? string.Empty : "s")}",
        };

    /// <summary>The overview dot for a country with several mains.</summary>
    private static MapPin ClusterPin(
        List<MapNode> mains, string country, double nx, double ny, int index)
        => new()
        {
            Kind = MapPinKind.Cluster,
            DotSize = ClusterDot,
            Index = index,
            Label = country.ToUpperInvariant(),
            FlagCountry = country,
            Cluster = mains.Select(node => node.Record).ToList(),
            Nx = nx,
            Ny = ny,
            TooltipTitle = country,
            TooltipDetail = $"{mains.Count} main VPSs in this country",
            TooltipMeta = "Click to choose which one",
        };

    /// <summary>
    /// Builds the network view of one main: its whole chain, each record at
    /// its own country's anchor, joined to its parent by a line.
    /// </summary>
    private void BuildNetwork(MapNode main)
    {
        IsFocused = true;
        FocusedName = main.Record.Name;
        FocusedFlagCountry = CountryOrNull(main.Record.Country);
        FocusedCountryLabel = string.IsNullOrWhiteSpace(main.Record.Country)
            ? "No country set"
            : main.Record.Country;
        FocusedSummary = main.TotalCount == 0
            ? "No connected VPSs yet"
            : $"{main.Children.Count} direct \u00B7 {main.TotalCount} in the network";

        List<MapPin> pins = new() { NetworkPin(main, main, index: 0) };
        List<(MapPin, MapPin, int)> edges = new();
        int index = 1;

        WalkChain(main, pins[0], pins, edges, ref index);

        _edges = edges;

        // The network frames itself: whatever the view was showing, the new
        // network's dots decide where it looks. A fit before the layout, so
        // the pins are placed once, already in their framed positions.
        FitToPins(pins);

        Layout(pins);

        Pins = pins;
    }

    /// <summary>Collects a main's chain, depth first, in reading order.</summary>
    private void WalkChain(
        MapNode parent,
        MapPin parentPin,
        List<MapPin> pins,
        List<(MapPin, MapPin, int)> edges,
        ref int index)
    {
        foreach (MapNode child in parent.Children.OrderBy(node => node.Record.Id))
        {
            MapPin pin = NetworkPin(child, parent, index++);
            pins.Add(pin);
            edges.Add((parentPin, pin, child.Level));

            WalkChain(child, pin, pins, edges, ref index);
        }
    }

    /// <summary>One dot of a network, wherever the record's country puts it.</summary>
    /// <remarks>
    /// A record without a usable country is anchored to its parent's anchor
    /// instead, so it arrives in the row under its parent rather than
    /// vanishing - the same promise the overview makes with its footnote.
    /// </remarks>
    private static MapPin NetworkPin(MapNode node, MapNode parent, int index)
    {
        VpsRecord record = node.Record;
        string country = record.Country.Trim();
        (double latitude, double longitude)? anchor = country.Length == 0
            ? null
            : CountryAnchors.For(country);

        // The fallback anchor: the parent's, so the shared-anchor spread takes
        // care of the row. A main without a country sits mid-Atlantic, in the
        // middle of the map where its network can still gather around it.
        (double nx, double ny) = anchor is { } point
            ? ToNormalized(point)
            : node == parent
                ? (0.5, 0.55)
                : NormalizedOf(parent);

        bool isMain = node.Level == 0;
        string title = country.Length == 0 ? "No country set" : country;

        string meta = isMain
            ? $"MAIN \u00B7 {node.Children.Count} direct \u00B7 {node.TotalCount} in the network"
            : $"Connected to {parent.Record.Name}"
              + (node.Children.Count > 0 ? $" \u00B7 {node.Children.Count} of its own" : string.Empty);

        return new MapPin
        {
            Kind = isMain ? MapPinKind.TreeMain : MapPinKind.Node,
            DotSize = isMain ? TreeMainDot : NodeDot,
            Index = index,
            Level = node.Level,
            Label = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
            FlagCountry = country.Length == 0 ? null : country,
            Record = record,
            Nx = nx,
            Ny = ny,
            TooltipTitle = title,
            TooltipDetail = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
            TooltipMeta = meta,
        };
    }

    /// <summary>A node's own anchor, or the middle of the map when it has none.</summary>
    private static (double Nx, double Ny) NormalizedOf(MapNode node)
    {
        string country = node.Record.Country.Trim();
        (double latitude, double longitude)? anchor = country.Length == 0
            ? null
            : CountryAnchors.For(country);

        return anchor is { } point ? ToNormalized(point) : (0.5, 0.55);
    }

    /// <summary>A country name when it is a real one, else null.</summary>
    private static string? CountryOrNull(string country)
        => country.Trim().Length == 0 ? null : country.Trim();

    // ------------------------------------------------------------------
    // Clicks
    // ------------------------------------------------------------------

    /// <summary>
    /// A dot was clicked: a cluster opens its chooser, everything else opens
    /// its network.
    /// </summary>
    public void HandlePinClick(MapPin pin)
    {
        switch (pin.Kind)
        {
            case MapPinKind.Cluster:
                if (pin.Cluster is { Count: > 1 } cluster)
                {
                    OpenChooser(pin, cluster);
                }
                break;

            case MapPinKind.Main:
            case MapPinKind.TreeMain:
                if (pin.Record is { } record
                    && _nodesById.TryGetValue(record.Id, out MapNode? main))
                {
                    CloseChooser();
                    BuildNetwork(main);
                }
                break;

            default:
                // A connected VPS in an open network: its tooltip is its story.
                break;
        }
    }

    /// <summary>One of the chooser's rows was picked: open that main's network.</summary>
    public void ChooseMain(VpsRecord record)
    {
        CloseChooser();

        if (_nodesById.TryGetValue(record.Id, out MapNode? main))
        {
            BuildNetwork(main);
        }
    }

    /// <summary>
    /// Back from a network to the overview of all mains - and to the whole
    /// earth, because the overview is the whole earth's view.
    /// </summary>
    public void BackToOverview()
    {
        ResetView();
        BuildOverview();
    }

    /// <summary>A tap on the map itself, away from any dot: the chooser steps aside.</summary>
    public void HandleBackgroundTap() => CloseChooser();

    // ------------------------------------------------------------------
    // The chooser
    // ------------------------------------------------------------------

    /// <summary>Opens the chooser over a cluster dot.</summary>
    private void OpenChooser(MapPin pin, IReadOnlyList<VpsRecord> cluster)
    {
        _chooserPin = pin;

        List<ChooserRow> rows = cluster
            .Select(record => new ChooserRow
            {
                Record = record,
                Label = string.IsNullOrWhiteSpace(record.Name) ? "VPS" : record.Name,
                Detail = _nodesById.TryGetValue(record.Id, out MapNode? node) && node.TotalCount > 0
                    ? $"{node.TotalCount} connected VPS{(node.TotalCount == 1 ? string.Empty : "s")}"
                    : "No connected VPSs",
                FlagCountry = pin.FlagCountry,
            })
            .ToList();

        // Seated before it is published, the same promise the pins are laid
        // out under: what the view builds is already where it belongs.
        double estimatedHeight = 96 + rows.Count * 62;

        Chooser = new MapChooser
        {
            Country = pin.FlagCountry ?? string.Empty,
            FlagCountry = pin.FlagCountry,
            Rows = rows,
            X = Math.Clamp(pin.X + 44, 12, Math.Max(12, _hostSize.Width - 320)),
            Y = Math.Clamp(pin.Y - 30, 12, Math.Max(12, _hostSize.Height - estimatedHeight)),
        };
    }

    /// <summary>Closes the chooser, wherever it was.</summary>
    private void CloseChooser()
    {
        _chooserPin = null;
        Chooser = null;
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    /// <summary>
    /// The host's size, from the view. Everything positional is recomputed
    /// from it: the map's rectangle, the dots' pixels, the connections'
    /// segments, the chooser's corner.
    /// </summary>
    public void SetMapSize(double hostWidth, double hostHeight)
    {
        if (hostWidth <= 0 || hostHeight <= 0)
        {
            return;
        }

        _hostSize = (hostWidth, hostHeight);

        // The plate is exactly 2:1; it letterboxes inside whatever it is given.
        double width = Math.Min(hostWidth, hostHeight * 2);
        double height = width / 2;
        _mapRect = ((hostWidth - width) / 2, (hostHeight - height) / 2, width, height);

        if (_fitPending && IsFocused && Pins is { Count: > 0 } current)
        {
            // The network arrived before the view had a size; the fit it
            // asked for happens now that there is one.
            FitToPins(current);
        }
        else
        {
            ClampPan();
        }

        Layout(Pins);
        OnPropertyChanged(nameof(ImageRect));
    }

    // ------------------------------------------------------------------
    // The view: zoom and pan
    // ------------------------------------------------------------------

    /// <summary>
    /// Zooms by a factor about a point: whatever the map holds under that
    /// point stays under it, which is what makes the wheel feel attached to
    /// the pointer.
    /// </summary>
    public void ZoomAt(double factor, double anchorX, double anchorY)
    {
        if (_hostSize.Width <= 0)
        {
            return;
        }

        double next = Math.Clamp(Zoom * factor, 1.0, MaxZoom);

        if (Math.Abs(next - Zoom) < 0.0001)
        {
            return;
        }

        (double rx, double ry, double rw, double rh) = _mapRect;

        // The map point under the anchor, in the plate's own terms.
        double nx = (anchorX - rx - _panX) / (rw * Zoom);
        double ny = (anchorY - ry - _panY) / (rh * Zoom);

        Zoom = next;
        _panX = anchorX - rx - nx * rw * next;
        _panY = anchorY - ry - ny * rh * next;

        ClampPan();
        OnViewMoved();
    }

    /// <summary>Zooms in a step, on the middle of the view.</summary>
    public void ZoomIn() => ZoomAt(ZoomStep, _hostSize.Width / 2, _hostSize.Height / 2);

    /// <summary>Zooms out a step, on the middle of the view.</summary>
    public void ZoomOut() => ZoomAt(1 / ZoomStep, _hostSize.Width / 2, _hostSize.Height / 2);

    /// <summary>
    /// Carries the map by a drag's delta. At rest there is nowhere to carry
    /// it to - the whole plate is already on show.
    /// </summary>
    public void PanBy(double deltaX, double deltaY)
    {
        if (Zoom <= 1.0)
        {
            return;
        }

        _panX += deltaX;
        _panY += deltaY;

        ClampPan();
        OnViewMoved();
    }

    /// <summary>
    /// Frames a set of pins: the zoom that holds their box with room to
    /// breathe, and the pan that centres it.
    /// </summary>
    /// <remarks>
    /// A network scattered across a continent at whole-earth zoom is a row of
    /// specks; the same network framed is a picture of itself. The fit is
    /// capped at five so a single lonely main does not fill the screen with
    /// ocean, and floored at one so a worldwide network simply shows the
    /// earth. Later pans and zooms are the user's - the fit happens once,
    /// when the network opens.
    /// </remarks>
    private void FitToPins(IReadOnlyList<MapPin> pins)
    {
        if (_hostSize.Width <= 0 || pins.Count == 0)
        {
            _fitPending = true;
            return;
        }

        _fitPending = false;

        double minNx = 1, maxNx = 0, minNy = 1, maxNy = 0;

        foreach (MapPin pin in pins)
        {
            minNx = Math.Min(minNx, pin.Nx);
            maxNx = Math.Max(maxNx, pin.Nx);
            minNy = Math.Min(minNy, pin.Ny);
            maxNy = Math.Max(maxNy, pin.Ny);
        }

        // Breathing room, clamped to the plate.
        const double pad = 0.045;
        minNx = Math.Max(0, minNx - pad);
        maxNx = Math.Min(1, maxNx + pad);
        minNy = Math.Max(0, minNy - pad);
        maxNy = Math.Min(1, maxNy + pad);

        (double rx, double ry, double rw, double rh) = _mapRect;
        double boxW = Math.Max(0.02, maxNx - minNx) * rw;
        double boxH = Math.Max(0.02, maxNy - minNy) * rh;

        double zoom = Math.Clamp(
            Math.Min((_hostSize.Width * 0.82) / boxW, (_hostSize.Height * 0.78) / boxH),
            1.0,
            5.0);

        Zoom = zoom;

        // The box's centre lands in the host's centre.
        double cnx = (minNx + maxNx) / 2;
        double cny = (minNy + maxNy) / 2;
        _panX = (_hostSize.Width / 2) - rx - (cnx * rw * zoom);
        _panY = (_hostSize.Height / 2) - ry - (cny * rh * zoom);

        ClampPan();
    }

    /// <summary>
    /// Returns the view to the whole earth. Called when the map opens, so
    /// every visit starts from the whole picture.
    /// </summary>
    public void ResetView()
    {
        _panX = 0;
        _panY = 0;
        Zoom = 1;

        ClampPan();
        OnViewMoved();
    }

    /// <summary>
    /// Keeps the scaled plate over its viewport: centred when it is narrower,
    /// edge to edge when it is wider - the map never drags past its own edge
    /// and leaves a gutter on one side.
    /// </summary>
    private void ClampPan()
    {
        if (_hostSize.Width <= 0)
        {
            return;
        }

        (double rx, double ry, double rw, double rh) = _mapRect;
        double scaledW = rw * Zoom;
        double scaledH = rh * Zoom;

        _panX = scaledW <= _hostSize.Width
            ? (_hostSize.Width - scaledW) / 2 - rx
            : Math.Clamp(_panX, _hostSize.Width - rx - scaledW, -rx);

        _panY = scaledH <= _hostSize.Height
            ? (_hostSize.Height - scaledH) / 2 - ry
            : Math.Clamp(_panY, _hostSize.Height - ry - scaledH, -ry);
    }

    /// <summary>
    /// Notifies everything that watches the view transform: the pixels move,
    /// but the pins are the same objects - the view re-seats what it holds
    /// rather than rebuilding it, so zooming never replays the dots' arrival.
    /// </summary>
    private void OnViewMoved()
    {
        Layout(Pins);
        OnPropertyChanged(nameof(ImageRect));
    }

    /// <summary>Places everything the current view shows.</summary>
    /// <param name="pins">
    /// The pins to place: the new list while a view is being built, before it
    /// is published, or the current one when only the size changed.
    /// </param>
    private void Layout(IReadOnlyList<MapPin>? pins)
    {
        if (_hostSize.Width <= 0 || pins is not { Count: > 0 })
        {
            return;
        }

        (double rx, double ry, double rw, double rh) = _mapRect;

        // The view transform: the plate scaled by the zoom and carried by the
        // pan. At rest - zoom one, pan zero - this is the letterboxed fit.
        double left = rx + _panX;
        double top = ry + _panY;
        double scaledW = rw * Zoom;
        double scaledH = rh * Zoom;

        foreach (MapPin pin in pins)
        {
            pin.X = Clamp(left + pin.Nx * scaledW, left + 20, left + scaledW - 20);
            pin.Y = Clamp(top + pin.Ny * scaledH, top + 22, top + scaledH - 22);
        }

        SpreadSharedAnchors(pins);
        NudgeLabels(pins);
        LayoutLines();
        LayoutChooser();
    }

    /// <summary>
    /// Dots that arrived at the same anchor are spread into a row under it,
    /// the anchor's own dot - the main, when there is one - staying put.
    /// </summary>
    private static void SpreadSharedAnchors(IReadOnlyList<MapPin> pins)
    {
        foreach (IGrouping<(int X, int Y), MapPin> group in pins.GroupBy(
                     pin => (X: (int)Math.Round(pin.Nx * 10000), Y: (int)Math.Round(pin.Ny * 10000))))
        {
            List<MapPin> sharing = group.ToList();

            if (sharing.Count < 2)
            {
                continue;
            }

            // The anchor keeps the shallowest dot: the main of a shared
            // country, the parent of a shared fallback.
            List<MapPin> row = sharing
                .OrderBy(pin => pin.Level)
                .ThenBy(pin => pin.Index)
                .ToList();

            for (int i = 1; i < row.Count; i++)
            {
                double spread = (i - 1 - (row.Count - 2) / 2.0) * SpreadSpacing;
                row[i].X = row[0].X + spread;
                row[i].Y = row[0].Y + SpreadDrop;
            }
        }
    }

    /// <summary>
    /// Keeps labels out of each other: each is placed under its pin's tip,
    /// and pushed down past any it would cover.
    /// </summary>
    /// <remarks>
    /// Widths are estimated rather than measured - the view model has no
    /// text services - which is enough for the nudge's job: labels in a busy
    /// Europe or Caribbean step onto their own lines instead of piling up.
    /// The drop it lands on is handed to the pin, so the control draws the
    /// label where the nudge put it rather than at its rest.
    /// </remarks>
    private static void NudgeLabels(IReadOnlyList<MapPin> pins)
    {
        List<(double Left, double Top, double Right, double Bottom)> placed = new();

        foreach (MapPin pin in pins.OrderBy(pin => pin.Y).ThenBy(pin => pin.X))
        {
            double width = Math.Min(150, 16 + pin.Label.Length * 7.4);
            double left = pin.X - width / 2;

            // A teardrop's label rests under its tip, clear of the whole pin.
            double rest = pin.Y + (pin.DotSize * 1.5) + 10;
            double top = rest;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                bool clear = placed.All(
                    box => left >= box.Right
                          || left + width <= box.Left
                          || top >= box.Bottom
                          || top + 20 <= box.Top);

                if (clear)
                {
                    break;
                }

                top += 21;
            }

            placed.Add((left, top, left + width, top + 20));
            pin.LabelDrop = top - rest;
            pin.TooltipAbove = pin.Y > 150;
        }
    }

    /// <summary>
    /// Draws the connections as pixel segments, each taking the short way
    /// across the map.
    /// </summary>
    private void LayoutLines()
    {
        List<MapLine> direct = new();
        List<MapLine> deep = new();

        foreach ((MapPin parent, MapPin child, int level) in _edges)
        {
            // The direct connections draw solid; everything deeper draws
            // thinner and dashed, so the chain's depth reads at a glance.
            List<MapLine> into = level <= 1 ? direct : deep;
            double width = _mapRect.W * Zoom;

            // The short way across: when the straight span crosses more than
            // half the map, the connection leaves one edge and re-enters the
            // other, as two segments of the one unwrapped line.
            double dx = child.X - parent.X;

            if (dx > width / 2)
            {
                into.Add(new MapLine { X1 = parent.X, Y1 = parent.Y, X2 = child.X - width, Y2 = child.Y });
                into.Add(new MapLine { X1 = parent.X + width, Y1 = parent.Y, X2 = child.X, Y2 = child.Y });
            }
            else if (dx < -width / 2)
            {
                into.Add(new MapLine { X1 = parent.X, Y1 = parent.Y, X2 = child.X + width, Y2 = child.Y });
                into.Add(new MapLine { X1 = parent.X - width, Y1 = parent.Y, X2 = child.X, Y2 = child.Y });
            }
            else
            {
                into.Add(new MapLine { X1 = parent.X, Y1 = parent.Y, X2 = child.X, Y2 = child.Y });
            }
        }

        Lines = direct;
        DeepLines = deep;
    }

    /// <summary>Seats the chooser beside its dot, inside the host.</summary>
    private void LayoutChooser()
    {
        if (Chooser is not { } chooser || _chooserPin is not { } pin)
        {
            return;
        }

        double estimatedHeight = 96 + chooser.Rows.Count * 62;

        chooser.X = Math.Clamp(pin.X + 44, 12, Math.Max(12, _hostSize.Width - 320));
        chooser.Y = Math.Clamp(pin.Y - 30, 12, Math.Max(12, _hostSize.Height - estimatedHeight));
    }

    /// <summary>An anchor in the map's own terms, 0 to 1 each way.</summary>
    private static (double Nx, double Ny) ToNormalized((double Latitude, double Longitude) anchor)
        => ((anchor.Longitude + 180) / 360, (90 - anchor.Latitude) / 180);

    /// <summary>
    /// A clamp that tolerates a map so small its margins cross - the dots
    /// pile into the middle rather than the layout throwing.
    /// </summary>
    private static double Clamp(double value, double low, double high)
        => Math.Clamp(value, Math.Min(low, high), Math.Max(low, high));
}
