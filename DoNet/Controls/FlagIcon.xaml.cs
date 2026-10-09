using System;
using System.Globalization;
using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DoNet.Controls;

/// <summary>
/// Draws a country's flag, named by country.
/// </summary>
/// <remarks>
/// <para>
/// The artwork comes from <see cref="FlagData"/> as a list of three primitives in a
/// 3 by 2 box - rectangle, ellipse, path - which this turns into shapes on a Canvas
/// that a Viewbox scales. Everything expressive was expanded into those three by the
/// generator, so there is no flag grammar to interpret here.
/// </para>
/// <para>
/// An unknown country draws nothing rather than a placeholder. The picker puts the
/// country's name beside the flag, so a blank space costs the reader nothing, while a
/// wrong flag would cost them trust in all the others.
/// </para>
/// </remarks>
public sealed partial class FlagIcon : UserControl
{
    /// <summary>Identifies the <see cref="Country"/> property.</summary>
    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(
        nameof(Country), typeof(string), typeof(FlagIcon),
        new PropertyMetadata(null, OnCountryChanged));

    /// <summary>Creates the control.</summary>
    public FlagIcon() => InitializeComponent();

    /// <summary>The country whose flag to draw, spelled as in the catalog.</summary>
    public string? Country
    {
        get => (string?)GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }

    private static void OnCountryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((FlagIcon)sender).Rebuild();

    /// <summary>
    /// Replaces the shapes on the canvas with the ones for the current country.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than recoloured, because flags do not share a structure. The
    /// control is recycled by the list as it scrolls, so this runs for each row that
    /// comes into view - roughly a dozen shapes, which is well inside a frame.
    /// </remarks>
    private void Rebuild()
    {
        Surface.Children.Clear();

        string? spec = FlagData.For(Country);
        if (spec is null)
        {
            return;
        }

        foreach (string layer in spec.Split(';'))
        {
            try
            {
                Shape? shape = Build(layer);
                if (shape is not null)
                {
                    Surface.Children.Add(shape);
                }
            }
            catch (Exception error)
            {
                // One malformed layer should cost that layer, not the whole flag and
                // certainly not the dialog that is trying to open.
                AppLog.Error($"A layer of the '{Country}' flag could not be drawn: {layer}", error);
            }
        }
    }

    private static Shape? Build(string layer)
    {
        // "R x y w h RRGGBB" / "E cx cy rx ry RRGGBB" / "P pathData RRGGBB"
        int split = layer.IndexOf(' ');
        if (split < 0)
        {
            return null;
        }

        char kind = layer[0];
        string rest = layer[(split + 1)..];

        int lastSpace = rest.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            return null;
        }

        string body = rest[..lastSpace];
        SolidColorBrush fill = new(Parse(rest[(lastSpace + 1)..]));

        switch (kind)
        {
            case 'R':
            {
                double[] n = Numbers(body, 4);
                Rectangle rectangle = new() { Width = n[2], Height = n[3], Fill = fill };
                Canvas.SetLeft(rectangle, n[0]);
                Canvas.SetTop(rectangle, n[1]);
                return rectangle;
            }

            case 'E':
            {
                double[] n = Numbers(body, 4);
                Ellipse ellipse = new() { Width = n[2] * 2, Height = n[3] * 2, Fill = fill };
                Canvas.SetLeft(ellipse, n[0] - n[2]);
                Canvas.SetTop(ellipse, n[1] - n[3]);
                return ellipse;
            }

            case 'P':
                return new Path
                {
                    // Parsed per shape. A Geometry cannot be attached to two Paths at
                    // once, so one cached per flag would work until two rows showed
                    // the same country and then throw.
                    Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), body),
                    Fill = fill,
                };

            default:
                return null;
        }
    }

    private static double[] Numbers(string body, int count)
    {
        string[] parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != count)
        {
            throw new FormatException($"Expected {count} numbers, found {parts.Length}.");
        }

        double[] values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static Color Parse(string hex) => Color.FromArgb(
        255,
        byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
}
