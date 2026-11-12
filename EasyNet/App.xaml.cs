using System;
using EasyNet.Services;
using EasyNet.ViewModels;
using EasyNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EasyNet;

/// <summary>
/// The application's composition root and its single instance state.
/// </summary>
/// <remarks>
/// <para>
/// Everything the app can do is reachable from the service collection built
/// here: the vault, the navigation service, and one view model per screen.
/// Pages resolve their view models from <see cref="Services"/> before
/// <c>InitializeComponent</c> runs, the same way everywhere, so the pattern in
/// one screen reads the same in every other.
/// </para>
/// <para>
/// The app is drawn on the light theme deliberately. The design's own SVGs
/// carry dark-mode tokens for their ink and lines, which is where a dark
/// theme will begin when it is asked for - but the mockups are white, and a
/// half-translated dark theme is worse than an honest light one.
/// </para>
///</remarks>
public partial class App : Application
{
    /// <summary>
    /// Builds the container, then the window. The order is the whole trick:
    /// nothing downstream may construct before the services exist.
    /// </summary>
    public App()
    {
        RequestedTheme = ApplicationTheme.Light;

        InitializeComponent();

        Services = ConfigureServices();
    }

    /// <summary>The one instance, for pages reaching their view models.</summary>
    public static App Current => (App)Application.Current;

    /// <summary>Everything the app can do.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Opens the shell on the splash screen.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = new();
        window.Activate();
    }

    /// <summary>
    /// The app's one true composition: services that keep state are singletons,
    /// view models are resolved per navigation and hold nothing that outlives
    /// their screen.
    /// </summary>
    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddSingleton<AppPaths>()
            .AddSingleton<IVaultService, VaultService>()
            .AddSingleton<INavigationService, NavigationService>()
            .AddTransient<SplashViewModel>()
            .AddTransient<CreatePasswordViewModel>()
            .AddTransient<UnlockViewModel>()
            .AddTransient<VaultViewModel>()
            .BuildServiceProvider();
}
