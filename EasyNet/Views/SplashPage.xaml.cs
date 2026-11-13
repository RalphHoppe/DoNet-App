using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The splash screen: the brand's moment, then the way in. See the XAML for
/// what is on it; this only starts the intro and hands focus to nothing,
/// because there is nothing to focus.
/// </summary>
public sealed partial class SplashPage : Page
{
    public SplashPage()
    {
        // Resolved before InitializeComponent, as everywhere in this app: the
        // generated bindings reach the view model while the tree is building.
        ViewModel = App.Current.Services.GetRequiredService<SplashViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    /// <summary>The screen's mind.</summary>
    public SplashViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        _ = ViewModel.BeginIntroCommand.ExecuteAsync(null);
    }

    /// <summary>No back, no forward: the splash is a door, not a room.</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();
}
