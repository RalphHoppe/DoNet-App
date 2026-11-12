using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The create-password screen. See the XAML for its layout; this wires the
/// fields' words into the view model, submits on Enter as on click, and
/// keeps the width states honest as the window resizes.
/// </summary>
public sealed partial class CreatePasswordPage : Page
{
    // The wide layout's threshold: below it, the lockup steps aside. 1160 is
    // where the form's own 840 no longer clears the lockup's 352 with margin
    // to breathe (1160 - 352 - 840 < 0 begins to squeeze at the tightest).
    private const double WideStateMinimumWidth = 1160;

    public CreatePasswordPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<CreatePasswordViewModel>();

        InitializeComponent();

        PasswordField.PasswordChanged += (_, _) => ViewModel.SetPassword(PasswordField.Password);
        ConfirmField.PasswordChanged += (_, _) => ViewModel.SetConfirm(ConfirmField.Password);
        PasswordField.Submitted += OnSubmitted;
        ConfirmField.Submitted += OnSubmitted;

        SizeChanged += (_, _) => ApplyWidthState();
        Loaded += OnLoaded;
    }

    /// <summary>The screen's mind.</summary>
    public CreatePasswordViewModel ViewModel { get; }

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

    private void OnSubmitted(object? sender, System.EventArgs args) => TryCreate();

    private void OnCreateClick(object sender, RoutedEventArgs args) => TryCreate();

    private void TryCreate()
    {
        if (ViewModel.CreateCommand.CanExecute(null))
        {
            _ = ViewModel.CreateCommand.ExecuteAsync(null);
        }
    }
}
