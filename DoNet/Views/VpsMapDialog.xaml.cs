using System;
using System.Collections.Generic;
using System.ComponentModel;
using DoNet.Controls;
using DoNet.Services;
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
/// The VPS map dialog. See the XAML for how the plate, the dots and the two
/// views fit together; this code-behind owns the parts a XAML file cannot.
/// </summary>
/// <remarks>
/// <para>
/// The dots and the connections are built in code rather than templated:
/// they are positioned in pixels by the view model's layout pass, and an
/// element the code positions is an element the code may as well create.
/// Rebuilding happens on the view model's say-so - a new set of pins, a new
/// set of lines - and repositioning on the window's, so a resize never
/// replays the dots' arrival.
///</para>
/// <para>
/// The map's height is the one size the dialog computes for itself: the
/// plate is exactly 2:1, so the map takes the width it is given and the
/// height the window can spare, clamped so a small window still shows a
/// whole earth rather than a strip of it.
///</para>
///</remarks>
public sealed partial class VpsMapDialog : UserControl
{
    /// <summary>The dots on the plate, in view model order.</summary>
    private readonly List<MapDot> _dots = new();

    /// <summary>The connections, paired with the segments that draw them.</summary>
    private readonly List<(Line Shape, MapLine Model)> _lines = new();

    /// <summary>Where a drag began, in host pixels, or null when there is no drag.</summary>
    private Point? _dragStart;

    /// <summary>Whether the pointer has moved far enough to count as a drag.</summary>
    private bool _dragging;

    public VpsMapDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind
        // binds its root object while the generated code runs.
        ViewModel = App.Current.Services.GetRequiredService<VpsMapViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The map's height follows the window's spare room, so the dialog is
        // never taller than the screen it is on.
        Root.SizeChanged += (_, _) => SizeMap();

        // A deferred control can be created *because* it is already supposed
        // to be showing. Catch up once in the tree, like the other dialogs.
        Loaded += (_, _) =>
        {
            if (ViewModel.IsDialogOpen && Root.Visibility != Visibility.Visible)
            {
                Open();
            }
        };

        // HomePage is rebuilt on every unlock, and the view model is a
        // singleton, so without this each lock/unlock cycle leaves another
        // detached dialog listening to it.
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>The map's state and its data.</summary>
    public VpsMapViewModel ViewModel { get; }

    // ------------------------------------------------------------------
    // The view model's word
    // ------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(VpsMapViewModel.IsDialogOpen):
                if (ViewModel.IsDialogOpen)
                {
                    Open();
                }
                else if (Root.Visibility == Visibility.Visible)
                {
                    Close();
                }
                break;

            case nameof(VpsMapViewModel.Pins):
                RebuildDots();
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
                // rectangle and the pins - the same objects, in new places -
                // are re-seated onto it. Rebuilding never happens here, so
                // the dots' arrival never replays under the wheel.
                ApplyImageRect();
                PositionOverlay();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Open and close
    // ------------------------------------------------------------------

    private void Open()
    {
        Root.Visibility = Visibility.Visible;

        if (Resources["OpenStoryboard"] is Storyboard open)
        {
            open.Begin();
        }
    }

