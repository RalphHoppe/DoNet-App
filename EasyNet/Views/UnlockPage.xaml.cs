using System.ComponentModel;
using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The unlock screen. See the XAML for its layout; this wires the field's
/// word into the view model, clears it when a try is refused, and owns the
/// composition's one responsive switch.
/// </summary>
public sealed partial class UnlockPage : Page
{
    // The handoff's wrap point: 26 + 212 + 28 + 296 + 26. This wide or
    // wider, the form stands beside the logo; narrower, the logo stacks
    // above it and the form takes the whole width.
    private const double SideBySideWidth = 588;

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
