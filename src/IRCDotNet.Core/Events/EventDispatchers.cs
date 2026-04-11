using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Events;

/// <summary>
/// Interface for event dispatching strategies.
/// </summary>
public interface IEventDispatcher : IDisposable
{
    /// <summary>
    /// Registers an async event handler for the specified event type.
    /// </summary>
    /// <typeparam name="T">The event type to handle.</typeparam>
    /// <param name="handler">The async handler delegate.</param>
    void AddHandler<T>(Func<T, Task> handler) where T : IrcEvent;

    /// <summary>
    /// Removes a previously registered event handler.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The handler delegate to remove.</param>
    void RemoveHandler<T>(Func<T, Task> handler) where T : IrcEvent;

    /// <summary>
    /// Dispatches an event to all registered handlers of the matching type.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="eventArgs">The event to dispatch.</param>
    /// <param name="cancellationToken">Token to cancel the dispatch.</param>
    Task DispatchAsync<T>(T eventArgs, CancellationToken cancellationToken = default) where T : IrcEvent;

    /// <summary>
    /// Gets the total number of registered handlers across all event types.
    /// </summary>
    int HandlerCount { get; }
}

/// <summary>
/// Threaded event dispatcher — each handler runs in its own <see cref="Task"/>.
/// </summary>
public class ThreadedEventDispatcher : IEventDispatcher
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<Delegate>> _handlers = new();
    private readonly ILogger<ThreadedEventDispatcher>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="ThreadedEventDispatcher"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public ThreadedEventDispatcher(ILogger<ThreadedEventDispatcher>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public int HandlerCount => _handlers.Values.Sum(bag => bag.Count);

    /// <inheritdoc />
    public void AddHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        if (_disposed) return;

        var handlers = _handlers.GetOrAdd(typeof(T), _ => new ConcurrentBag<Delegate>());
        handlers.Add(handler);

        _logger?.LogDebug("Added handler for event type {EventType}. Total handlers: {HandlerCount}",
            typeof(T).Name, HandlerCount);
    }

    /// <inheritdoc />
    public void RemoveHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        if (_disposed) return;

        if (!_handlers.TryGetValue(typeof(T), out var handlers)) return;

        // ConcurrentBag doesn't support removal, so we'll recreate without the handler
        var newHandlers = new ConcurrentBag<Delegate>();
        foreach (var existingHandler in handlers)
        {
            if (!ReferenceEquals(existingHandler, handler))
                newHandlers.Add(existingHandler);
        }

        _handlers.TryUpdate(typeof(T), newHandlers, handlers);

        _logger?.LogDebug("Removed handler for event type {EventType}. Total handlers: {HandlerCount}",
            typeof(T).Name, HandlerCount);
    }

    /// <inheritdoc />
    public async Task DispatchAsync<T>(T eventArgs, CancellationToken cancellationToken = default) where T : IrcEvent
    {
        if (_disposed) return;

        if (!_handlers.TryGetValue(typeof(T), out var handlers)) return;

        var tasks = new List<Task>();

        foreach (var handler in handlers)
        {
            if (handler is Func<T, Task> typedHandler)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await typedHandler(eventArgs).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error in event handler for {EventType}", typeof(T).Name);
                    }
                }, cancellationToken));
            }
        }

        if (tasks.Count > 0)
        {
            _logger?.LogTrace("Dispatching event {EventType} to {HandlerCount} handlers",
                typeof(T).Name, tasks.Count);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handlers.Clear();
    }
}

