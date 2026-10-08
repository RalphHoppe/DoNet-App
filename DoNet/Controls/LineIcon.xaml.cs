using System;
using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace DoNet.Controls;

/// <summary>
/// Draws one of the app's line icons, chosen by name.
/// </summary>
/// <remarks>
/// The geometry is parsed per instance rather than shared from a resource
/// dictionary. A <see cref="Geometry"/> cannot be attached to two
/// <see cref="Microsoft.UI.Xaml.Shapes.Path"/> elements at once, so a StaticResource
/// holding one would work until the second card appeared and then throw. Parsing
/// path markup costs microseconds, which is the cheaper side of that trade.
/// </remarks>
public sealed partial class LineIcon : UserControl
{
    /// <summary>Identifies the <see cref="Kind"/> property.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(LineIcon), new PropertyMetadata(null, OnKindChanged));

    /// <summary>Creates the control.</summary>
    public LineIcon()
    {
        InitializeComponent();

        // Foreground is an inherited property, so it can change without anyone
        // touching this instance - a parent setting it, or a theme change. Watching
        // the property catches all of those; reading it once in the constructor
        // would only catch the first.
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Shape.Stroke = Foreground);
        Shape.Stroke = Foreground;
    }

    /// <summary>
    /// Which icon to draw. One of the names in <see cref="LineIconData.Names"/>.
    /// </summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((LineIcon)sender).ApplyKind();

    private void ApplyKind()
    {
        string? kind = Kind;
        string? markup = LineIconData.For(kind);

        if (markup is null)
        {
            Shape.Data = null;
            if (!string.IsNullOrEmpty(kind))
            {
                AppLog.Warn($"No line icon is named '{kind}'. Nothing was drawn.");
            }

            return;
        }

        try
        {
            Shape.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), markup);
        }
        catch (Exception error)
        {
            // An icon is decoration. A generated path that will not parse is worth a
            // line in the log, not a window that fails to open.
            AppLog.Error($"The '{kind}' icon could not be parsed", error);
            Shape.Data = null;
        }
    }
}
