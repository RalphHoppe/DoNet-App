using System;
using DoNet.Contracts;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace DoNet;

/// <summary>
/// Application entry point and composition root.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>
    /// The app's service provider.
    /// </summary>
    /// <remarks>
    /// WinUI constructs pages itself via <c>Frame.Navigate</c>, which needs a parameterless
    /// constructor, so pages pull their view model from here instead of receiving it through
    /// constructor injection.
    /// </remarks>
    public new static App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    public Window? MainWindowInstance => _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IVaultService, VaultService>();

        services.AddTransient<CreatePasswordViewModel>();
        services.AddTransient<LockViewModel>();
        services.AddTransient<ForgotPasswordViewModel>();

        return services.BuildServiceProvider();
    }
}
