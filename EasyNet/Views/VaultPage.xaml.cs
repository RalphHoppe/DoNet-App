using EasyNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EasyNet.Views;

/// <summary>
/// The vault's open state, as a placeholder: one honest sentence and one
/// real action. See the XAML for what is on it.
/// </summary>
public sealed partial class VaultPage : Page
{
    public VaultPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<VaultViewModel>();

        InitializeComponent();
    }

    /// <summary>The screen's mind.</summary>
    public VaultViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs args) =>
        Frame?.BackStack.Clear();
}
