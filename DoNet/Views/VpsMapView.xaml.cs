using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DoNet.Controls;
using DoNet.Models;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DoNet.Views;

/// <summary>
/// The VPS map screen. See the XAML for how the page, the panel and the two
/// views fit together; this code-behind owns the parts a XAML file cannot.
/// </summary>
/// <remarks>
/// <para>
/// The markers and the connections are built in code rather than templated:
/// they are positioned in pixels by the view model's layout pass, and an
/// element the code positions is an element the code may as well create.
/// Rebuilding happens on the view model's say-so - a new set of pins, a new
/// set of lines - and repositioning on the screen's, so a resize never
/// replays the markers' arrival.
/// </para>
/// <para>
/// The panel's rows are built here too: the list as one row per record -
/// every main in the overview, the chain stepped in by depth in a network -
/// and the selected VPS's details as the preview's read-only cells. Hovering
/// a row stands its marker up on the page, so the list and the earth speak
/// with one voice.
/// </para>
/// <para>
/// The screen's own arrival and departure belong to the shell: HomePage
/// slides the layer this view lives in, the way it moves its sections, and
/// the rail's Services button becomes the way back while the map is up.
/// </para>
///</remarks>
public sealed partial class VpsMapView : UserControl
{
    /// <summary>The markers on the plate, in view model order.</summary>
    private readonly List<MapDot> _dots = new();

    /// <summary>The connections, paired with the segments that draw them.</summary>
    private readonly List<(Path Shape, MapLine Model)> _lines = new();

    /// <summary>
    /// The connections' flow: one storyboard drifting every arc's dashes along
    /// it, so an open network reads as traffic rather than wiring.
    /// </summary>
    private Storyboard? _flow;

    /// <summary>Where a drag began, in host pixels, or null when there is no drag.</summary>
    private Point? _dragStart;

    /// <summary>Whether the pointer has moved far enough to count as a drag.</summary>
    private bool _dragging;

    public VpsMapView()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind
        // binds its root object while the generated code runs.
        ViewModel = App.Current.Services.GetRequiredService<VpsMapViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // A deferred control can be created *because* it is already supposed
        // to be showing; whatever it missed, it catches up on now.
        Loaded += (_, _) => CatchUp();

