using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace DoNet.Controls;

/// <summary>
/// Attaches a real composition drop shadow, shaped to a rounded rectangle.
/// </summary>
/// <remarks>
/// <para>
/// A circle's shadow can be faked exactly with a radial gradient, which is what the
/// rail buttons do. A rounded rectangle cannot - the falloff differs along the edges
/// and around the corners - so the two panels use the compositor instead.
/// </para>
/// <para>
/// <c>ThemeShadow</c> is the obvious alternative and was tried first; it rendered
/// nothing at all here. This drives <see cref="DropShadow"/> directly, which is the
/// same machinery without the ambient-occlusion layer and the receiver bookkeeping.
/// </para>
/// <para>
/// The shadow needs its own host behind the shape, not the shape itself. A sprite set
/// as a child visual draws <i>above</i> its host's content, so hanging it on the shape
/// being elevated would lay a 7% wash over the panel's own fill. The caller passes an
/// empty element that sits earlier in the same Grid cell.
/// </para>
/// </remarks>
public static class Elevation
{
    /// <summary>
    /// Shapes a shadow to <paramref name="caster"/> and draws it in <paramref name="host"/>.
    /// </summary>
    /// <param name="host">An empty element behind the caster, in the same Grid cell.</param>
    /// <param name="caster">The shape whose silhouette the shadow takes.</param>
    /// <param name="blurRadius">The design's blur.</param>
    /// <param name="offsetY">The design's Y offset; X is always 0 on these panels.</param>
    /// <param name="color">Shadow colour including its alpha.</param>
    public static void Apply(
        FrameworkElement host,
        Shape caster,
        double blurRadius,
        double offsetY,
        Windows.UI.Color color)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(caster);

        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = (float)blurRadius;
            shadow.Offset = new Vector3(0f, (float)offsetY, 0f);
            shadow.Color = color;
            shadow.Mask = caster.GetAlphaMask();

            var sprite = compositor.CreateSpriteVisual();
            sprite.Shadow = shadow;
            sprite.Size = new Vector2((float)caster.ActualWidth, (float)caster.ActualHeight);

            ElementCompositionPreview.SetElementChildVisual(host, sprite);

            // The panels stretch with the window, and a sprite does not resize itself.
            caster.SizeChanged += (_, args) =>
                sprite.Size = new Vector2((float)args.NewSize.Width, (float)args.NewSize.Height);
        }
        catch (Exception)
        {
            // A missing shadow is a cosmetic loss; it must never take the window down.
            // The panels keep their hairline stroke, which is what separates them anyway.
        }
    }
}
