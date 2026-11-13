using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace EasyNet.Controls;

/// <summary>
/// The design's one shadow recipe, measured from its SVG: every floating
/// piece casts its own shape shrunk by 8 px, moved 10 px down, blurred
/// softly, in plain black at 7.84% - a whisper of depth, not a statement.
/// </summary>
/// <remarks>
/// The shrunken shape lives as a small white-on-transparent PNG under
/// Assets/Shadows (one per piece geometry). Set the attached property on a
/// transparent layer element placed behind the piece, and the layer grows
/// a composition drop shadow shaped by that mask. The composition is fixed
/// size - the design never scales - so the mask's aspect always matches.
/// </remarks>
public sealed class PieceShadow
{
    /// <summary>Identifies the Mask attached property.</summary>
    public static readonly DependencyProperty MaskProperty = DependencyProperty.RegisterAttached(
        "Mask", typeof(string), typeof(PieceShadow),
        new PropertyMetadata(null, OnMaskChanged));

    /// <summary>The shadow's shape: an ms-appx URI to an eroded-shape PNG.</summary>
    public static string GetMask(FrameworkElement element) => (string)element.GetValue(MaskProperty);

    public static void SetMask(FrameworkElement element, string value) => element.SetValue(MaskProperty, value);

    // The measured recipe, in the design's own pixels.
    private const double Erode = 8;       // the shape shrinks 8 px before it is cast
    private const double OffsetY = 10;    // and falls 10 px down
    private const double Blur = 30;       // resvg's 12-sigma gaussian, in blur radius
    private const float ShadowOpacity = 0.0784f; // black at 7.84%

    private static void OnMaskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)d;
        if (element.IsLoaded)
        {
            Attach(element);
        }
        else
        {
            element.Loaded -= OnElementLoaded;
            element.Loaded += OnElementLoaded;
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnElementLoaded;
        Attach(element);
    }

    private static void Attach(FrameworkElement element)
    {
        string? mask = GetMask(element);
        if (string.IsNullOrEmpty(mask))
        {
            return;
        }

        double width = element.ActualWidth;
        double height = element.ActualHeight;
        if (width <= Erode * 2 || height <= Erode * 2)
        {
            return; // not measured, or too small to hold the shadow
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;

        var shadow = compositor.CreateDropShadow();
        shadow.Color = Microsoft.UI.Colors.Black;
        shadow.Opacity = ShadowOpacity;
        shadow.Offset = new Vector3(0, (float)OffsetY, 0);
        shadow.BlurRadius = (float)Blur;

        // The mask carries the eroded shape, so the shadow hugs inside the
        // piece's own edge instead of haloing around it.
        LoadedImageSurface surface = LoadedImageSurface.StartLoadFromUri(new Uri(mask));
        var maskBrush = compositor.CreateSurfaceBrush(surface);
        maskBrush.Stretch = CompositionStretch.Fill;
        shadow.Mask = maskBrush;

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new Vector2((float)(width - Erode * 2), (float)(height - Erode * 2));
        sprite.Offset = new Vector3((float)Erode, (float)(Erode + OffsetY), 0);
        sprite.Shadow = shadow;

        ElementCompositionPreview.SetElementChildVisual(element, sprite);
    }
}
