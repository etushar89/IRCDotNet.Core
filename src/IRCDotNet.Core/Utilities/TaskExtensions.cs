using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Utilities;

/// <summary>
/// Extension methods for safer fire-and-forget task execution with error logging.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Observes a task for exceptions without awaiting it. Logs any unhandled exceptions via <paramref name="logger"/>.
    /// </summary>
    /// <param name="task">The task to observe for faults.</param>
    /// <param name="logger">Optional logger for reporting unhandled exceptions.</param>
    /// <param name="operationName">Label for the operation, included in error log messages.</param>
    public static void SafeFireAndForget(this Task task, ILogger? logger = null, string? operationName = null)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                var opName = operationName ?? "Unknown";
                logger?.LogError(t.Exception, "Unhandled exception in fire-and-forget task: {OperationName}", opName);

                // Optionally, you could add telemetry, metrics, or other error reporting here
            }
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Runs a task factory on the thread pool and observes it for exceptions without awaiting.
    /// </summary>
    /// <param name="taskFactory">Factory that creates the async operation to run.</param>
    /// <param name="logger">Optional logger for reporting unhandled exceptions.</param>
    /// <param name="operationName">Label for the operation, included in error log messages.</param>
    public static void SafeFireAndForget(Func<Task> taskFactory, ILogger? logger = null, string? operationName = null)
    {
        Task.Run(taskFactory).SafeFireAndForget(logger, operationName);
    }
}
