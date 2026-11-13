using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The create-password screen. See the XAML for its layout; this wires the
/// fields' words into the view model and submits on Enter as on click.
/// </summary>
public sealed partial class CreatePasswordPage : Page
{
    public CreatePasswordPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<CreatePasswordViewModel>();

        InitializeComponent();

        PasswordField.PasswordChanged += (_, _) => ViewModel.SetPassword(PasswordField.Password);
        ConfirmField.PasswordChanged += (_, _) => ViewModel.SetConfirm(ConfirmField.Password);
        PasswordField.Submitted += OnSubmitted;
        ConfirmField.Submitted += OnSubmitted;

        Loaded += OnLoaded;
    }

    /// <summary>The screen's mind.</summary>
    public CreatePasswordViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        PasswordField.FocusInput();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();

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
