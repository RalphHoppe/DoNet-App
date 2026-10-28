using System;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// One marker on the VPS map. See the XAML for how the delta carries its
/// callout, its count and its selection ring - and why the words live on an
/// overhang canvas rather than in the root grid.
/// </summary>
public sealed partial class MapDot : UserControl
{
    /// <summary>Whether the pointer is over the marker, debounced for the bubbling exits.</summary>
    private bool _entered;

    public MapDot()
    {
        InitializeComponent();

        // The entrance starts the marker hidden and small; the storyboard on
        // load brings it in.
        Root.Opacity = 0;
        EntryScale.ScaleX = 0.6;
        EntryScale.ScaleY = 0.6;

        Loaded += OnLoadedOnce;
    }

    /// <summary>Identifies the <see cref="Pin"/> property.</summary>
    public static readonly DependencyProperty PinProperty = DependencyProperty.Register(
        nameof(Pin), typeof(MapPin), typeof(MapDot),
        new PropertyMetadata(null, OnPinChanged));

    /// <summary>The pin's data: what it is, where it is, what it says.</summary>
    public MapPin? Pin
    {
        get => (MapPin?)GetValue(PinProperty);
        set => SetValue(PinProperty, value);
    }

    /// <summary>The marker was clicked: the map decides what that means.</summary>
    public event EventHandler<MapPin>? PinTapped;

    /// <summary>
    /// How far the anchor point sits from the control's left edge: the tip's
    /// horizontal centre. The caller seats the control so the tip lands on
    /// the anchor.
    /// </summary>
    public double TipOffsetX { get; private set; }

    /// <summary>How far the anchor point sits from the control's top edge.</summary>
    public double TipOffsetY { get; private set; }

