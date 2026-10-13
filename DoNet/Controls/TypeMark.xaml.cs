using System;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DoNet.Controls;

/// <summary>
/// One primary service type's tile: the type's gradient on a 64px rounded
/// square, the type's icon in white centred inside - see the XAML for why the
/// paths are built in code.
/// </summary>
public sealed partial class TypeMark : UserControl
{
    public TypeMark()
    {
        InitializeComponent();

        // The default kind never raises the changed callback, so the tile is
        // drawn once here and redrawn only if the kind actually moves.
        Build();
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(PrimaryServiceType), typeof(TypeMark),
        new PropertyMetadata(PrimaryServiceType.Vps, OnKindChanged));

    /// <summary>Which type's tile this is.</summary>
    public PrimaryServiceType Kind
    {
        get => (PrimaryServiceType)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TypeMark)sender).Build();

    /// <summary>
    /// Draws the tile: the gradient on the square, one white path per SVG
    /// source shape at the icon's natural size, centred.
    /// </summary>
    private void Build()
    {
        Sheet.Children.Clear();

        (double width, double height) = PrimaryServiceTypes.MarkBoxOf(Kind);
        Sheet.Width = width;
        Sheet.Height = height;

        // The colour is the button's, not the icon's.
        Tile.Background = PrimaryServiceTypes.GradientOf(Kind);

        Brush icon = new SolidColorBrush(PrimaryServiceTypes.IconWhite);

        foreach (string data in PrimaryServiceTypes.MarkOf(Kind))
        {
            Sheet.Children.Add(new Path
            {
                Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
                Fill = icon,
            });
        }
    }
}
