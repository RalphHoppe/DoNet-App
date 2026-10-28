using System;
using System.Collections.Generic;
using System.ComponentModel;
using DoNet.Controls;
using DoNet.Models;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DoNet.Views;

/// <summary>
/// The VPS map screen. See the XAML for how the world, the scroller and the
/// panel fit together; this code-behind owns the parts a XAML file cannot.
/// </summary>
/// <remarks>
/// <para>
/// The view is a scroller over a fixed world. Every position on the map is
/// a world position, placed once by the view model; the scroller's zoom and
/// offsets are the only things that ever change, and they change only
/// through <see cref="ScrollViewer.ChangeView"/>. The screen-to-world
/// conversions - the letterbox included - are the exact formulas of
/// tools/simulate_map_screen.py, which asserts their invariants before any
/// of this ships.
/// </para>
/// <para>
/// The markers counter-scale with the zoom so they stay the same size on
/// screen at every depth; the arcs' stroke does the same. The chooser
/// closes the moment the view moves, so it can never float loose of the
/// marker it belongs to.
/// </para>
/// <para>
/// The panel's rows are templated, not built: the repeater prepares them,
/// and this code only forwards their taps and hovers to the view model and
/// the markers. Nothing here re-positions anything that was already
/// placed.
/// </para>
///</remarks>
public sealed partial class NetworkMapView : UserControl
{
    /// <summary>The markers on the world, paired with their models.</summary>
    private readonly List<(MapMarker Marker, MapMarkerItem Model)> _markers = new();

    /// <summary>The arcs drawn on the world.</summary>
    private readonly List<Path> _arcs = new();

    /// <summary>Where a drag began, in viewport pixels, or null when there is no drag.</summary>
    private Point? _dragStart;

    /// <summary>Whether the pointer has moved far enough to count as a drag.</summary>
    private bool _dragging;

    /// <summary>Whether the scroller has been given its base zoom yet.</summary>
    private bool _based;

    /// <summary>
    /// The zoom the fit was asking for, when a network arrived before the
    /// scroller had a size to be fitted into.
    /// </summary>
    private bool _fitPending;

    public NetworkMapView()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind
        // binds its root object while the generated code runs.
        ViewModel = App.Current.Services.GetRequiredService<NetworkMapViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The repeater prepares the panel's rows; the forwarding of their
        // taps and hovers is the only thing they need from this view.
        RowsRepeater.ElementPrepared += OnRowPrepared;
        RowsRepeater.ElementClearing += OnRowClearing;

        // A deferred control can be created *because* it is already supposed
        // to be showing; whatever it missed, it catches up on now.
        Loaded += (_, _) => CatchUp();

        // HomePage is rebuilt on every unlock, and the view model is a
        // singleton, so without this each lock/unlock cycle leaves another
        // detached screen listening to it.
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>The map's state and its data.</summary>
    public NetworkMapViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page
    /// is leaving for good.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();

    // ------------------------------------------------------------------
    // The view model's word
    // ------------------------------------------------------------------

