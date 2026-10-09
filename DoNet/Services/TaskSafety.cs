using System;
using System.Threading;
using System.Threading.Tasks;

namespace DoNet.Services;

/// <summary>
/// Helpers for work that is deliberately not awaited.
/// </summary>
public static class TaskSafety
{
    /// <summary>
    /// Marks a fire-and-forget task's failure as seen, and writes it to the log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>_ = SomethingAsync()</c> is not free. If the task faults, nothing observes the
    /// exception, so the only trace of it is an <c>UnobservedTaskException</c> raised
    /// whenever the garbage collector gets round to the task - possibly minutes later,
    /// possibly at shutdown, with no connection left to what caused it.
    /// </para>
    /// <para>
    /// This attaches a continuation that runs only on failure, which both observes the
    /// exception and records it against a name the caller chose. Cancellation is not a
    /// failure and is ignored.
    /// </para>
    /// </remarks>
    /// <param name="task">The task to watch.</param>
    /// <param name="context">What the task was doing, for the log entry.</param>
    public static void Observe(this Task task, string context)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.ContinueWith(
            finished =>
            {
                AggregateException? failure = finished.Exception;

                if (failure is null)
                {
                    return;
                }

                // Unwrap: a single inner exception is the normal case and reads far
                // better in the log than the AggregateException wrapper.
                Exception error = failure.InnerExceptions.Count == 1
                    ? failure.InnerExceptions[0]
                    : failure;

                if (error is OperationCanceledException)
                {
                    return;
                }

                AppLog.Error(context, error);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
