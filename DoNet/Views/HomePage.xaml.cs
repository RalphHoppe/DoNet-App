using DoNet.Controls;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The home screen: a navigation rail on the left and, for now, an empty content
/// surface on the right.
/// </summary>
public sealed partial class HomePage : Page
{
    /// <summary>The design's panel shadow: X 0, Y 6, blur 14, 041516 at 7.06%.</summary>
    private const double ShadowBlur = 14;
    private const double ShadowOffsetY = 6;

    public HomePage()
    {
        // Before InitializeComponent, not after: x:Bind resolves its root object while
        // the generated code runs, so a view model assigned afterwards would leave every
        // binding on this page pointing at null.
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public HomeViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Composition needs the elements measured and live, so this waits for Loaded
        // rather than running in the constructor.
        var shadow = Color.FromArgb(0x12, 0x04, 0x15, 0x16);
        Elevation.Apply(RailShadowHost, RailPlate, ShadowBlur, ShadowOffsetY, shadow);
        Elevation.Apply(ContentShadowHost, ContentPlate, ShadowBlur, ShadowOffsetY, shadow);

        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            intro.Begin();
        }
    }
}
