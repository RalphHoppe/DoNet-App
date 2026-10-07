using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace DoNet.Services;

/// <summary>
/// Schedules work for moments when the app is doing nothing anyway.
/// </summary>
/// <remarks>
/// <para>
/// An app feels fast when the expensive thing already happened before it was asked
/// for. DoNet has four windows where it is free to work and nobody is waiting on it:
/// the splash animation, the lock screen while a password is being typed, the welcome
/// animation, and the pause after the home screen settles. Each is a few seconds of a
/// person reading or watching, and every millisecond moved into one is a millisecond
/// off something the user is waiting for.
/// </para>
/// <para>
/// Two kinds of idleness, and the distinction matters:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="OnUiIdle"/> - work that must touch the visual tree. Queued at the lowest
/// dispatcher priority, so it runs only once typing, scrolling and animation have
/// nothing left to do.
/// </description></item>
/// <item><description>
/// <see cref="InBackground"/> - work that touches no UI. Runs on the thread pool at
/// below-normal priority, filling spare cores without competing with the thread that
/// draws.
/// </description></item>
/// </list>
/// <para>
/// Everything scheduled here is an optimisation, never a dependency. Each caller must
/// still be correct if the work never runs - a warm-up that becomes required is just
/// initialisation with extra steps and a race condition. Failures are logged and
/// swallowed for the same reason.
/// </para>
/// </remarks>
public static class IdleWork
{
    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread once the dispatcher has nothing
    /// more urgent to do.
    /// </summary>
    /// <param name="queue">The dispatcher to queue on. Null is ignored.</param>
    /// <param name="name">What the work is, for the log if it fails.</param>
    /// <param name="work">The work to run.</param>
    public static void OnUiIdle(DispatcherQueue? queue, string name, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                try
                {
                    work();
                }
                catch (Exception error)
                {
                    AppLog.Error($"Idle work '{name}' failed", error);
                }
            });
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread at below-normal priority.
    /// </summary>
    /// <remarks>
    /// The priority drop is the point. This is speculative work - warming a code path,
    /// building a model, loading a library - and the scheduler should hand the core
    /// back the moment the user does something real. Without it, a "helpful" warm-up
    /// competes with the animation it was supposed to hide behind.
    /// </remarks>
    public static void InBackground(string name, Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        Task.Run(
            async () =>
            {
                Thread current = Thread.CurrentThread;
                ThreadPriority original = current.Priority;

                try
                {
                    current.Priority = ThreadPriority.BelowNormal;
                    await work().ConfigureAwait(false);
                }
                finally
                {
                    // The thread goes back to the pool; leaving it demoted would
                    // quietly slow down whatever picks it up next.
                    try
                    {
                        current.Priority = original;
                    }
                    catch (Exception)
                    {
                        // Nothing useful to do if the thread is already gone.
                    }
                }
            })
            .Observe($"Background warm-up '{name}'");
    }
}
