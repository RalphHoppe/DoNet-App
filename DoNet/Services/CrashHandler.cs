using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DoNet.Services;

/// <summary>
/// Catches what nothing else caught, writes it down, and decides whether the app can
/// carry on.
/// </summary>
/// <remarks>
/// <para>
/// Three handlers are needed rather than one, because in WinUI 3 they cover different
/// ground:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>Application.UnhandledException</c> sees failures on the UI thread, and is the
/// only one of the three that can stop the process from dying.
/// </description></item>
/// <item><description>
/// <c>AppDomain.UnhandledException</c> is where background-thread failures surface in
/// WinUI 3 - they do <i>not</i> reach the Application event. By the time it fires the
/// process is already going down, so all that is left is to write the reason out.
/// </description></item>
/// <item><description>
/// <c>TaskScheduler.UnobservedTaskException</c> catches faulted tasks nobody awaited.
/// It fires when the task is collected, which can be long after the fault, so it is a
/// diagnostic net rather than a recovery point.
/// </description></item>
/// </list>
/// <para>
/// There is also a documented WinUI quirk worked around here: the exception handed to
/// <c>Application.UnhandledException</c> often arrives with its stack trace stripped,
/// which makes it nearly useless in a log. Recording the most recent first-chance
/// exception and substituting it when the stack is missing restores the detail. The
/// cost is a handler on every throw in the process, which is acceptable in an app that
/// throws rarely and never in a loop - and it only stores a reference, it does not log.
/// </para>
/// </remarks>
public static class CrashHandler
{
    /// <summary>
    /// How many unhandled UI failures inside <see cref="BreakerWindow"/> before the app
    /// stops trying to survive them.
    /// </summary>
    /// <remarks>
    /// Marking everything handled sounds safer than it is. An exception thrown from
    /// layout or a binding repeats on every frame, and a handler that always swallows
    /// turns that into an unkillable app burning a core. After a few failures in quick
    /// succession the honest outcome is to stop, so the user gets a crash they can
    /// report instead of a machine that has quietly locked up.
    /// </remarks>
    private const int BreakerLimit = 5;

    private static readonly TimeSpan BreakerWindow = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static readonly Queue<DateTimeOffset> Recent = new();

    private static Exception? _lastFirstChance;
    private static bool _installed;

    /// <summary>
    /// Installs the handlers. Call once, as early as possible - anything that throws
    /// before this runs is invisible.
    /// </summary>
    public static void Install(Microsoft.UI.Xaml.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (_installed)
        {
            return;
        }

        _installed = true;

        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            // Store only. Logging here would record every handled exception in the
            // process, including the deliberate one a wrong password produces.
            _lastFirstChance = args.Exception;
        };

        application.UnhandledException += (_, args) =>
        {
            Exception? error = args.Exception;

            // WinUI hands over an exception with no stack in many cases; the
            // first-chance copy of the same failure still has one.
            if (error?.StackTrace is null && _lastFirstChance is not null)
            {
                error = _lastFirstChance;
            }

            AppLog.Error("Unhandled exception on the UI thread", error);

            if (ShouldKeepRunning())
            {
                // Survive it. The failure is on record, and a password manager that
                // disappears mid-session is worse than one with a glitch in it.
                args.Handled = true;
            }
            else
            {
                AppLog.Error(
                    "Too many unhandled exceptions in a short window - letting the process end",
                    null);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Terminal. Write it down before the process goes.
            AppLog.Error(
                $"Unhandled exception on a background thread (terminating: {args.IsTerminating})",
                args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Faulted task that nobody awaited", args.Exception);

            // Without this the exception escalates. It is already logged, and a
            // forgotten background task is not a reason to kill the app.
            args.SetObserved();
        };

        AppLog.Info(
            $"--- DoNet started --- version {typeof(CrashHandler).Assembly.GetName().Version}, "
            + $".NET {Environment.Version}, {Environment.OSVersion.VersionString}");
    }

    /// <summary>
    /// Rolling-window circuit breaker. True while failures are still occasional.
    /// </summary>
    private static bool ShouldKeepRunning()
    {
        lock (Gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            while (Recent.Count > 0 && now - Recent.Peek() > BreakerWindow)
            {
                Recent.Dequeue();
            }

            Recent.Enqueue(now);

            return Recent.Count <= BreakerLimit;
        }
    }
}
