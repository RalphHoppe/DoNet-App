using System;
using System.Globalization;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// One pin on the VPS map. See the XAML for how the teardrop carries its
/// flag, its count, its label and its hover card - and why the words live on
/// an overhang canvas rather than in the root grid.
/// </summary>
public sealed partial class MapDot : UserControl
{
    /// <summary>Whether the pointer is over the pin, debounced for the bubbling exits.</summary>
    private bool _entered;

    /// <summary>Whether the ping storyboard is running.</summary>
    private bool _pulsing;

    public MapDot()
    {
        InitializeComponent();

        // The entrance starts the pin dark and small; the storyboard on load
        // brings it in. Until the control is loaded there is nothing to see,
        // so the base values only matter once it is in the tree.
        Root.Opacity = 0;
        EntryScale.ScaleX = 0.55;
        EntryScale.ScaleY = 0.55;

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

    /// <summary>The pin was clicked: the map decides what that means.</summary>
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
    /// The pin's arrival: a staggered drop, so a view's pins land one after
    /// another in the order the view model numbered them - each growing from
    /// its own tip, like pins being pressed into the page.
    /// </summary>
    /// <remarks>
    /// Built in code because the delay is the pin's arrival index, which only
    /// the instance knows. The base values are set in the constructor and
    /// re-set here, so a pin arriving into an already-loaded control still
    /// enters rather than appearing fully formed.
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
        EntryScale.ScaleX = 0.55;
        EntryScale.ScaleY = 0.55;

        Storyboard entrance = new()
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Min(pin.Index, 20) * 30),
        };

        foreach (string axis in new[] { "ScaleX", "ScaleY" })
        {
            DoubleAnimation rise = new()
            {
                From = 0.55,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
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
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
        };
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");
        entrance.Children.Add(fade);

        entrance.Begin();
    }

    /// <summary>
    /// Pushes the pin into the teardrop: geometry, stroke, flag, badge,
    /// label and hover card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The teardrop is built from one head radius, in the pin's own terms:
    /// a circle of that radius as the head, sides curving down to a point
    /// whose tip is the anchor. Everything else on the pin - shadow, flag,
    /// badge, ping - is seated from the same numbers, so one size change
    /// reshapes the whole marker.
    /// </para>
    /// <para>
    /// The words live on the overhang canvas, measured against infinity
    /// first so they can be centred on the pin without the canvas
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
        double head = pin.DotSize / 2.0;

        // The footprint: the teardrop's width is the head's diameter plus
        // stroke room; its height is the head's arc plus the taper to the
        // tip, plus a little ground for the shadow.
        Root.Width = (head * 2) + 8;
        Root.Height = (head * 2.9) + 8;

        double tipY = Root.Height - 2;
        double cx = Root.Width / 2;
        TipOffsetX = cx;
        TipOffsetY = tipY;

        // The pin grows from its tip, wherever it is planted. The point is
        // built with float parts - fully qualified and narrow, so the line
        // means the same thing to every binding of the name Point.
        Root.RenderTransformOrigin = new Windows.Foundation.Point(0.5f, (float)(tipY / Root.Height));

        // The body: a circle of the head's radius, its sides curving to the
        // tip. Built as a path in the pin's own coordinates.
        PinBody.Data = PinGeometry(cx, tipY, head);
        PinBody.Stroke = (Brush)Resources[isMainKind ? "MainRingBrush" : "NodeRingBrush"];
        PinBody.StrokeThickness = isMainKind ? 2.5 : 2;

        // The ground shadow, under the tip.
        GroundShadow.Width = head * 1.15;
        GroundShadow.Height = Math.Max(4, head * 0.32);
        GroundShadow.Margin = new Thickness(
            cx - (head * 0.575), tipY - (GroundShadow.Height / 2) + 1, 0, 0);
        GroundShadow.HorizontalAlignment = HorizontalAlignment.Left;
        GroundShadow.VerticalAlignment = VerticalAlignment.Top;

        // The flag in the head, or the globe when there is no country.
        bool hasFlag = pin.FlagCountry is not null;
        FlagBox.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;
        NoCountryBox.Visibility = hasFlag ? Visibility.Collapsed : Visibility.Visible;

        if (hasFlag)
        {
            Flag.Country = pin.FlagCountry;
            double flagWidth = head * 1.24;
            FlagBox.Width = flagWidth;
            FlagBox.Height = flagWidth * 15.0 / 21.0;
            FlagBox.Margin = new Thickness(cx - (flagWidth / 2), tipY - (head * 1.9) - (FlagBox.Height / 2), 0, 0);
            FlagBox.HorizontalAlignment = HorizontalAlignment.Left;
            FlagBox.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            double icon = head * 1.1;
            NoCountryBox.Width = icon;
            NoCountryBox.Height = icon;
            NoCountryBox.Margin = new Thickness(cx - (icon / 2), tipY - (head * 1.9) - (icon / 2), 0, 0);
            NoCountryBox.HorizontalAlignment = HorizontalAlignment.Left;
            NoCountryBox.VerticalAlignment = VerticalAlignment.Top;
        }

        // The focused main's ping, planted at the tip.
        SetPulse(pin.Kind == MapPinKind.TreeMain, cx, tipY, head);

        // The count badge, only on a country holding several mains: hung off
        // the head's top-right shoulder.
        bool isCluster = pin.Kind == MapPinKind.Cluster && pin.Cluster is { Count: > 1 };
        CountBadge.Visibility = isCluster ? Visibility.Visible : Visibility.Collapsed;

        if (isCluster)
        {
            CountText.Text = $"\u00D7{pin.Cluster!.Count}";
            CountBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(CountBadge, cx + (head * 0.4));
            Canvas.SetTop(CountBadge, tipY - (head * 2.9) - 6);
        }

        // The label under the tip, when there is one.
        bool hasLabel = pin.Label.Trim().Length > 0;
        LabelPill.Visibility = hasLabel ? Visibility.Visible : Visibility.Collapsed;

        if (hasLabel)
        {
            LabelText.Text = pin.Label;
            LabelText.FontFamily = (FontFamily)Application.Current.Resources[isMainKind
                ? "BalooSemiBoldFont"
                : "BalooFont"];
            LabelPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(LabelPill, cx - (LabelPill.DesiredSize.Width / 2));
            Canvas.SetTop(LabelPill, Root.Height + 2 + pin.LabelDrop);
        }

        // The hover card: the country in words, the record's name, its place
        // in the network. Above the head by default, under the label when
        // the pin sits near the top of the map.
        TipTitle.Text = pin.TooltipTitle;
        TipDetail.Text = pin.TooltipDetail;
        TipMeta.Text = pin.TooltipMeta;
        TipFlagBox.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;

        if (hasFlag)
        {
            TipFlag.Country = pin.FlagCountry;
        }

        TipCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cardWidth = TipCard.DesiredSize.Width;
        double cardHeight = TipCard.DesiredSize.Height;

        if (pin.TooltipAbove)
        {
            Canvas.SetLeft(TipCard, cx - (cardWidth / 2));
            Canvas.SetTop(TipCard, tipY - (head * 2.9) - cardHeight - 8);
        }
        else
        {
            Canvas.SetLeft(TipCard, cx - (cardWidth / 2));
            Canvas.SetTop(TipCard, Root.Height + 30);
        }
    }

    /// <summary>
    /// The teardrop's geometry: a head circle of the given radius whose
    /// sides curve down to a point at the tip.
    /// </summary>
    private static string PinGeometry(double cx, double tipY, double head)
    {
        static string Pt(double x, double y)
            => string.Create(CultureInfo.InvariantCulture, $"{x:0.###},{y:0.###}");

        double headY = tipY - (head * 1.9);

        // Tip, left curve to the head's left edge, the head's arc over the
        // top, right curve back down to the tip.
        return string.Join(' ',
            "M", Pt(cx, tipY),
            "C", Pt(cx - head * 0.6, tipY - head * 0.8),
                  Pt(cx - head, tipY - head * 1.3),
                  Pt(cx - head, headY),
            "A", Pt(head, head), "0 1 1",
                  Pt(cx + head, headY),
            "C", Pt(cx + head, tipY - head * 1.3),
                  Pt(cx + head * 0.6, tipY - head * 0.8),
                  Pt(cx, tipY),
            "Z");
    }

    /// <summary>
    /// Starts or stops the focused main's ping, planted at the tip.
    /// </summary>
    private void SetPulse(bool on, double cx, double tipY, double head)
    {
        // Resolved once, before the branches: a pattern variable declared in
        // the if condition is in scope in the else branch too, so the two
        // branches cannot each declare their own board.
        Storyboard? board = Resources["PulseStoryboard"] as Storyboard;

        if (on && board is not null)
        {
            double size = Math.Max(10, head * 0.55);
            PulseRing.Width = size;
            PulseRing.Height = size;
            Canvas.SetLeft(PulseRing, cx - (size / 2));
            Canvas.SetTop(PulseRing, tipY - (size / 2));
            PulseRing.Opacity = 0.55;
            board.Begin();
            _pulsing = true;
        }
        else
        {
            if (_pulsing && board is not null)
            {
                board.Stop();
            }

            PulseRing.Opacity = 0;
            _pulsing = false;
        }
    }

    /// <summary>
    /// The pin leans toward the pointer and speaks: its hover card says in
    /// words what its colours just said.
    /// </summary>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (_entered)
        {
            return;
        }

        _entered = true;
        HighlightOn();
        ShowTip();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _entered = false;
        HighlightOff();
        HideTip();
    }

    /// <summary>
    /// Grows the pin toward the pointer - also called by the map when the
    /// pin's row is hovered in the network panel, so the list and the page
    /// speak with one voice.
    /// </summary>
    public void HighlightOn()
    {
        if (Resources["HoverOn"] is Storyboard hover)
        {
            hover.Begin();
        }
    }

    /// <summary>Returns the pin to its rest.</summary>
    public void HighlightOff()
    {
        if (Resources["HoverOff"] is Storyboard hover)
        {
            hover.Begin();
        }
    }

    private void ShowTip()
    {
        if (Resources["TipShow"] is Storyboard tip)
        {
            tip.Begin();
        }
    }

    private void HideTip()
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
