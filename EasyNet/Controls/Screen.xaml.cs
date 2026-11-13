using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace EasyNet.Controls;

/// <summary>
/// The design's screen scaffold. See the XAML for how it is drawn.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed partial class Screen : UserControl
{
    /// <summary>Identifies the <see cref="Content"/> property.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(UIElement), typeof(Screen), new PropertyMetadata(null));

    public Screen()
    {
        InitializeComponent();
        Scroll.SizeChanged += OnSomethingResized;
        Host.SizeChanged += OnSomethingResized;
    }

    /// <summary>The screen's composition - the pages supply their own.</summary>
    public UIElement Content
    {
        get => (UIElement)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private void OnSomethingResized(object sender, SizeChangedEventArgs e)
    {
        // Centring content that is taller than the viewport would push its
        // top beyond the scroller's reach, so the moment it does not fit,
        // the box settles to the top and lets the scroll do the rest. The
        // same guard horizontally, though the window's minimum already
        // keeps the content inside.
        bool fitsVertically = Host.ActualHeight <= Scroll.ViewportHeight + 0.5;
        bool fitsHorizontally = Host.ActualWidth <= Scroll.ViewportWidth + 0.5;

        VerticalAlignment vertical = fitsVertically ? VerticalAlignment.Center : VerticalAlignment.Top;
        HorizontalAlignment horizontal = fitsHorizontally ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        if (Scroll.VerticalContentAlignment != vertical)
        {
            Scroll.VerticalContentAlignment = vertical;
        }

        if (Scroll.HorizontalContentAlignment != horizontal)
        {
            Scroll.HorizontalContentAlignment = horizontal;
        }
    }
}
