using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The vault's home: the designer's sidebar panel - brand, categories,
/// compact view, lock - with the main area waiting on its own design. See
/// the XAML for what is on it.
/// </summary>
/// <remarks>
/// The design's sidebar is 850 tall at its natural size; the window opens
/// that tall here and returns to 450 on every other screen.
///</remarks>
public sealed partial class VaultPage : Page, IPreferredWindowSize
{
    public VaultPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<VaultViewModel>();

        InitializeComponent();
    }

    /// <summary>The screen's mind.</summary>
    public VaultViewModel ViewModel { get; }

    /// <inheritdoc/>
    public int PreferredWidth => 800;

    /// <inheritdoc/>
    public int PreferredHeight => 850;

    protected override void OnNavigatedTo(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();
}