    private static void OnPinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MapDot)d).Apply();

    /// <summary>
    /// The marker's arrival: a staggered drop, so a view's markers land one
    /// after another in the order the view model numbered them - each from
    /// its own tip, like pins being pressed into the page.
    /// </summary>
    /// <remarks>
    /// Built in code because the delay is the marker's arrival index, which
    /// only the instance knows. The base values are set in the constructor
    /// and re-set here, so a marker arriving into an already-loaded control
    /// still enters rather than appearing fully formed.
    /// </remarks>
    private void OnLoadedOnce(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoadedOnce;

        if (Pin is not { } pin)
        {
            Root.Opacity = 1;
            EntryScale.ScaleX = 1;
            EntryScale.ScaleY = 1;
            return;
        }

        Root.Opacity = 0;
        EntryScale.ScaleX = 0.6;
        EntryScale.ScaleY = 0.6;

        Storyboard entrance = new()
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Min(pin.Index, 20) * 26),
        };

        foreach (string axis in new[] { "ScaleX", "ScaleY" })
        {
            DoubleAnimation rise = new()
            {
                From = 0.6,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(rise, EntryScale);
            Storyboard.SetTargetProperty(rise, axis);
            entrance.Children.Add(rise);
        }

        DoubleAnimation fade = new()
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(190)),
        };
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");
        entrance.Children.Add(fade);

        entrance.Begin();
    }

    /// <summary>
    /// Pushes the pin into the delta: geometry, fill, count, callout and
    /// selection ring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delta is built from one width, in the pin's own terms: a triangle
    /// as wide as the pin's size, about four fifths as tall, its tip at the
    /// bottom centre. Everything else on the marker - the ring, the count
    /// chip, the callout - is seated from the same numbers, so one size
    /// change reshapes the whole marker.
    /// </para>
    /// <para>
    /// The words live on the overhang canvas, measured against infinity
    /// first so they can be centred on the marker without the canvas
    /// constraining them.
    /// </para>
    ///</remarks>
    private void Apply()
    {
        if (Pin is not { } pin)
        {
            return;
        }

        bool isMainKind = pin.Kind is MapPinKind.Main or MapPinKind.Cluster or MapPinKind.TreeMain;

        // The footprint: the delta's width plus stroke room; its height is
        // the width's share, plus room for the white halo.
        double width = pin.DotSize;
        double height = Math.Round(width * 0.82);
        Root.Width = width + 8;
        Root.Height = height + 8;

        double tipY = Root.Height - 4;
        double cx = Root.Width / 2;
        TipOffsetX = cx;
        TipOffsetY = tipY;

        // The marker grows from its tip, wherever it is planted. The point
        // is built with float parts - fully qualified and narrow, so the
        // line means the same thing to every binding of the name Point.
        Root.RenderTransformOrigin = new Windows.Foundation.Point(0.5f, (float)(tipY / Root.Height));

        // The delta: a triangle whose tip sits at the anchor.
        Delta.Data = DeltaGeometry(cx, tipY, width, height);
        Delta.Fill = (Brush)Resources[isMainKind ? "MainFillBrush" : "NodeFillBrush"];

        // The selection ring, circling the delta's visual centre, a breath
        // outside it.
        double centreY = tipY - (height * 0.42);
        double ringSize = (width * 0.62) + 9;
        SelectHalo.Width = ringSize + 2;
        SelectHalo.Height = ringSize + 2;
        SelectRing.Width = ringSize;
        SelectRing.Height = ringSize;
        Canvas.SetLeft(SelectHalo, cx - (ringSize / 2) - 1);
        Canvas.SetTop(SelectHalo, centreY - (ringSize / 2) - 1);
        Canvas.SetLeft(SelectRing, cx - (ringSize / 2));
        Canvas.SetTop(SelectRing, centreY - (ringSize / 2));

        // The count badge, only on a country holding several mains: hung off
        // the delta's right shoulder.
        bool isCluster = pin.Kind == MapPinKind.Cluster && pin.Cluster is { Count: > 1 };
        CountBadge.Visibility = isCluster ? Visibility.Visible : Visibility.Collapsed;

        if (isCluster)
        {
            CountText.Text = $"\u00D7{pin.Cluster!.Count}";
            CountBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(CountBadge, cx + (width * 0.34));
            Canvas.SetTop(CountBadge, tipY - height - 2);
        }

        // The callout: the country's name in the overview, the record's name
        // in an open network, above the delta.
        CalloutText.Text = pin.Kind switch
        {
            MapPinKind.Main or MapPinKind.Cluster => pin.TooltipTitle,
            _ => string.IsNullOrWhiteSpace(pin.TooltipDetail) ? pin.TooltipTitle : pin.TooltipDetail,
        };
        Callout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Callout, cx - (Callout.DesiredSize.Width / 2));
        Canvas.SetTop(Callout, tipY - height - Callout.DesiredSize.Height - 6);
    }

    /// <summary>
    /// The delta's geometry: a triangle as wide as the given width, about
    /// four fifths as tall, tip at the bottom centre.
    /// </summary>
    private static PathGeometry DeltaGeometry(double cx, double tipY, double width, double height)
    {
        PathFigure figure = new()
        {
            StartPoint = new Point(cx - (width / 2), tipY - height),
            IsClosed = true,
            IsFilled = true,
        };

        figure.Segments.Add(new LineSegment { Point = new Point(cx + (width / 2), tipY - height) });
        figure.Segments.Add(new LineSegment { Point = new Point(cx, tipY) });

        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// The marker stands up toward the pointer and speaks its place's name.
    /// The lift also raises it above its siblings on the layer, so the
    /// callout is never spoken from underneath another marker.
    /// </summary>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (_entered)
        {
            return;
        }

        _entered = true;
        Canvas.SetZIndex(this, 100);
        HighlightOn();
        ShowCallout();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _entered = false;
        Canvas.SetZIndex(this, 0);
        HighlightOff();
        HideCallout();
    }

    /// <summary>
    /// Stands the marker up - also called by the map when the marker's row is
    /// hovered in the panel, so the list and the page speak with one voice.
    /// </summary>
    public void HighlightOn()
    {
        if (Resources["HoverOn"] is Storyboard hover)
        {
            hover.Begin();
        }
    }

    /// <summary>Returns the marker to its rest.</summary>
    public void HighlightOff()
    {
        if (Resources["HoverOff"] is Storyboard hover)
        {
            hover.Begin();
        }
    }

    /// <summary>
    /// Marks this marker as the one whose story the panel is telling: a
    /// quiet teal ring around the delta. Also called by the map when the
    /// selection changes, so the page and the panel agree on who is being
    /// read.
    /// </summary>
    public void SetSelected(bool on)
    {
        SelectHalo.Opacity = on ? 1 : 0;
        SelectRing.Opacity = on ? 1 : 0;
    }

    private void ShowCallout()
    {
        if (Resources["TipShow"] is Storyboard tip)
        {
            tip.Begin();
        }
    }

    private void HideCallout()
    {
        if (Resources["TipHide"] is Storyboard tip)
        {
            tip.Begin();
        }
    }

    private void OnClick(object sender, RoutedEventArgs args)
    {
        if (Pin is { } pin)
        {
            PinTapped?.Invoke(this, pin);
        }
    }
}
