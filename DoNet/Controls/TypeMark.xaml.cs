using System;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DoNet.Controls;

/// <summary>
/// One primary service type's mark, gradient-filled - see the XAML for why the
/// paths are built in code.
/// </summary>
public sealed partial class TypeMark : UserControl
{
    public TypeMark()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(PrimaryServiceType), typeof(TypeMark),
        new PropertyMetadata(PrimaryServiceType.Vps, OnKindChanged));

    /// <summary>Which type's mark this is.</summary>
    public PrimaryServiceType Kind
    {
        get => (PrimaryServiceType)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TypeMark)sender).Build();

    /// <summary>
    /// Draws the mark: one path per SVG source shape, all sharing the type's
    /// gradient.
    /// </summary>
    private void Build()
    {
        Sheet.Children.Clear();

        (double width, double height) = PrimaryServiceTypes.MarkBoxOf(Kind);
        Sheet.Width = width;
        Sheet.Height = height;

        Brush fill = PrimaryServiceTypes.GradientOf(Kind);

        foreach (string data in PrimaryServiceTypes.MarkOf(Kind))
        {
            Sheet.Children.Add(new Path
            {
                Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
                Fill = fill,
            });
        }
    }
}
