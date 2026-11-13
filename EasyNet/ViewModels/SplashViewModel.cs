using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyNet.Services;
using EasyNet.Views;

namespace EasyNet.ViewModels;

/// <summary>
/// The splash screen's mind: let the entrance play, then go where the vault's
/// state says - the password's creation the first time, its lock every time
/// after.
/// </summary>
public sealed partial class SplashViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IVaultService _vault;

    public SplashViewModel(INavigationService navigation, IVaultService vault)
    {
        _navigation = navigation;
        _vault = vault;
    }

    /// <summary>
    /// Plays the brand's moment, then routes. The mark's entrance is 2.3
    /// seconds and the loop's first pulse lands a beat later; leaving at 3.4
    /// lets one whole breath of the living logo show without making anyone
    /// wait for it.
    /// </summary>
    [RelayCommand]
    private async Task BeginIntroAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3.4));

        _navigation.NavigateTo(_vault.IsCreated ? typeof(UnlockPage) : typeof(CreatePasswordPage));
    }
}