/// <summary>
/// Sequential event dispatcher — handlers run one after another in order.
/// </summary>
public class SequentialEventDispatcher : IEventDispatcher
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<Delegate>> _handlers = new();
    private readonly ILogger<SequentialEventDispatcher>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="SequentialEventDispatcher"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public SequentialEventDispatcher(ILogger<SequentialEventDispatcher>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public int HandlerCount => _handlers.Values.Sum(bag => bag.Count);

    /// <inheritdoc />
    public void AddHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        if (_disposed) return;

        var handlers = _handlers.GetOrAdd(typeof(T), _ => new ConcurrentBag<Delegate>());
        handlers.Add(handler);

        _logger?.LogDebug("Added handler for event type {EventType}. Total handlers: {HandlerCount}",
            typeof(T).Name, HandlerCount);
    }

    /// <inheritdoc />
    public void RemoveHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        if (_disposed) return;

        if (!_handlers.TryGetValue(typeof(T), out var handlers)) return;

        var newHandlers = new ConcurrentBag<Delegate>();
        foreach (var existingHandler in handlers)
        {
            if (!ReferenceEquals(existingHandler, handler))
                newHandlers.Add(existingHandler);
        }

        _handlers.TryUpdate(typeof(T), newHandlers, handlers);

        _logger?.LogDebug("Removed handler for event type {EventType}. Total handlers: {HandlerCount}",
            typeof(T).Name, HandlerCount);
    }

    /// <inheritdoc />
    public async Task DispatchAsync<T>(T eventArgs, CancellationToken cancellationToken = default) where T : IrcEvent
    {
        if (_disposed) return;

        if (!_handlers.TryGetValue(typeof(T), out var handlers)) return;

        _logger?.LogTrace("Dispatching event {EventType} to {HandlerCount} handlers sequentially",
            typeof(T).Name, handlers.Count);

        foreach (var handler in handlers)
        {
            if (handler is Func<T, Task> typedHandler)
            {
                try
                {
                    await typedHandler(eventArgs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in event handler for {EventType}", typeof(T).Name);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handlers.Clear();
    }
}

/// <summary>
/// Background event dispatcher — specific handlers run in dedicated background processing queues.
/// </summary>
public class BackgroundEventDispatcher : IEventDispatcher
{
    private readonly ThreadedEventDispatcher _normalDispatcher;
    private readonly ConcurrentDictionary<Type, BackgroundEventQueue> _backgroundQueues = new();
    private readonly ILogger<BackgroundEventDispatcher>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="BackgroundEventDispatcher"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public BackgroundEventDispatcher(ILogger<BackgroundEventDispatcher>? logger = null)
    {
        _logger = logger;
        _normalDispatcher = new ThreadedEventDispatcher();
    }

    /// <inheritdoc />
    public int HandlerCount => _normalDispatcher.HandlerCount + _backgroundQueues.Values.Sum(q => q.HandlerCount);

    /// <inheritdoc />
    public void AddHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        _normalDispatcher.AddHandler(handler);
    }

    /// <summary>
    /// Adds a handler that will run in a dedicated background processing queue.
    /// </summary>
    /// <typeparam name="T">The event type to handle.</typeparam>
    /// <param name="handler">The async handler delegate.</param>
    public void AddBackgroundHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        if (_disposed) return;

        var queue = _backgroundQueues.GetOrAdd(typeof(T), _ => new BackgroundEventQueue<T>(_logger));
        ((BackgroundEventQueue<T>)queue).AddHandler(handler);

        _logger?.LogDebug("Added background handler for event type {EventType}", typeof(T).Name);
    }

    /// <inheritdoc />
    public void RemoveHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        _normalDispatcher.RemoveHandler(handler);

        if (_backgroundQueues.TryGetValue(typeof(T), out var queue))
        {
            ((BackgroundEventQueue<T>)queue).RemoveHandler(handler);
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync<T>(T eventArgs, CancellationToken cancellationToken = default) where T : IrcEvent
    {
        if (_disposed) return;

        // Dispatch to normal handlers
        var normalTask = _normalDispatcher.DispatchAsync(eventArgs, cancellationToken);

        // Enqueue for background handlers
        if (_backgroundQueues.TryGetValue(typeof(T), out var queue))
        {
            ((BackgroundEventQueue<T>)queue).Enqueue(eventArgs);
        }

        await normalTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _normalDispatcher.Dispose();

        foreach (var queue in _backgroundQueues.Values)
        {
            queue.Dispose();
        }

        _backgroundQueues.Clear();
    }
}

/// <summary>
/// Base class for background event processing queues.
/// </summary>
public abstract class BackgroundEventQueue : IDisposable
{
    /// <inheritdoc cref="IEventDispatcher.HandlerCount" />
    public abstract int HandlerCount { get; }
    /// <inheritdoc />
    public abstract void Dispose();
}

/// <summary>
/// Background event processing queue for a specific event type.
/// </summary>
/// <typeparam name="T">The event type this queue processes.</typeparam>
public class BackgroundEventQueue<T> : BackgroundEventQueue where T : IrcEvent
{
    private readonly ConcurrentQueue<T> _eventQueue = new();
    private readonly ConcurrentBag<Func<T, Task>> _handlers = new();
    private readonly SemaphoreSlim _eventSemaphore = new(0);
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ILogger? _logger;
    private volatile bool _disposed;

    /// <inheritdoc />
    public override int HandlerCount => _handlers.Count;

    /// <summary>
    /// Initializes a new <see cref="BackgroundEventQueue{T}"/> and starts the background processing loop.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public BackgroundEventQueue(ILogger? logger = null)
    {
        _logger = logger;
        _processingTask = Task.Run(ProcessEventsAsync);
    }

    /// <summary>
    /// Registers a handler to process events in this background queue.
    /// </summary>
    /// <param name="handler">The async handler delegate.</param>
    public void AddHandler(Func<T, Task> handler)
    {
        if (!_disposed)
            _handlers.Add(handler);
    }

    /// <summary>
    /// Removes a handler from this background queue.
    /// </summary>
    /// <param name="handler">The handler delegate to remove.</param>
    public void RemoveHandler(Func<T, Task> handler)
    {
        // ConcurrentBag doesn't support removal directly
        // In practice, this would need a more sophisticated implementation
    }

    /// <summary>
    /// Enqueues an event for background processing.
    /// </summary>
    /// <param name="eventArgs">The event to enqueue.</param>
    public void Enqueue(T eventArgs)
    {
        if (_disposed) return;

        _eventQueue.Enqueue(eventArgs);
        _eventSemaphore.Release();
    }

    private async Task ProcessEventsAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                await _eventSemaphore.WaitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                if (_eventQueue.TryDequeue(out var eventArgs))
                {
                    foreach (var handler in _handlers)
                    {
                        try
                        {
                            await handler(eventArgs).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error in background event handler for {EventType}", typeof(T).Name);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellationTokenSource.Cancel();
        _eventSemaphore.Dispose();
        _cancellationTokenSource.Dispose();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error waiting for background event processing task to complete");
        }
    }
}
