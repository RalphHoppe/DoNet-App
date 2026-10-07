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
        // First statement in the process that we control. Anything that throws before
        // this is invisible, so nothing goes above it.
        CrashHandler.Install(this);

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

        // WinUI desktop has no application-level exit event, so the main window closing
        // is the shutdown hook. Disposing the provider disposes the singletons that hold
        // unmanaged resources - in particular it closes the encrypted database, which
        // lets SQLite check the write-ahead log back into the main file instead of
        // leaving a -wal beside it for the next launch to recover.
        _window.Closed += OnMainWindowClosed;

        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            (Services as IDisposable)?.Dispose();
            AppLog.Info("--- DoNet closed ---");
        }
        catch (Exception error)
        {
            // Shutdown is not a place to throw; the window is already going.
            AppLog.Error("Shutdown failed", error);
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IVaultService, VaultService>();

        // The person store. Real add, edit and delete; what it does not yet do is
        // survive a restart. Swapping this one registration for EF Core over SQLCipher
        // is the whole of that change - nothing above this line needs to move.
        services.AddSingleton<IPersonDirectory, PersonDirectoryService>();

        services.AddTransient<CreatePasswordViewModel>();
        services.AddTransient<LockViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ForgotPasswordViewModel>();
        // Singleton: the directory grid and the two modals hosted at the HomePage root
        // are three views onto one screen and must share its state.
        services.AddSingleton<PersonsViewModel>();

        return services.BuildServiceProvider();
    }
}
