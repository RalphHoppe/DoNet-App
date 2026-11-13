using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The create-password screen. See the XAML for its layout; this wires the
/// fields' words into the view model, submits on Enter as on click, and
/// owns the composition's one responsive switch.
/// </summary>
public sealed partial class CreatePasswordPage : Page
{
    // The handoff's wrap point: 26 + 212 + 28 + 296 + 26. This wide or
    // wider, the form stands beside the logo; narrower, the logo stacks
    // above it and the form takes the whole width.
    private const double SideBySideWidth = 588;

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

    /// <summary>
    /// The composition's one switch: the form beside the logo or stacked
    /// below it, decided by the page's own width in the design's pixels.
    /// </summary>
    /// <remarks>
    /// An AdaptiveTrigger in the XAML owned this first and never fired -
    /// a window eight hundred wide shipped the stacked base state, the
    /// form squeezed into the logo's 212 column and the layout's floors
    /// overflowing it. This cannot fail quietly: the size is measured on
    /// the same element the design's artboard describes.
    /// </remarks>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool beside = e.NewSize.Width >= SideBySideWidth;

        Grid.SetColumn(Form, beside ? 2 : 0);
        Grid.SetRow(Form, beside ? 0 : 2);
        Grid.SetColumnSpan(Form, beside ? 1 : 3);
    }

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
