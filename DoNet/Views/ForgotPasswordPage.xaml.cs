using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The forgot-password screen: an explanation that there is no recovery, and the option
/// to erase everything and start again.
/// </summary>
public sealed partial class ForgotPasswordPage : Page
{
    public ForgotPasswordPage()
    {
        // Before InitializeComponent, not after: x:Bind resolves its root object while
        // the generated code runs, so a view model assigned afterwards would leave every
        // binding on this page pointing at null.
        ViewModel = App.Current.Services.GetRequiredService<ForgotPasswordViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public ForgotPasswordViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            intro.Begin();
        }
    }
}
