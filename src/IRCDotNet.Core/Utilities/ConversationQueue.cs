using System.Collections.Concurrent;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Utilities;

/// <summary>
/// Utility for handling conversational flows and waiting for specific events.
/// Inspired by PircBotX's WaitForQueue.
/// </summary>
public class ConversationQueue : IDisposable
{
    private readonly IrcClient _client;
    private readonly ConcurrentQueue<IrcEvent> _eventQueue = new();
    private readonly SemaphoreSlim _eventSemaphore = new(0);
    private readonly List<IDisposable> _subscriptions = new();
    private readonly ILogger<ConversationQueue>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="ConversationQueue"/> that listens for events from the specified client.
    /// </summary>
    /// <param name="client">The IRC client to listen on.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public ConversationQueue(IrcClient client, ILogger<ConversationQueue>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;

        // Subscribe to all events and queue them
        SubscribeToEvents();
    }

    /// <summary>
    /// Wait for a specific type of event with optional filtering.
    /// </summary>
    /// <typeparam name="T">Type of event to wait for</typeparam>
    /// <param name="predicate">Optional predicate to filter events</param>
    /// <param name="timeout">Timeout for waiting</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The first matching event, or null if timeout/cancellation occurs</returns>
    public async Task<T?> WaitForAsync<T>(
        Func<T, bool>? predicate = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where T : IrcEvent
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConversationQueue));

        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;

        using var combinedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var combinedToken = combinedCts.Token;

        _logger?.LogDebug("Waiting for event of type {EventType}", typeof(T).Name);

        while (!combinedToken.IsCancellationRequested)
        {
            try
            {
                await _eventSemaphore.WaitAsync(combinedToken).ConfigureAwait(false);

                if (_eventQueue.TryDequeue(out var eventItem))
                {
                    if (eventItem is T typedEvent)
                    {
                        if (predicate == null || predicate(typedEvent))
                        {
                            _logger?.LogDebug("Found matching event of type {EventType}", typeof(T).Name);
                            return typedEvent;
                        }
                        else
                        {
                            _logger?.LogTrace("Event of type {EventType} did not match predicate", typeof(T).Name);
                        }
                    }

                    // If not the right type or doesn't match predicate, continue waiting
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Wait for event {EventType} was cancelled or timed out", typeof(T).Name);
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Wait for a message from a specific user.
    /// </summary>
    /// <param name="nick">The nickname.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching event, or <c>null</c> if not found.</returns>
    public async Task<EnhancedMessageEvent?> WaitForMessageFromUserAsync(
        string nick,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await WaitForAsync<EnhancedMessageEvent>(
            e => e.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase),
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for a message in a specific channel.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching event, or <c>null</c> if not found.</returns>
    public async Task<EnhancedMessageEvent?> WaitForMessageInChannelAsync(
        string channel,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await WaitForAsync<EnhancedMessageEvent>(
            e => e.Target.Equals(channel, StringComparison.OrdinalIgnoreCase),
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for a message containing specific text.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="ignoreCase">Whether to ignore case when matching.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching event, or <c>null</c> if not found.</returns>
    public async Task<EnhancedMessageEvent?> WaitForMessageContainingAsync(
        string text,
        bool ignoreCase = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return await WaitForAsync<EnhancedMessageEvent>(
            e => e.Text.Contains(text, comparison),
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for any message and return it.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching event, or <c>null</c> if not found.</returns>
    public async Task<EnhancedMessageEvent?> WaitForAnyMessageAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await WaitForAsync<EnhancedMessageEvent>(timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Collect multiple events of a specific type until a condition is met.
    /// </summary>
    /// <param name="stopCondition">Predicate that signals collection is complete.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of collected T objects.</returns>
    public async Task<List<T>> CollectEventsUntilAsync<T>(
        Func<T, bool> stopCondition,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where T : IrcEvent
    {
        var events = new List<T>();
        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;

        using var combinedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var combinedToken = combinedCts.Token;

        while (!combinedToken.IsCancellationRequested)
        {
            var eventItem = await WaitForAsync<T>(cancellationToken: combinedToken).ConfigureAwait(false);
            if (eventItem != null)
            {
                events.Add(eventItem);

                if (stopCondition(eventItem))
                {
                    break;
                }
            }
            else
            {
                break; // Timeout or cancellation
            }
        }

        return events;
    }

    /// <summary>
    /// Start a conversation flow that can handle multiple interactions.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public ConversationBuilder StartConversation()
    {
        return new ConversationBuilder(this, _client, _logger);
    }

    private void SubscribeToEvents()
    {
        // In a real implementation, we would subscribe to all event types from the client
        // For now, this is a placeholder showing the concept

        // Example subscriptions:
        // _subscriptions.Add(_client.OnMessage.Subscribe(OnEventReceived));
        // _subscriptions.Add(_client.OnUserJoined.Subscribe(OnEventReceived));
        // etc.
    }

    private void OnEventReceived(IrcEvent eventArgs)
    {
        if (_disposed) return;

        _eventQueue.Enqueue(eventArgs);
        _eventSemaphore.Release();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _eventSemaphore.Dispose();
    }
}

/// <summary>
/// Builder for creating complex conversation flows.
/// </summary>
public class ConversationBuilder
{
    private readonly ConversationQueue _queue;
    private readonly IrcClient _client;
    private readonly ILogger? _logger;
    private readonly List<ConversationStep> _steps = new();

    internal ConversationBuilder(ConversationQueue queue, IrcClient client, ILogger? logger)
    {
        _queue = queue;
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Add a step that sends a message and waits for a response.
    /// </summary>
    /// <param name="target">The target (channel or nickname).</param>
    /// <param name="message">The message text.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>This builder for chaining.</returns>
    public ConversationBuilder SendAndWait(string target, string message, TimeSpan? timeout = null)
    {
        _steps.Add(new SendAndWaitStep(target, message, timeout));
        return this;
    }

    /// <summary>
    /// Add a step that waits for a specific condition.
    /// </summary>
    /// <param name="condition">Predicate to match the desired event.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>This builder for chaining.</returns>
    public ConversationBuilder WaitFor<T>(Func<T, bool> condition, TimeSpan? timeout = null) where T : IrcEvent
    {
        _steps.Add(new WaitForStep<T>(condition, timeout));
        return this;
    }

    /// <summary>
    /// Add a step that executes custom logic.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <returns>This builder for chaining.</returns>
    public ConversationBuilder Execute(Func<Task> action)
    {
        _steps.Add(new ExecuteStep(action));
        return this;
    }

    /// <summary>
    /// Execute the conversation flow.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ConversationResult.</returns>
    public async Task<ConversationResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IrcEvent>();

        foreach (var step in _steps)
        {
            try
            {
                var result = await step.ExecuteAsync(_queue, _client, cancellationToken).ConfigureAwait(false);
                if (result != null)
                    results.Add(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing conversation step");
                return new ConversationResult(false, results, ex);
            }
        }

        return new ConversationResult(true, results);
    }
}

/// <summary>
/// Base class for conversation steps.
/// </summary>
public abstract class ConversationStep
{
    /// <summary>
    /// Executes this conversation step.
    /// </summary>
    /// <param name="queue">The conversation queue for waiting on events.</param>
    /// <param name="client">The IRC client for sending messages.</param>
    /// <param name="cancellationToken">Token to cancel the step.</param>
    /// <returns>The event produced by this step, or <c>null</c>.</returns>
    public abstract Task<IrcEvent?> ExecuteAsync(ConversationQueue queue, IrcClient client, CancellationToken cancellationToken);
}

/// <summary>
/// Step that sends a message and waits for any response.
/// </summary>
public class SendAndWaitStep : ConversationStep
{
    private readonly string _target;
    private readonly string _message;
    private readonly TimeSpan? _timeout;

    /// <summary>
    /// Initializes a new <see cref="SendAndWaitStep"/>.
    /// </summary>
    /// <param name="target">Target to send the message to.</param>
    /// <param name="message">The message text.</param>
    /// <param name="timeout">Optional timeout for waiting.</param>
    public SendAndWaitStep(string target, string message, TimeSpan? timeout)
    {
        _target = target;
        _message = message;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public override async Task<IrcEvent?> ExecuteAsync(ConversationQueue queue, IrcClient client, CancellationToken cancellationToken)
    {
        await client.SendMessageWithCancellationAsync(_target, _message, cancellationToken).ConfigureAwait(false);
        return await queue.WaitForAnyMessageAsync(_timeout, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Step that waits for a specific event type and condition.
/// </summary>
/// <typeparam name="T">The event type to wait for.</typeparam>
public class WaitForStep<T> : ConversationStep where T : IrcEvent
{
    private readonly Func<T, bool> _condition;
    private readonly TimeSpan? _timeout;

    /// <summary>
    /// Initializes a new <see cref="WaitForStep{T}"/>.
    /// </summary>
    /// <param name="condition">Predicate to match the desired event.</param>
    /// <param name="timeout">Optional timeout for waiting.</param>
    public WaitForStep(Func<T, bool> condition, TimeSpan? timeout)
    {
        _condition = condition;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public override async Task<IrcEvent?> ExecuteAsync(ConversationQueue queue, IrcClient client, CancellationToken cancellationToken)
    {
        return await queue.WaitForAsync(_condition, _timeout, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Step that executes custom logic.
/// </summary>
public class ExecuteStep : ConversationStep
{
    private readonly Func<Task> _action;

    /// <summary>
    /// Initializes a new <see cref="ExecuteStep"/>.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    public ExecuteStep(Func<Task> action)
    {
        _action = action;
    }

    /// <inheritdoc />
    public override async Task<IrcEvent?> ExecuteAsync(ConversationQueue queue, IrcClient client, CancellationToken cancellationToken)
    {
        await _action().ConfigureAwait(false);
        return null;
    }
}

/// <summary>
/// Result of a conversation flow execution.
/// </summary>
public class ConversationResult
{
    /// <summary>Whether the conversation completed successfully.</summary>
    public bool Success { get; }
    /// <summary>Events collected during the conversation.</summary>
    public List<IrcEvent> Events { get; }
    /// <summary>The exception that caused the conversation to fail, if any.</summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Initializes a new <see cref="ConversationResult"/>.
    /// </summary>
    /// <param name="success">Whether the conversation completed successfully.</param>
    /// <param name="events">Events collected during the conversation.</param>
    /// <param name="exception">Optional exception if the conversation failed.</param>
    public ConversationResult(bool success, List<IrcEvent> events, Exception? exception = null)
    {
        Success = success;
        Events = events;
        Exception = exception;
    }
}
