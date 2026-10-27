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
/// One dot on the VPS map. See the XAML for how the dot carries its flag,
/// its count, its MAIN tag, its label and its hover card - and why the
/// overhang lives on a canvas rather than in the root grid.
/// </summary>
public sealed partial class MapDot : UserControl
{
    /// <summary>Whether the pointer is over the dot, debounced for the bubbling exits.</summary>
    private bool _entered;

    /// <summary>Whether the heartbeat storyboard is running.</summary>
    private bool _pulsing;

    public MapDot()
    {
        InitializeComponent();

        // The entrance starts the dot dark and small; the storyboard on load
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

    /// <summary>The dot's data: what it is, where it is, what it says.</summary>
    public MapPin? Pin
    {
        get => (MapPin?)GetValue(PinProperty);
        set => SetValue(PinProperty, value);
    }

    /// <summary>The dot was clicked: the map decides what that means.</summary>
    public event EventHandler<MapPin>? PinTapped;

    private static void OnPinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MapDot)d).Apply();

    /// <summary>
    /// The dot's arrival: a staggered pop, so a view's dots land one after
    /// another in the order the view model numbered them.
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
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
        };
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");
        entrance.Children.Add(fade);

        entrance.Begin();
    }

    /// <summary>
    /// Pushes the pin into the dot: size, ring, flag, badge, tag, label and
    /// hover card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The coin is the root's own square; everything else is seated on the
    /// overhang canvas, measured first so it can be centred on the coin
    /// without the canvas constraining it. A canvas measures its children
    /// against infinity, so the measure is the element's honest size.
    /// </para>
    /// <para>
    /// The seats are computed from the dot's size, so a 28-pixel node and a
    /// 40-pixel main carry their extras at the same breathing distance.
    /// </para>
    /// </remarks>
    private void Apply()
    {
        if (Pin is not { } pin)
        {
            return;
        }

        bool isMainKind = pin.Kind is MapPinKind.Main or MapPinKind.Cluster or MapPinKind.TreeMain;
        double size = pin.DotSize;

        // The coin and its halo.
        Root.Width = size;
        Root.Height = size;
        Coin.Width = size;
        Coin.Height = size;
        Coin.Stroke = (Brush)Resources[isMainKind ? "MainRingBrush" : "NodeRingBrush"];
        Coin.StrokeThickness = isMainKind ? 3 : 2.25;

        if (isMainKind)
        {
            Halo.Width = size + 8;
            Halo.Height = size + 8;
            Halo.Opacity = 1;
            HaloWash.Width = size + 18;
            HaloWash.Height = size + 18;
            HaloWash.Opacity = 1;
        }
        else
        {
            Halo.Opacity = 0;
            HaloWash.Opacity = 0;
        }

        // The heartbeat belongs to the root of an open network alone: the
        // pulse is how the eye finds the centre of the network.
        SetPulse(pin.Kind == MapPinKind.TreeMain, size);

        // The flag inside the coin, or the globe when there is no country.
        bool hasFlag = pin.FlagCountry is not null;
        FlagBox.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;
        NoCountryBox.Visibility = hasFlag ? Visibility.Collapsed : Visibility.Visible;

        if (hasFlag)
        {
            Flag.Country = pin.FlagCountry;
            double flagWidth = size >= 34 ? 21 : 18;
            FlagBox.Width = flagWidth;
            FlagBox.Height = flagWidth * 15.0 / 21.0;
        }

        // The count badge, only on a country holding several mains: hung off
        // the coin's top-right shoulder.
        bool isCluster = pin.Kind == MapPinKind.Cluster && pin.Cluster is { Count: > 1 };
        CountBadge.Visibility = isCluster ? Visibility.Visible : Visibility.Collapsed;

        if (isCluster)
        {
            CountText.Text = $"\u00D7{pin.Cluster!.Count}";
            SeatOverhang(CountBadge, right: 20, top: -8, fromRight: true);
        }

        // The MAIN tag, only on the root of an open network: centred above
        // the coin, clear of the halo.
        MainTag.Visibility = pin.Kind == MapPinKind.TreeMain
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (pin.Kind == MapPinKind.TreeMain)
        {
            SeatOverhang(MainTag, centre: true, top: -26);
        }

        // The label under the coin, when there is one.
        bool hasLabel = pin.Label.Trim().Length > 0;
        LabelPill.Visibility = hasLabel ? Visibility.Visible : Visibility.Collapsed;

        if (hasLabel)
        {
            LabelText.Text = pin.Label;
            LabelText.FontFamily = (FontFamily)Application.Current.Resources[isMainKind
                ? "BalooSemiBoldFont"
                : "BalooFont"];
            LabelText.FontSize = pin.Kind == MapPinKind.TreeMain ? 12.5 : 12;
            SeatOverhang(LabelPill, centre: true, bottom: size / 2 + 13, fromBottom: true);
        }

        // The hover card, above by default and under when the dot sits near
        // the top of the map.
        TipTitle.Text = pin.TooltipTitle;
        TipDetail.Text = pin.TooltipDetail;
        TipMeta.Text = pin.TooltipMeta;
        TipFlagBox.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;
        TipCard.Visibility = Visibility.Visible;

        if (hasFlag)
        {
            TipFlag.Country = pin.FlagCountry;
        }

        // The clearance the card needs: past the MAIN tag when there is one,
        // past the badge's shoulder when there is not - and past the label
        // when it opens downward instead.
        double above = pin.Kind == MapPinKind.TreeMain ? 134 : 96;
        double below = size / 2 + 41 + 84;

        // The card is measured against infinity, so its honest size is
        // known before it is seated: centred on the coin, hanging down from
        // its clearance above or standing on it below.
        TipCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        if (pin.TooltipAbove)
        {
            Canvas.SetLeft(TipCard, size / 2 - TipCard.DesiredSize.Width / 2);
            Canvas.SetTop(TipCard, -above);
        }
        else
        {
            Canvas.SetLeft(TipCard, size / 2 - TipCard.DesiredSize.Width / 2);
            Canvas.SetTop(TipCard, size + below - TipCard.DesiredSize.Height);
        }
    }

    /// <summary>
    /// Starts or stops the main's heartbeat, sized to its coin.
    /// </summary>
    private void SetPulse(bool on, double size)
    {
        if (on == _pulsing && on && Math.Abs(PulseRing.Width - size) < 0.5)
        {
            return;
        }

        if (on && Resources["PulseStoryboard"] is Storyboard pulse)
        {
            PulseRing.Width = size;
            PulseRing.Height = size;
            PulseRing.Opacity = 0.6;
            pulse.Begin();
            _pulsing = true;
        }
        else
        {
            if (_pulsing && Resources["PulseStoryboard"] is Storyboard pulse)
            {
                pulse.Stop();
            }

            PulseRing.Opacity = 0;
            _pulsing = false;
        }
    }

    /// <summary>
    /// Seats an overhang element on its canvas: centred on the coin, or hung
    /// off one of its corners.
    /// </summary>
    /// <remarks>
    /// The element is measured against infinity first - a canvas does not
    /// size its children, so the measure is the only way to know how wide the
    /// words are.
    /// </remarks>
    private void SeatOverhang(
        FrameworkElement element,
        bool centre = false,
        double right = 0,
        double top = 0,
        double bottom = 0,
        bool fromRight = false,
        bool fromBottom = false)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        if (centre)
        {
            Canvas.SetLeft(element, Root.Width / 2 - element.DesiredSize.Width / 2);
        }
        else if (fromRight)
        {
            Canvas.SetLeft(element, Root.Width + right - element.DesiredSize.Width);
        }

        if (fromBottom)
        {
            Canvas.SetTop(element, Root.Height + bottom - element.DesiredSize.Height);
        }
        else
        {
            Canvas.SetTop(element, top);
        }
    }

    /// <summary>
    /// The dot leans toward the pointer and speaks: its hover card says in
    /// words what its colours just said.
    /// </summary>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (_entered)
        {
            return;
        }

        _entered = true;

        if (Resources["HoverOn"] is Storyboard hover)
        {
            hover.Begin();
        }

        if (Resources["TipShow"] is Storyboard tip)
        {
            tip.Begin();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _entered = false;

        if (Resources["HoverOff"] is Storyboard hover)
        {
            hover.Begin();
        }

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
