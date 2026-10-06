using System;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The lock screen: shown on every launch once a password has been set.
/// </summary>
public sealed partial class LockPage : Page
{
    public LockPage()
    {
        // Before InitializeComponent, not after: x:Bind resolves its root object while
        // the generated code runs, so a view model assigned afterwards would leave every
        // binding on this page pointing at null.
        ViewModel = App.Current.Services.GetRequiredService<LockViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public LockViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            intro.Begin();
        }

        // The only thing anyone comes here to do.
        PasswordInput.Focus(FocusState.Programmatic);
    }

    private void OnFieldSubmitted(object sender, EventArgs args)
    {
        if (ViewModel.UnlockCommand.CanExecute(null))
        {
            ViewModel.UnlockCommand.Execute(null);
        }
    }
}
