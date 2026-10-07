using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace DoNet.Views;

/// <summary>
/// The home screen: a navigation rail on the left and, for now, an empty content
/// surface on the right.
/// </summary>
public sealed partial class HomePage : Page
{
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
        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            intro.Begin();
        }
    }

    /// <summary>
    /// Drops the compiled bindings when the home screen is navigated away from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// x:Bind subscribes straight to the source's PropertyChanged and never
    /// unsubscribes - the generated code has no Unloaded handling at all. That is
    /// harmless when the source dies with the view, and a leak when it does not. Here
    /// it does not: PersonsViewModel is a singleton, so every binding on this page and
    /// on the two controls below it is a reference from an object that lives forever to
    /// one that should not. Without this, each lock and unlock would strand a whole page
    /// tree - the rail, the grid, the cards and both modals - in memory for the rest of
    /// the session.
    /// </para>
    /// <para>
    /// Safe because the home screen is never navigated back to. Its only exit is the
    /// lock button, which clears the back stack, and unlocking builds a fresh instance
    /// by way of the welcome screen. If a route back is ever added, this has to become
    /// conditional - bindings do not restart once stopped.
    /// </para>
    /// </remarks>
    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        base.OnNavigatedFrom(args);

        DirectoryView.ReleaseBindings();
        PersonModal.ReleaseBindings();
        Bindings.StopTracking();
    }
}
