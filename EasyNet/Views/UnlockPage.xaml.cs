using System.ComponentModel;
using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The unlock screen. See the XAML for its layout; this wires the field's
/// word into the view model and clears it when a try is refused.
/// </summary>
public sealed partial class UnlockPage : Page
{
    public UnlockPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<UnlockViewModel>();

        InitializeComponent();

        PasswordField.PasswordChanged += (_, _) => ViewModel.SetPassword(PasswordField.Password);
        PasswordField.Submitted += OnSubmitted;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
    }

    /// <summary>The screen's mind.</summary>
    public UnlockViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        PasswordField.FocusInput();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();

    /// <summary>
    /// The verdict's consequences on the field itself: a refused try empties
    /// the word and hands the caret back, ready for the next one.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UnlockViewModel.HasError))
        {
            PasswordField.SetValidity(!ViewModel.HasError);

            if (ViewModel.HasError)
            {
                PasswordField.Clear();
                PasswordField.FocusInput();
            }
        }
    }

    private void OnSubmitted(object? sender, System.EventArgs args) => TryUnlock();

    private void OnUnlockClick(object sender, RoutedEventArgs args) => TryUnlock();

    private void TryUnlock()
    {
        if (ViewModel.UnlockCommand.CanExecute(null))
        {
            _ = ViewModel.UnlockCommand.ExecuteAsync(null);
        }
    }
}
