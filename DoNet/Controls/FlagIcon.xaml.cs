using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DoNet.Controls;

/// <summary>
/// Shows a country's flag, named by country.
/// </summary>
/// <remarks>
/// <para>
/// The artwork is a PNG in <c>Assets/Flags</c>, resolved from the country's name
/// through <see cref="FlagData"/>. The list recycles these controls as it scrolls,
/// and the brush is handed a fresh <see cref="BitmapImage"/> on every change for
/// that reason: a bitmap decodes asynchronously, and reusing one would leave the
/// previous country's flag on the row until the new file had decoded.
///</para>
/// <para>
/// An unknown country draws nothing rather than a placeholder. The picker puts the
/// country's name beside the flag, so a blank space costs the reader nothing, while
/// a wrong flag would cost them trust in all the others.
///</para>
/// </remarks>
public sealed partial class FlagIcon : UserControl
{
    /// <summary>Identifies the <see cref="Country"/> property.</summary>
    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(
        nameof(Country), typeof(string), typeof(FlagIcon),
        new PropertyMetadata(null, OnCountryChanged));

    /// <summary>Creates the control.</summary>
    public FlagIcon() => InitializeComponent();

    /// <summary>The country whose flag to show, spelled as in the catalog.</summary>
    public string? Country
    {
        get => (string?)GetValue(CountryProperty);
        set => SetValue(CountryProperty, value);
    }

    private static void OnCountryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((FlagIcon)sender).Rebuild();

    /// <summary>
    /// Points the brush at the current country's file, or empties the control when
    /// there is no such flag.
    /// </summary>
    private void Rebuild()
    {
        string? code = FlagData.For(Country);

        Surface.Visibility = code is null ? Visibility.Collapsed : Visibility.Visible;
        Edge.Visibility = Surface.Visibility;

        // Null rather than a blank placeholder image: an empty brush draws nothing,
        // which is the whole of the contract for an unknown country.
        FlagBrush.ImageSource = code is null
            ? null
            : new BitmapImage(new Uri($"ms-appx:///Assets/Flags/{code}.png"));
    }
}
