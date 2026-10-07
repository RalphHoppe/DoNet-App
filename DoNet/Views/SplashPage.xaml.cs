using System;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// Branded splash: the DoNet lockup draws itself on, holds, un-draws, and the app moves
/// on to onboarding.
/// </summary>
public sealed partial class SplashPage : Page
{
    /// <summary>Beat between the wordmark finishing and starting to rewind.</summary>
    private const int HoldMs = 420;

    /// <summary>
    /// Hard ceiling on the whole splash. Whatever happens to the animation, the user
    /// reaches onboarding - a splash that never advances is the worst thing this screen
    /// could do.
    /// </summary>
    private const int WatchdogMs = 6000;

    private readonly INavigationService _navigation;
    private readonly IVaultService _vault;
    private bool _started;

    public SplashPage()
    {
        InitializeComponent();

        _navigation = App.Current.Services.GetRequiredService<INavigationService>();
        _vault = App.Current.Services.GetRequiredService<IVaultService>();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Loaded can fire again if the page is ever re-parented; the splash plays once.
        if (_started)
        {
            return;
        }

        _started = true;

        // The splash is two and a half seconds of animation during which the app has
        // nothing else to do. Spend it loading the SQLCipher native library and
        // building EF's object model - neither needs the key, neither touches the
        // database file, and both otherwise land on the first query after unlock.
        IdleWork.InBackground(
            "Preparing the data layer",
            () => App.Current.Services.GetRequiredService<IPersonDirectory>().PrepareAsync());

        try
        {
            await Task.WhenAny(PlaySequenceAsync(), Task.Delay(WatchdogMs));
        }
        catch (Exception error)
        {
            // An animation failure must never strand the user on a blank splash -
            // fall through and navigate anyway.
            AppLog.Error("The splash animation failed", error);
        }

        try
        {
            _navigation.NavigateTo(Destination(), clearBackStack: true);
        }
        catch (Exception error)
        {
            // Inside an async void handler, so this would otherwise end the process.
            AppLog.Error("Navigating away from the splash failed", error);
        }
    }

    /// <summary>
    /// First run goes to onboarding; every launch after that goes to the lock screen.
    /// Either way the back stack is cleared - there is nothing sensible to go back to.
    /// </summary>
    private Type Destination() =>
        _vault.IsInitialized ? typeof(LockPage) : typeof(CreatePasswordPage);

    private async Task PlaySequenceAsync()
    {
        await Logo.PlayIntroAsync();
        await Task.Delay(HoldMs);
        await Logo.PlayOutroAsync();
    }
}
