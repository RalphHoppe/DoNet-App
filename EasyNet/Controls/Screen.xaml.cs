using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace EasyNet.Controls;

/// <summary>
/// The design's screen scaffold. See the XAML for how it is drawn.
/// </summary>
/// <remarks>
/// The centre-when-it-fits / scroll-when-it-doesn't behaviour is entirely
/// structural - a fitter that never reports less than the viewport - so
/// there is nothing here but the content it hosts.
/// </remarks>
[ContentProperty(Name = nameof(Content))]
public sealed partial class Screen : UserControl
{
    /// <summary>Identifies the <see cref="Content"/> property.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(UIElement), typeof(Screen), new PropertyMetadata(null));

    public Screen()
    {
        InitializeComponent();
    }

    /// <summary>The screen's composition - the pages supply their own.</summary>
    public UIElement Content
    {
        get => (UIElement)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}
