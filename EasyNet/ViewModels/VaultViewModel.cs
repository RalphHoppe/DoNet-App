using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyNet.Services;
using EasyNet.Views;

namespace EasyNet.ViewModels;

/// <summary>
/// The vault screen's mind. For now it holds the one honest action a
/// placeholder can: locking again, which walks back to the unlock screen and
/// proves the whole door works in both directions.
/// </summary>
public sealed partial class VaultViewModel : ObservableObject
{
    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;

    public VaultViewModel(IVaultService vault, INavigationService navigation)
    {
        _vault = vault;
        _navigation = navigation;
    }

    /// <summary>Locks the vault and asks for the password again.</summary>
    [RelayCommand]
    private void Lock()
    {
        _vault.Lock();
        _navigation.NavigateTo(typeof(UnlockPage));
    }
}