        // HomePage is rebuilt on every unlock, and the view model is a
        // singleton, so without this each lock/unlock cycle leaves another
        // detached screen listening to it. The flow stops with it - an
        // animation nobody can see is still an animation.
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _flow?.Stop();
            _flow = null;
        };
    }

    /// <summary>The map's state and its data.</summary>
    public VpsMapViewModel ViewModel { get; }

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
        RebuildDots();
        RebuildLines();
        RebuildChooser();
        RebuildList();
        RebuildDetails();
        ApplyImageRect();
        PositionOverlay();
        UpdateSelectedDots();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(VpsMapViewModel.Pins):
                RebuildDots();
                UpdateSelectedDots();
                break;

            case nameof(VpsMapViewModel.Rows):
                RebuildList();
                break;

            case nameof(VpsMapViewModel.IsFocused):
                RebuildList();
                RebuildDetails();
                UpdateSelectedDots();
                break;

            case nameof(VpsMapViewModel.SelectedRecord):
                RebuildDetails();
                UpdateSelectedDots();
                break;

            case nameof(VpsMapViewModel.Lines):
            case nameof(VpsMapViewModel.DeepLines):
                RebuildLines();
                break;

            case nameof(VpsMapViewModel.Chooser):
                RebuildChooser();
                break;

            case nameof(VpsMapViewModel.ImageRect):
                // The transform moved: the image is seated into its new
                // rectangle and the markers - the same objects, in new places
                // - are re-seated onto it. Rebuilding never happens here, so
                // the markers' arrival never replays under the wheel.
                ApplyImageRect();
                PositionOverlay();
                break;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs args) => ViewModel.BackToOverview();

    private void OnChooserCloseClick(object sender, RoutedEventArgs args)
        => ViewModel.HandleBackgroundTap();

    // ------------------------------------------------------------------
    // Sizing
    // ------------------------------------------------------------------

    private void OnMapHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // The clip keeps the plate's overflows - the connections that cross
        // the edge where the map ends and restarts, the view panned past an
        // edge - inside the map's own bounds.
        MapClip.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
        };

        ViewModel.SetMapSize(args.NewSize.Width, args.NewSize.Height);
        ApplyImageRect();
        PositionOverlay();
    }

    /// <summary>
    /// Seats the plate's image in its transformed rectangle: the view model
    /// owns where everything on the map is, the image included.
    /// </summary>
    private void ApplyImageRect()
    {
        (double x, double y, double width, double height) = ViewModel.ImageRect;

        Canvas.SetLeft(MapImage, x);
        Canvas.SetTop(MapImage, y);
        MapImage.Width = width;
        MapImage.Height = height;
    }

    // ------------------------------------------------------------------
    // Zoom and pan
    // ------------------------------------------------------------------

    /// <summary>
    /// The wheel zooms about the pointer: whatever the map holds under the
    /// cursor stays under it, a notch at a time.
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

        ViewModel.ZoomAt(
            Math.Pow(1.15, delta / 120.0),
            point.Position.X,
            point.Position.Y);
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

        ViewModel.PanBy(position.X - start.X, position.Y - start.Y);
        _dragStart = position;
    }

    /// <summary>
    /// The end of a press. One that never became a drag still reads as a tap
    /// on the map - the capture it took can swallow the ordinary tapped
    /// event, so the tap is delivered here.
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
    /// reaches the plate's canvas or the marker layer's background before any
    /// of the panels that sit over them.
    /// </summary>
    private bool PressStartsOnMap(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is MapDot || node == ChooserLayer)
            {
                return false;
            }

            if (node == DotLayer || node == MapCanvas)
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

    private void OnZoomInClick(object sender, RoutedEventArgs args) => ViewModel.ZoomIn();

    private void OnZoomOutClick(object sender, RoutedEventArgs args) => ViewModel.ZoomOut();

    private void OnZoomResetClick(object sender, RoutedEventArgs args) => ViewModel.ResetView();

    // ------------------------------------------------------------------
    // The overlay
    // ------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the markers from the view model's pins, then seats them. The
    /// controls are created fresh so their arrival plays each time the view
    /// changes; a resize never comes through here.
    /// </summary>
    private void RebuildDots()
    {
        foreach (MapDot dot in _dots)
        {
            dot.PinTapped -= OnPinTapped;
            DotLayer.Children.Remove(dot);
        }

        _dots.Clear();

        if (ViewModel.Pins is not { } pins)
        {
            return;
        }

        foreach (MapPin pin in pins)
        {
            MapDot dot = new() { Pin = pin };
            dot.PinTapped += OnPinTapped;
            _dots.Add(dot);
            DotLayer.Children.Add(dot);
        }

        PositionOverlay();
    }

    private void OnPinTapped(object? sender, MapPin pin) => ViewModel.HandlePinClick(pin);

    /// <summary>
    /// Rebuilds the connections: the dashed deep ones first, the solid direct
    /// ones over them.
    /// </summary>
    private void RebuildLines()
    {
        foreach ((Path shape, _) in _lines)
        {
            MapCanvas.Children.Remove(shape);
        }

        _lines.Clear();

        AddConnections(ViewModel.DeepLines, deep: true);
        AddConnections(ViewModel.Lines, deep: false);

        PositionOverlay();
        StartFlow();
    }

    /// <summary>
    /// One connection per segment: a curved arc, dashed, its dashes left to
    /// the flow storyboard to carry.
    /// </summary>
    private void AddConnections(IReadOnlyList<MapLine>? segments, bool deep)
    {
        if (segments is null)
        {
            return;
        }

        foreach (MapLine segment in segments)
        {
            Path shape = new()
            {
                Stroke = (Brush)Resources[deep ? "DeepConnectionBrush" : "ConnectionBrush"],
                StrokeThickness = deep ? 1.5 : 2.25,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = deep ? new DoubleCollection { 3, 4 } : new DoubleCollection { 2, 3 },
                UseLayoutRounding = true,
            };

            _lines.Add((shape, segment));
            MapCanvas.Children.Add(shape);
        }
    }

    /// <summary>
    /// An arc between two points: the straight line's midpoint pushed along
    /// its perpendicular, bowing upward, by a fraction of the distance - the
    /// route-map curve, capped so a long haul does not loop the world.
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

        double bow = Math.Min(0.22 * length, 110);

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

    /// <summary>
    /// Starts the dashes drifting along every connection, one dash period per
    /// second - a loop, so the flow never pops. The previous flow stops
    /// first; a rebuild owns the screen.
    /// </summary>
    private void StartFlow()
    {
        _flow?.Stop();
        _flow = null;

        if (_lines.Count == 0)
        {
            return;
        }

        Storyboard flow = new();

        foreach ((Path shape, _) in _lines)
        {
            DoubleCollection dashes = shape.StrokeDashArray ?? new DoubleCollection { 2, 3 };
            double period = dashes.Count > 1 ? dashes[0] + dashes[1] : dashes[0];

            DoubleAnimation drift = new()
            {
                From = 0,
                To = -period,
                Duration = new Duration(TimeSpan.FromSeconds(1)),

                // Forever is a mode, not a number: the count-taking
                // constructor rejects infinity with an out-of-range
                // exception, so the forever behaviour is set by type.
                RepeatBehavior = new RepeatBehavior { Type = RepeatBehaviorType.Forever },
            };
            Storyboard.SetTarget(drift, shape);
            Storyboard.SetTargetProperty(drift, "StrokeDashOffset");
            flow.Children.Add(drift);
        }

        flow.Begin();
        _flow = flow;
    }

    /// <summary>
    /// Seats everything on the plate at its laid-out position: the markers by
    /// their tips - the point that lands on the country - the connections end
    /// to end, the chooser beside its marker.
    /// </summary>
    private void PositionOverlay()
    {
        foreach (MapDot dot in _dots)
        {
            if (dot.Pin is not { } pin)
            {
                continue;
            }

            Canvas.SetLeft(dot, pin.X - dot.TipOffsetX);
            Canvas.SetTop(dot, pin.Y - dot.TipOffsetY);
        }

        foreach ((Path shape, MapLine model) in _lines)
        {
            shape.Data = ArcGeometry(model.X1, model.Y1, model.X2, model.Y2);
        }

        ChooserCard.Visibility = ViewModel.Chooser is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ViewModel.Chooser is { } chooser)
        {
            Canvas.SetLeft(ChooserCard, chooser.X);
            Canvas.SetTop(ChooserCard, chooser.Y);
        }
    }

    /// <summary>
    /// A tap on the map itself, away from any marker: the chooser steps
    /// aside.
    /// </summary>
    /// <remarks>
    /// The tap that opens a chooser bubbles up from the marker to this layer
    /// as well, so the climb up the tree skips the taps that belong to a
    /// marker.
    /// </remarks>
    private void OnMapBackgroundTapped(object sender, TappedRoutedEventArgs args)
    {
        DependencyObject? node = args.OriginalSource as DependencyObject;

        while (node is not null)
        {
            if (node is MapDot)
            {
                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        ViewModel.HandleBackgroundTap();
    }

    // ------------------------------------------------------------------
    // The panel's list
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills the panel's list: every main in the overview, the whole chain -
    /// stepped in by depth - in an open network.
    /// </summary>
    private void RebuildList()
    {
        ListRows.Children.Clear();
        ListTitle.Text = ViewModel.IsFocused ? "THE NETWORK" : "MAINS";

        if (ViewModel.Rows is not { } rows)
        {
            return;
        }

        foreach (MapListRow row in rows)
        {
            ListRows.Children.Add(BuildListRow(row));
        }

        if (rows.Count == 0 && !ViewModel.IsFocused)
        {
            ListRows.Children.Add(new TextBlock
            {
                Text = "No mains yet - add a VPS and it lands here",
                FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
                FontSize = 12.5,
                Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// <summary>
    /// One row of the list: the record's marker in words - flag, name,
    /// country - stepped in by its depth, with the record's place in the
    /// network on the right. Hovering the row stands its marker up on the
    /// page; clicking the row is clicking the marker.
    /// </summary>
    private Button BuildListRow(MapListRow row)
    {
        // The marker this row speaks for, so the hover can find it.
        MapDot? dot = _dots.FirstOrDefault(d =>
            d.Pin is { Record: { } record } && record.Id == row.Record.Id);

        Button button = new()
        {
            Style = (Style)Application.Current.Resources["BareButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Brush clear = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

        Border shell = new()
        {
            MinHeight = 50,
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(13 + (row.Level * 13), 5, 11, 5),
            Background = clear,
            BorderBrush = clear,
            BorderThickness = new Thickness(1),
        };

        Grid layout = new() { ColumnSpacing = 10 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The flag, or the globe when the record has no country.
        Viewbox flagBox = new() { Width = 20, Height = 15, VerticalAlignment = VerticalAlignment.Center };

        if (row.FlagCountry is not null)
        {
            flagBox.Child = new FlagIcon { Country = row.FlagCountry };
        }
        else
        {
            flagBox.Child = new LineIcon
            {
                Kind = "Globe",
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xA0, 0xA5, 0xAB)),
            };
        }

        Grid.SetColumn(flagBox, 0);
        layout.Children.Add(flagBox);

        StackPanel texts = new() { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontFamily = (FontFamily)Application.Current.Resources["BalooSemiBoldFont"],
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 190,
        });
        texts.Children.Add(new TextBlock
        {
            Text = row.Country,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 210,
        });

        Grid.SetColumn(texts, 1);
        layout.Children.Add(texts);

        // The record's place in the network, on the right.
        Border chip = new()
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 1),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x4B, 0xAA, 0x98)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Child = new TextBlock
        {
            Text = row.Meta,
            FontFamily = (FontFamily)Application.Current.Resources["BalooSemiBoldFont"],
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 14,
        };
        Grid.SetColumn(chip, 2);
        layout.Children.Add(chip);

        shell.Child = layout;
        button.Content = shell;

        // The hover pairs: the row warms, the marker itself stands up.
        Brush warm = new SolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0xF6, 0xF9));
        Brush warmBorder = new SolidColorBrush(Color.FromArgb(0xFF, 0xE4, 0xE6, 0xE9));

        button.PointerEntered += (_, _) =>
        {
            shell.Background = warm;
            shell.BorderBrush = warmBorder;
            dot?.HighlightOn();
        };
        button.PointerExited += (_, _) =>
        {
            shell.Background = clear;
            shell.BorderBrush = clear;
            dot?.HighlightOff();
        };

        // A click on the row is a click on the marker: the overview's mains
        // open their network, the network's records trade the selection.
        button.Click += (_, _) =>
        {
            if (ViewModel.IsFocused)
            {
                ViewModel.SelectedRecord = row.Record;
            }
            else
            {
                ViewModel.ChooseMain(row.Record);
            }
        };

        return button;
    }

    /// <summary>
    /// Marks the marker whose record the panel is telling the story of, so
    /// the page and the panel agree on who is being read.
    /// </summary>
    private void UpdateSelectedDots()
    {
        VpsRecord? selected = ViewModel.IsFocused ? ViewModel.SelectedRecord : null;

        foreach (MapDot dot in _dots)
        {
            dot.SetSelected(dot.Pin is { Record: { } record }
                && selected is { } chosen
                && record.Id == chosen.Id);
        }
    }

    // ------------------------------------------------------------------
    // The panel's details
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills the panel's detail section with the selected VPS's card and its
    /// important details: the same fields the preview shows, as its read-only
    /// cells, with the ones never filled left out rather than shown blank.
    /// </summary>
    private void RebuildDetails()
    {
        SelectionCard.Children.Clear();

        if (!ViewModel.IsFocused || ViewModel.SelectedRecord is not { } record)
        {
            return;
        }

        // The selection's own card, compact: flag, name, place, place in the
        // network.
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 9 };

        if (ViewModel.SelectedFlagCountry is not null)
        {
            Viewbox flagBox = new() { Width = 22, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            flagBox.Child = new FlagIcon { Country = ViewModel.SelectedFlagCountry };
            head.Children.Add(flagBox);
        }

        head.Children.Add(new TextBlock
        {
            Text = ViewModel.SelectedName,
            FontFamily = (FontFamily)Application.Current.Resources["BalooSemiBoldFont"],
            FontSize = 15,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 240,
            VerticalAlignment = VerticalAlignment.Center,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 20,
        });
        SelectionCard.Children.Add(head);

        SelectionCard.Children.Add(new TextBlock
        {
            Text = ViewModel.SelectedCountryLabel,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 15,
        });
        SelectionCard.Children.Add(new TextBlock
        {
            Text = ViewModel.SelectedSummary,
            FontFamily = (FontFamily)Application.Current.Resources["BalooSemiBoldFont"],
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 15,
        });

        // The details themselves, in a scroller of their own so a long chain
        // above never buries them.
        ScrollViewer scroller = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
        };
        StackPanel cells = new() { Spacing = 9 };
        scroller.Content = cells;
        SelectionCard.Children.Add(scroller);

        VpsSystemInfo system = record.System ?? new VpsSystemInfo();

        void AddCell(
            string label, string value, string icon, string unit = "", bool secret = false, bool copy = false)
        {
            if (value.Trim().Length == 0 && unit.Trim().Length == 0)
            {
                return;
            }

            cells.Children.Add(new FieldCell
            {
                Label = label,
                Value = value,
                UnitValue = unit,
                IconKind = icon,
                Kind = FieldCellKind.Display,
                IsSecret = secret,
                ShowCopy = copy,
            });
        }

        AddCell("IP ADDRESS", system.Ip, "Globe", copy: true);
        AddCell("PORT", system.Port, "Key", unit: system.PortNumber, copy: true);
        AddCell("OS", system.Os, "Info", copy: true);
        AddCell("USERNAME", system.Username, "User", copy: true);
        AddCell("USER PASSWORD", system.UserPassword, "Lock", secret: true, copy: true);
        AddCell("ROOT USER", system.RootUser, "User", copy: true);
        AddCell("ROOT PASSWORD", system.RootPassword, "Lock", secret: true, copy: true);
        AddCell("CPU", system.Cpu, "Info");
        AddCell("MEMORY (RAM)", system.Memory, "Info");
        AddCell("DISK", system.Disk, "Info");
        AddCell("BANDWIDTH", system.Bandwidth, "Info");
        AddCell("OUT DATE", record.OutDate, "Calendar");

        if (record.CycleLabel.Length > 0)
        {
            AddCell("CYCLE", record.CycleLabel, "Clock");
        }

        if (record.Balance is { } balance)
        {
            AddCell(
                "BALANCE",
                $"{balance:0.##} {record.Currency}".Trim(),
                "Bitcoin",
                unit: record.BalanceBtc is { } btc ? $"{btc:0.########} BTC" : string.Empty);
        }

        if (cells.Children.Count == 0)
        {
            SelectionCard.Children.Add(new TextBlock
            {
                Text = "Nothing recorded for this one yet",
                FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    // ------------------------------------------------------------------
    // The chooser
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills the chooser panel from the view model's rows: one row per main
    /// VPS the country holds, its flag, its name, and how much hangs off it.
    /// </summary>
    private void RebuildChooser()
    {
        ChooserRows.Children.Clear();

        if (ViewModel.Chooser is not { } chooser)
        {
            ChooserCard.Visibility = Visibility.Collapsed;
            return;
        }

        ChooserFlag.Country = chooser.FlagCountry;
        ChooserTitle.Text =
            $"{chooser.Rows.Count} MAIN VPS{(chooser.Rows.Count == 1 ? string.Empty : "S")} "
            + $"IN {chooser.Country.ToUpperInvariant()}";

        foreach (ChooserRow row in chooser.Rows)
        {
            ChooserRows.Children.Add(BuildChooserRow(row));
        }

        PositionOverlay();
    }

    /// <summary>One row of the chooser, built by hand as the drawers are.</summary>
    private Button BuildChooserRow(ChooserRow row)
    {
        Button button = new()
        {
            Style = (Style)Application.Current.Resources["BareButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Border shell = new()
        {
            Height = 54,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0xF6, 0xF9)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE4, 0xE6, 0xE9)),
            BorderThickness = new Thickness(1),
        };

        Grid layout = new() { ColumnSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The flag, or the globe when the record has no country.
        Viewbox flagBox = new() { Width = 20, Height = 15, VerticalAlignment = VerticalAlignment.Center };
        FlagIcon flag = new();

        if (row.FlagCountry is not null)
        {
            flag.Country = row.FlagCountry;
            flagBox.Child = flag;
        }
        else
        {
            flagBox.Child = new LineIcon
            {
                Kind = "Globe",
                Width = 16,
                Height = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xA0, 0xA5, 0xAB)),
            };
        }

        Grid.SetColumn(flagBox, 0);
        layout.Children.Add(flagBox);

        StackPanel texts = new() { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        TextBlock name = new()
        {
            Text = row.Label,
            FontFamily = (FontFamily)Application.Current.Resources["BalooSemiBoldFont"],
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2F, 0x33)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
        };
        TextBlock detail = new()
        {
            Text = row.Detail,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x7A, 0x87, 0x98)),
        };
        texts.Children.Add(name);
        texts.Children.Add(detail);

        Grid.SetColumn(texts, 1);
        layout.Children.Add(texts);

        shell.Child = layout;
        button.Content = shell;
        button.Click += (_, _) => ViewModel.ChooseMain(row.Record);

        return button;
    }
}