    /// <summary>
    /// Plays the exit, then hides. Hiding only after the animation completes
    /// is the whole point - collapsing first would make the close instant and
    /// the storyboard invisible.
    /// </summary>
    private void Close()
    {
        if (Resources["CloseStoryboard"] is Storyboard close)
        {
            close.Begin();
        }
        else
        {
            Root.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCloseCompleted(object? sender, object args)
    {
        Root.Visibility = Visibility.Collapsed;

        // Reset the transforms the exit left behind, or the next open starts
        // displaced.
        CardOffset.Y = 0;
        Card.Opacity = 1;
        Scrim.Opacity = 1;
    }

    /// <summary>Click-away on the backdrop closes, as a modal should.</summary>
    private void OnScrimTapped(object sender, TappedRoutedEventArgs args) => ViewModel.Close();

    private void OnCloseClick(object sender, RoutedEventArgs args) => ViewModel.Close();

    private void OnBackClick(object sender, RoutedEventArgs args) => ViewModel.BackToOverview();

    private void OnChooserCloseClick(object sender, RoutedEventArgs args)
        => ViewModel.HandleBackgroundTap();

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page
    /// is leaving for good.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();

    // ------------------------------------------------------------------
    // Sizing
    // ------------------------------------------------------------------

    /// <summary>
    /// Gives the map its height: the width it is given halved (the plate is
    /// exactly 2:1), capped by the room the window can spare, clamped so the
    /// earth is never reduced to a strip.
    /// </summary>
    private void SizeMap()
    {
        double availableWidth = Math.Max(0, Root.ActualWidth - 48);
        double availableHeight = Math.Max(0, Root.ActualHeight - 48);

        double cardInnerWidth = Math.Max(120, Math.Min(availableWidth, 1420) - 72);
        double byWidth = cardInnerWidth / 2;
        double byHeight = Math.Max(120, availableHeight - 238);
        double mapHeight = Math.Clamp(Math.Min(byWidth, byHeight), 280, 620);

        MapHost.Height = mapHeight;
    }

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
    /// A press on the map itself - the sea or the land, not a dot or a panel
    /// - may become a drag of the view.
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
    /// reaches the plate's canvas or the dot layer's background before any of
    /// the panels that sit over them.
    /// </summary>
    private bool PressStartsOnMap(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is MapDot || node == FocusedHeader || node == ChooserLayer)
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
    /// Rebuilds the dots from the view model's pins, then seats them. The
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
        foreach ((Line shape, _) in _lines)
        {
            MapCanvas.Children.Remove(shape);
        }

        _lines.Clear();

        AddLines(ViewModel.DeepLines, dashed: true);
        AddLines(ViewModel.Lines, dashed: false);

        PositionOverlay();
    }

    private void AddLines(IReadOnlyList<MapLine>? segments, bool dashed)
    {
        if (segments is null)
        {
            return;
        }

        foreach (MapLine segment in segments)
        {
            Line shape = new()
            {
                Stroke = (Brush)Resources[dashed ? "DeepConnectionBrush" : "ConnectionBrush"],
                StrokeThickness = dashed ? 1.5 : 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                UseLayoutRounding = true,
            };

            if (dashed)
            {
                shape.StrokeDashArray = new DoubleCollection { 4, 2.5 };
            }

            _lines.Add((shape, segment));
            MapCanvas.Children.Add(shape);
        }
    }

    /// <summary>
    /// Seats everything on the plate at its laid-out position: the dots
    /// centred on theirs, the connections end to end, the chooser beside its
    /// dot.
    /// </summary>
    private void PositionOverlay()
    {
        foreach (MapDot dot in _dots)
        {
            if (dot.Pin is not { } pin)
            {
                continue;
            }

            Canvas.SetLeft(dot, pin.X - pin.DotSize / 2);
            Canvas.SetTop(dot, pin.Y - pin.DotSize / 2);
        }

        foreach ((Line shape, MapLine model) in _lines)
        {
            shape.X1 = model.X1;
            shape.Y1 = model.Y1;
            shape.X2 = model.X2;
            shape.Y2 = model.Y2;
        }

        if (ViewModel.Chooser is { } chooser)
        {
            ChooserCard.Visibility = Visibility.Visible;
            Canvas.SetLeft(ChooserCard, chooser.X);
            Canvas.SetTop(ChooserCard, chooser.Y);
        }
        else
        {
            ChooserCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// A tap on the map itself, away from any dot: the chooser steps aside.
    /// </summary>
    /// <remarks>
    /// The tap that opens a chooser bubbles up from the dot to this layer as
    /// well, so the climb up the tree skips the taps that belong to a dot.
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
