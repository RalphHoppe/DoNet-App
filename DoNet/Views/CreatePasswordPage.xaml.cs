using System;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// Onboarding step one: choose the master password that will protect the vault.
/// </summary>
public sealed partial class CreatePasswordPage : Page
{
    public CreatePasswordPage()
    {
        // Assigned before InitializeComponent on purpose: x:Bind resolves its root
        // during InitializeComponent, and the page does not raise PropertyChanged for
        // ViewModel, so a later assignment would leave every binding stuck on null.
        ViewModel = App.Current.Services.GetRequiredService<CreatePasswordViewModel>();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Bound by x:Bind, so it has to be a public member on the page.</summary>
    public CreatePasswordViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            intro.Begin();
        }

        PasswordInput.Focus(FocusState.Programmatic);
    }

    /// <summary>Enter in either field is the same as clicking the button.</summary>
    private void OnFieldSubmitted(object sender, EventArgs args)
    {
        if (ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
        }
    }
}