    /// <summary>
    /// Brings the screen up to date with a view model that may have moved
    /// while the control did not exist: everything is rebuilt from what is,
    /// never from what changed.
    /// </summary>
    private void CatchUp()
    {
        RebuildMarkers();
        RebuildArcs();
        RowsRepeater.ItemsSource = ViewModel.Rows;
        DetailsRepeater.ItemsSource = ViewModel.Details;
        UpdateListTitle();
        RebuildChooser();
        ApplyBaseZoom();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(NetworkMapViewModel.Markers):
                RebuildMarkers();
                break;

            case nameof(NetworkMapViewModel.Arcs):
                RebuildArcs();
                break;

            case nameof(NetworkMapViewModel.Rows):
                RowsRepeater.ItemsSource = ViewModel.Rows;
                UpdateListTitle();
                break;

            case nameof(NetworkMapViewModel.Details):
                DetailsRepeater.ItemsSource = ViewModel.Details;
                break;

            case nameof(NetworkMapViewModel.IsFocused):
                UpdateListTitle();

                // The network frames itself: whatever the view was showing,
                // the new network's markers decide where it looks.
                if (ViewModel.IsFocused)
                {
                    if (MapScroll.ViewportWidth > 0)
                    {
                        FitToMarkers();
                    }
                    else
                    {
                        _fitPending = true;
                    }
                }
                break;

            case nameof(NetworkMapViewModel.Chooser):
                RebuildChooser();
                break;
        }
    }

    // ------------------------------------------------------------------
    // The world's contents
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the world's markers from the view model's scene. Each is
    /// placed once, centred on its country's anchor, and never moves again.
    /// </summary>
    private void RebuildMarkers()
    {
        foreach ((MapMarker marker, _) in _markers)
        {
            marker.MarkerTapped -= OnMarkerTapped;
            MarkerLayer.Children.Remove(marker);
        }

        _markers.Clear();

        if (ViewModel.Markers is not { } models)
        {
            return;
        }

        double zoom = MapScroll.ZoomFactor > 0 ? MapScroll.ZoomFactor : 1;

        foreach (MapMarkerItem model in models)
        {
            MapMarker marker = new() { Model = model };
            marker.MarkerTapped += OnMarkerTapped;
            marker.SetZoom(zoom);
            _markers.Add((marker, model));
            MarkerLayer.Children.Add(marker);

            // Centred on the anchor: the tile's middle lands on the top of
            // the country's shape, and stays there.
            Canvas.SetLeft(marker, model.WorldX - (marker.Width / 2));
            Canvas.SetTop(marker, model.WorldY - (marker.Height / 2));
        }
    }

    private void OnMarkerTapped(object? sender, MapMarkerItem model)
        => ViewModel.ActivateMarker(model);

    /// <summary>
    /// Builds the world's arcs: gentle curves between each record and its
    /// parent, drawn once in world coordinates.
    /// </summary>
    private void RebuildArcs()
    {
        foreach (Path arc in _arcs)
        {
            ArcLayer.Children.Remove(arc);
        }

        _arcs.Clear();

        if (ViewModel.Arcs is not { } arcs)
        {
            return;
        }

        double zoom = MapScroll.ZoomFactor > 0 ? MapScroll.ZoomFactor : 1;

        foreach (MapArcItem arc in arcs)
        {
            Path shape = new()
            {
                Stroke = (Brush)Resources[arc.Deep ? "DeepConnectionBrush" : "ConnectionBrush"],
                StrokeThickness = 1.75 / zoom,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = ArcGeometry(arc.X1, arc.Y1, arc.X2, arc.Y2),
                IsHitTestVisible = false,
            };

            _arcs.Add(shape);
            ArcLayer.Children.Add(shape);
        }
    }

    /// <summary>
    /// An arc between two world points: the straight line's midpoint pushed
    /// along its perpendicular, bowing upward, by a fraction of the
    /// distance - the route-map curve, capped so a long haul does not loop
    /// the world.
    /// </summary>
    private static PathGeometry ArcGeometry(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt((dx * dx) + (dy * dy));

        // The unit perpendicular; when the points coincide any will do.
        double nx = dy;
        double ny = -dx;
        double norm = Math.Sqrt((nx * nx) + (ny * ny));

        if (norm < 0.001)
        {
            nx = 0;
            ny = -1;
        }
        else
        {
            nx /= norm;
            ny /= norm;
        }

        // Always bow upward: the perpendicular that points up wins.
        if (ny > 0)
        {
            nx = -nx;
            ny = -ny;
        }

        double bow = Math.Min(0.16 * length, 130);

        PathFigure figure = new() { StartPoint = new Point(x1, y1) };

        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = new Point((x1 + x2) / 2 + (nx * bow), (y1 + y2) / 2 + (ny * bow)),
            Point2 = new Point(x2, y2),
        });

        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>The list's title, whichever list is on show.</summary>
    private void UpdateListTitle()
        => ListTitle.Text = ViewModel.IsFocused ? "THE NETWORK" : "MAINS";

    // ------------------------------------------------------------------
    // The panel's rows
    // ------------------------------------------------------------------

    /// <summary>
    /// Forwards a prepared row's taps and hovers: the tap to the view model,
    /// the hover to the row's marker, so the list and the world speak with
    /// one voice.
    /// </summary>
    private void OnRowPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not MapListRow row)
        {
            return;
        }

        row.RowTapped += OnRowTapped;
        row.RowPointerEntered += OnRowPointerEntered;
        row.RowPointerExited += OnRowPointerExited;
    }

    /// <summary>
    /// A row's handlers come off with it; a recycled row is prepared again
    /// and re-subscribes, so nothing is ever forwarded twice.
    /// </summary>
    private void OnRowClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not MapListRow row)
        {
            return;
        }

        row.RowTapped -= OnRowTapped;
        row.RowPointerEntered -= OnRowPointerEntered;
        row.RowPointerExited -= OnRowPointerExited;
    }

    private void OnRowTapped(object? sender, MapRowItem row) => ViewModel.ActivateRow(row);

    private void OnRowPointerEntered(object? sender, MapRowItem row) => SetMarkerHover(row, on: true);

    private void OnRowPointerExited(object? sender, MapRowItem row) => SetMarkerHover(row, on: false);

    /// <summary>Finds the row's marker and lifts or settles it.</summary>
    private void SetMarkerHover(MapRowItem row, bool on)
    {
        foreach ((MapMarker marker, MapMarkerItem model) in _markers)
        {
            if (model.Record is { } record && row.Record is { } rowRecord
                && record.Id == rowRecord.Id)
            {
                marker.SetHover(on);
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // The view: base zoom, zoom and pan
    // ------------------------------------------------------------------

    /// <summary>The zoom at which the whole world fits the viewport exactly.</summary>
    private double BaseZoom
        => Math.Min(
            MapScroll.ViewportWidth / MapWorld.Width,
            MapScroll.ViewportHeight / MapWorld.Height);

    /// <summary>The deepest zoom the view allows: five times closer than the fit.</summary>
    private double MaxZoom => BaseZoom * MapWorld.ZoomHeadroom;

    /// <summary>
    /// The dead space between the viewport's edge and the world, when the
    /// world is smaller than the viewport in a dimension: the content is
    /// centred, so the gap is half on each side, and the scroll offset is
    /// zero for that dimension.
    /// </summary>
    private double LetterX(double zoom)
        => Math.Max(0, (MapScroll.ViewportWidth - (MapWorld.Width * zoom)) / 2);

    /// <summary>The letterbox's vertical share.</summary>
    private double LetterY(double zoom)
        => Math.Max(0, (MapScroll.ViewportHeight - (MapWorld.Height * zoom)) / 2);

    /// <summary>How far the view may scroll horizontally at this zoom.</summary>
    private double ScrollX(double zoom)
        => Math.Max(0, (MapWorld.Width * zoom) - MapScroll.ViewportWidth);

    /// <summary>How far the view may scroll vertically at this zoom.</summary>
    private double ScrollY(double zoom)
        => Math.Max(0, (MapWorld.Height * zoom) - MapScroll.ViewportHeight);

    /// <summary>
    /// Gives the scroller its zoom range and its resting view, whenever the
    /// viewport changes size. The world never changes to fit the window;
    /// only the fit's zoom does.
    /// </summary>
    private void OnMapHostSizeChanged(object sender, SizeChangedEventArgs args)
        => ApplyBaseZoom();

    /// <summary>
    /// Sets the scroller's zoom range from the viewport's size and rests the
    /// view on the whole world - the first time, and any time the window
    /// grew enough to widen the fit.
    /// </summary>
    private void ApplyBaseZoom()
    {
        if (MapScroll.ViewportWidth <= 0 || MapScroll.ViewportHeight <= 0)
        {
            return;
        }

        double min = BaseZoom;
        MapScroll.MinZoomFactor = (float)min;
        MapScroll.MaxZoomFactor = (float)(min * MapWorld.ZoomHeadroom);

        if (!_based || MapScroll.ZoomFactor < min)
        {
            _based = true;
            RestAtBase();
        }

        if (_fitPending && ViewModel.IsFocused)
        {
            _fitPending = false;
            FitToMarkers();
        }
    }

    /// <summary>
    /// Rests the view on the whole world: the fit's zoom, the world centred.
    /// </summary>
    private void RestAtBase()
    {
        double zoom = BaseZoom;
        double x = ScrollX(zoom) / 2;
        double y = ScrollY(zoom) / 2;
        _ = MapScroll.ChangeView(x, y, (float)zoom, disableAnimation: true);
    }

    /// <summary>
    /// The wheel zooms about the pointer: whatever the world holds under the
    /// cursor stays under it, a notch at a time - the simulator's own
    /// formula, letterbox and clamping included.
    /// </summary>
    private void OnMapWheel(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(MapHost);
        double delta = point.Properties.MouseWheelDelta;

        if (delta == 0)
        {
            return;
        }

        args.Handled = true;
        ZoomAbout(point.Position.X, point.Position.Y, Math.Pow(1.15, delta / 120.0));
    }

    /// <summary>
    /// Zooms by a factor about a viewport point, keeping the world point
    /// under it.
    /// </summary>
    private void ZoomAbout(double px, double py, double factor)
    {
        if (MapScroll.ViewportWidth <= 0)
        {
            return;
        }

        double zoom = MapScroll.ZoomFactor;
        double next = Math.Clamp(zoom * factor, BaseZoom, MaxZoom);

        if (Math.Abs(next - zoom) < 0.0001)
        {
            return;
        }

        // The world point under the anchor, recovered through the letterbox.
        double wx = ((px + MapScroll.HorizontalOffset) - LetterX(zoom)) / zoom;
        double wy = ((py + MapScroll.VerticalOffset) - LetterY(zoom)) / zoom;

        // Re-anchored under the pointer at the new zoom, within the world's
        // true edges.
        double dx = Math.Clamp((wx * next) + LetterX(next) - px, 0, ScrollX(next));
        double dy = Math.Clamp((wy * next) + LetterY(next) - py, 0, ScrollY(next));

        _ = MapScroll.ChangeView(dx, dy, (float)next, disableAnimation: true);
    }

    /// <summary>
    /// A press on the map itself - the sea or the land, not a marker or a
    /// panel - may become a drag of the view.
    /// </summary>
    private void OnMapPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(MapHost).Properties.IsLeftButtonPressed
            || !PressStartsOnMap(args.OriginalSource as DependencyObject))
        {
            return;
        }

        _dragStart = args.GetCurrentPoint(MapHost).Position;
        _dragging = false;
        MapHost.CapturePointer(args.Pointer);
    }

    /// <summary>
    /// The drag itself: six pixels of slack before it counts, so a press
    /// released where it began still reads as a tap.
    /// </summary>
    private void OnMapPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        Point position = args.GetCurrentPoint(MapHost).Position;

        if (!_dragging
            && Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) > 6)
        {
            _dragging = true;
        }

        if (!_dragging)
        {
            return;
        }

        _ = MapScroll.ChangeView(
            MapScroll.HorizontalOffset - (position.X - start.X),
            MapScroll.VerticalOffset - (position.Y - start.Y),
            zoomFactor: null,
            disableAnimation: true);
        _dragStart = position;
    }

    /// <summary>
    /// The end of a press. One that never became a drag still reads as a tap
    /// on the map - the chooser steps aside.
    /// </summary>
    private void OnMapPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is null)
        {
            return;
        }

        bool wasDrag = _dragging;
        _dragStart = null;
        _dragging = false;
        MapHost.ReleasePointerCapture(args.Pointer);

        if (!wasDrag)
        {
            ViewModel.HandleBackgroundTap();
        }
    }

    /// <summary>
    /// Whether a press began on the map itself: the climb from where it began
    /// reaches the world or its layers before any of the panels over them.
    /// </summary>
    private bool PressStartsOnMap(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is MapMarker || node == ChooserLayer)
            {
                return false;
            }

            if (node == World || node == MapScroll)
            {
                return true;
            }

            if (node == MapHost)
            {
                return false;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>
    /// The view moved: the markers counter-scale to stay their size on
    /// screen, the arcs' strokes do the same, and the chooser - which
    /// belongs to a place on the screen, not the world - steps aside.
    /// </summary>
    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        double zoom = MapScroll.ZoomFactor;

        foreach ((MapMarker marker, _) in _markers)
        {
            marker.SetZoom(zoom);
        }

        foreach (Path arc in _arcs)
        {
            arc.StrokeThickness = 1.75 / zoom;
        }

        if (ViewModel.Chooser is not null)
        {
            ViewModel.CloseChooser();
        }
    }

    private void OnZoomInClick(object sender, RoutedEventArgs args)
        => ZoomAbout(MapScroll.ViewportWidth / 2, MapScroll.ViewportHeight / 2, 1.35);

    private void OnZoomOutClick(object sender, RoutedEventArgs args)
        => ZoomAbout(MapScroll.ViewportWidth / 2, MapScroll.ViewportHeight / 2, 1 / 1.35);

    private void OnZoomResetClick(object sender, RoutedEventArgs args) => RestAtBase();

    // ------------------------------------------------------------------
    // The fit
    // ------------------------------------------------------------------

    /// <summary>
    /// Frames the open network: the zoom that fits its markers, the view
    /// centred on them - the simulator's own formula, animated by the
    /// platform's own scroller.
    /// </summary>
    private void FitToMarkers()
    {
        if (ViewModel.Markers is not { Count: > 0 } models)
        {
            return;
        }

        double vw = MapScroll.ViewportWidth;
        double vh = MapScroll.ViewportHeight;

        double left = double.MaxValue;
        double right = double.MinValue;
        double top = double.MaxValue;
        double bottom = double.MinValue;

        foreach (MapMarkerItem model in models)
        {
            left = Math.Min(left, model.WorldX - 24);
            right = Math.Max(right, model.WorldX + 24);
            top = Math.Min(top, model.WorldY - 20);
            bottom = Math.Max(bottom, model.WorldY + 20);
        }

        double zoom = Math.Min(
            vw / Math.Max(right - left, 1),
            vh / Math.Max(bottom - top, 1)) * 0.92;
        zoom = Math.Clamp(zoom, BaseZoom, MaxZoom);

        double cx = (((left + right) / 2) * zoom) + LetterX(zoom) - (vw / 2);
        double cy = (((top + bottom) / 2) * zoom) + LetterY(zoom) - (vh / 2);
        double dx = Math.Clamp(cx, 0, ScrollX(zoom));
        double dy = Math.Clamp(cy, 0, ScrollY(zoom));

        _ = MapScroll.ChangeView(dx, dy, (float)zoom, disableAnimation: false);
    }

    // ------------------------------------------------------------------
    // The chooser
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills the chooser from the view model's state and seats it beside its
    /// marker - the simulator's own formula, flipped at the east edge.
    /// </summary>
    private void RebuildChooser()
    {
        if (ViewModel.Chooser is not { } chooser)
        {
            ChooserCard.Visibility = Visibility.Collapsed;
            return;
        }

        // A chooser without its marker has nowhere to sit.
        if (chooser.Anchor is not { } anchor)
        {
            ChooserCard.Visibility = Visibility.Collapsed;
            return;
        }

        ChooserFlag.Country = chooser.FlagCountry;
        ChooserTitle.Text =
            $"{chooser.Rows.Count} MAIN VPS{(chooser.Rows.Count == 1 ? string.Empty : "S")} "
            + $"IN {chooser.Country.ToUpperInvariant()}";
        ChooserRepeater.ItemsSource = chooser.Rows;

        // Measured before it is shown, so it can be kept on screen.
        ChooserCard.Measure(new Size(300, double.PositiveInfinity));
        double panelW = 300;
        double panelH = ChooserCard.DesiredSize.Height;

        double zoom = MapScroll.ZoomFactor;
        double anchorX = (anchor.WorldX * zoom) + LetterX(zoom) - MapScroll.HorizontalOffset;
        double anchorY = (anchor.WorldY * zoom) + LetterY(zoom) - MapScroll.VerticalOffset;

        double vw = MapScroll.ViewportWidth;
        double vh = MapScroll.ViewportHeight;
        bool fitsRight = anchorX + 14 + panelW <= vw - 16;
        double left = fitsRight ? anchorX + 14 : anchorX - 14 - panelW;
        double top = anchorY - 30;

        left = Math.Clamp(left, 12, Math.Max(12, vw - panelW - 12));
        top = Math.Clamp(top, 12, Math.Max(12, vh - panelH - 12));

        ChooserCard.Visibility = Visibility.Visible;
        Canvas.SetLeft(ChooserCard, left);
        Canvas.SetTop(ChooserCard, top);
    }

    private void OnChooserCloseClick(object sender, RoutedEventArgs args)
        => ViewModel.CloseChooser();

    /// <summary>One of the chooser's rows was picked: that main's network opens.</summary>
    private void OnChooserRowClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: VpsRecord record })
        {
            ViewModel.ChooseMain(record);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs args) => ViewModel.BackToOverview();
}
