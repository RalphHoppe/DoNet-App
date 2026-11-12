using System.ComponentModel;
using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The unlock screen. See the XAML for its layout; this wires the field's
/// word into the view model, clears it when a try is refused, and keeps the
/// width states honest as the window resizes.
/// </summary>
public sealed partial class UnlockPage : Page
{
    // The create screen's own threshold: the two doors are the same width or
    // the app feels askew walking between them.
    private const double WideStateMinimumWidth = 1160;

    public UnlockPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<UnlockViewModel>();

        InitializeComponent();

        PasswordField.PasswordChanged += (_, _) => ViewModel.SetPassword(PasswordField.Password);
        PasswordField.Submitted += OnSubmitted;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        SizeChanged += (_, _) => ApplyWidthState();
        Loaded += OnLoaded;
    }

    /// <summary>The screen's mind.</summary>
    public UnlockViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        ApplyWidthState();
        PasswordField.FocusInput();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();

    private void ApplyWidthState() =>
        _ = VisualStateManager.GoToState(
            this,
            ActualWidth >= WideStateMinimumWidth ? "WideState" : "NarrowState",
            useTransitions: false);

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
