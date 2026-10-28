using System;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// One marker on the VPS map: the country as a flag tile. See the XAML for
/// how the tile carries its callout, its count chip and its selection ring -
/// and why the words live on an overhang canvas rather than in the root.
/// </summary>
public sealed partial class MapMarker : UserControl
{
    /// <summary>Whether the pointer is over the tile, debounced for the bubbling exits.</summary>
    private bool _entered;

    /// <summary>Identifies the <see cref="Model"/> property.</summary>
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MapMarkerItem), typeof(MapMarker),
        new PropertyMetadata(null, OnModelChanged));

    public MapMarker()
    {
        InitializeComponent();

        // The states do not drive themselves inside a UserControl: the
        // pointer's arrival and leaving are forwarded by hand.
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
    }

    /// <summary>The marker's data: what it is, where it is, what it says.</summary>
    public MapMarkerItem? Model
    {
        get => (MapMarkerItem?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>The marker was clicked: the map decides what that means.</summary>
    public event EventHandler<MapMarkerItem>? MarkerTapped;

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MapMarker)d).Apply();

    /// <summary>
    /// Pushes the model into the tile: size, ring, flag, count chip, callout.
    /// </summary>
    /// <remarks>
    /// The tile is centred on its anchor - the caller seats the control so
    /// its middle lands on the country - and everything the tile says is
    /// seated from its own fixed size, so it lands the same way every time.
    /// </remarks>
    private void Apply()
    {
        if (Model is not { } model)
        {
            return;
        }

        bool isMainKind = model.Kind is MapMarkerKind.Main or MapMarkerKind.Cluster;

        // The footprint: a main's tile is the larger, a connected VPS's the
        // smaller; the halo breathes a pixel beyond each.
        double tileW = isMainKind ? 38 : 30;
        double tileH = isMainKind ? 30 : 24;
        Root.Width = tileW + 8;
        Root.Height = tileH + 8;

        Halo.Width = tileW + 6;
        Halo.Height = tileH + 6;
        Halo.CornerRadius = new CornerRadius((tileW + 6) / 2 - 3);
        Halo.Margin = new Thickness(1);

        Tile.Width = tileW;
        Tile.Height = tileH;
        Tile.BorderBrush = (Brush)Resources[isMainKind ? "MainRingBrush" : "NodeRingBrush"];

        // The flag, or the globe when the record has no country.
        bool hasFlag = model.FlagCountry is not null;
        FlagBox.Visibility = hasFlag ? Visibility.Visible : Visibility.Collapsed;
        GlobeBox.Visibility = hasFlag ? Visibility.Collapsed : Visibility.Visible;

        if (hasFlag)
        {
            Flag.Country = model.FlagCountry;
        }

        FlagBox.Width = tileW - 10;
        FlagBox.Height = tileH - 9;
        GlobeBox.Width = tileW - 14;
        GlobeBox.Height = tileH - 13;

        // The selected VPS's ring, around the tile.
        SelectRing.Width = tileW + 10;
        SelectRing.Height = tileH + 10;
        SelectRing.CornerRadius = new CornerRadius((tileW + 10) / 2 - 2);
        Canvas.SetLeft(SelectRing, -1);
        Canvas.SetTop(SelectRing, -1);
        SelectRing.Opacity = model.IsSelected ? 1 : 0;

        // A country holding several mains wears its count, at the tile's
        // top-right shoulder.
        bool hasCount = model.Kind == MapMarkerKind.Cluster && model.ClusterCount > 1;
        CountChip.Visibility = hasCount ? Visibility.Visible : Visibility.Collapsed;

        if (hasCount)
        {
            CountText.Text = $"\u00D7{model.ClusterCount}";
            CountChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(CountChip, Root.Width - 14);
            Canvas.SetTop(CountChip, -9);
        }

        // The callout, above the tile, centred on it. The tile's size is
        // fixed, so this lands the same way every time.
        CalloutText.Text = model.Name;
        Callout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Callout, (Root.Width / 2) - (Callout.DesiredSize.Width / 2));
        Canvas.SetTop(Callout, -Callout.DesiredSize.Height - 9);
    }

    /// <summary>
    /// The view's zoom, so the tile can counter-scale and stay the same size
    /// on screen at every depth.
    /// </summary>
    public void SetZoom(double zoom)
    {
        double scale = 1 / Math.Max(zoom, 0.01);
        ZoomScale.ScaleX = scale;
        ZoomScale.ScaleY = scale;
    }

    /// <summary>
    /// Lifts the tile and speaks its callout - the pointer's own hover, and
    /// the view's forwarding of the panel row's hover, so the list and the
    /// page speak with one voice.
    /// </summary>
    public void SetHover(bool on)
        => _ = VisualStateManager.GoToState(this, on ? "PointerOver" : "Rest", useTransitions: true);

    /// <summary>The pointer arrived: the tile lifts and the callout speaks.</summary>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _entered = true;
        SetHover(on: true);
    }

    /// <summary>The pointer left: the tile settles and the callout goes.</summary>
    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!_entered)
        {
            return;
        }

        _entered = false;
        SetHover(on: false);
    }

    private void OnClick(object sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            MarkerTapped?.Invoke(this, model);
        }
    }
}
