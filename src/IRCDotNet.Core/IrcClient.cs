using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using IRCDotNet.Core.Transport;
using IRCDotNet.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core;

/// <summary>
/// IRC (Internet Relay Chat) client implementing RFC 1459, the Modern IRC Client Protocol, and IRCv3 extensions.
/// Provides async connect/disconnect, channel management, messaging, SASL authentication, rate limiting, and auto-reconnect.
/// </summary>
public class IrcClient : IIrcClient
{
    private static readonly Regex NickUserHostRegex = new(@"^([^!]+)!([^@]+)@(.+)$", RegexOptions.Compiled);

    private readonly IrcClientOptions _options;
    private readonly ILogger? _logger;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _sendLock = new(5, 5); // Allow up to 5 concurrent sends
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private volatile int _disposed = 0; // Use Interlocked for thread-safety

    // Cached collections to prevent excessive allocations
    private IReadOnlyDictionary<string, IReadOnlySet<string>>? _cachedChannels;
    private IReadOnlySet<string>? _cachedEnabledCapabilities;
    private volatile bool _channelsCacheValid = false;
    private volatile bool _capabilitiesCacheValid = false;

    private IIrcTransport? _transport;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _readLoopTask;
    private Timer? _pingTimer;
    private volatile bool _isConnected;
    private volatile bool _isRegistered;
    private volatile string _currentNick = string.Empty;
    private readonly ConcurrentHashSet<string> _supportedCapabilities = new();
    private readonly ConcurrentHashSet<string> _enabledCapabilities = new(); private readonly ConcurrentDictionary<string, ConcurrentHashSet<string>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastPongReceived = DateTimeOffset.UtcNow;
    private volatile int _reconnectAttempts;
    private volatile List<string> _channelsToRejoin = new();

    // NickServ state tracking
    private volatile bool _nickServIdentified;

    // IRCv3 state tracking
    private volatile bool _saslInProgress;
    private volatile string? _currentSaslMechanism;
    private readonly ConcurrentDictionary<string, UserInfo> _userInfo = new();
    private readonly ConcurrentHashSet<string> _monitoredNicks = new();

    // Protocol enhancements
    private readonly IrcRateLimiter _rateLimiter;
    private readonly IsupportParser _isupportParser;

    /// <summary>
    /// Gets whether the client is currently connected to an IRC server.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets whether the client has completed IRC registration (NICK/USER handshake).
    /// </summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// Gets the client's current nickname on the server.
    /// </summary>
    public string CurrentNick => _currentNick;

    /// <summary>
    /// Gets a snapshot of all channels the client is in, mapped to their user sets.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Channels
    {
        get
        {
            if (!_channelsCacheValid || _cachedChannels == null)
            {
                lock (_stateLock)
                {
                    if (!_channelsCacheValid || _cachedChannels == null)
                    {
                        // Create a safe snapshot of the channels collection with proper enumeration
                        var channelSnapshot = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

                        // Take a snapshot of the keys first to avoid enumeration modification
                        var channelKeys = _channels.Keys.ToArray();
                        foreach (var channelKey in channelKeys)
                        {
                            if (_channels.TryGetValue(channelKey, out var users))
                            {
                                // Create immutable snapshot of users
                                channelSnapshot[channelKey] = users.ToHashSet();
                            }
                        }

                        _cachedChannels = channelSnapshot;
                        _channelsCacheValid = true;
                    }
                }
            }
            return _cachedChannels ?? new Dictionary<string, IReadOnlySet<string>>();
        }
    }

    /// <summary>
    /// Gets the set of IRCv3 capabilities currently enabled for this connection.
    /// </summary>
    public IReadOnlySet<string> EnabledCapabilities
    {
        get
        {
            var cached = _cachedEnabledCapabilities;
            if (!_capabilitiesCacheValid || cached == null)
            {
                lock (_stateLock)
                {
                    // Re-read the cached value inside the lock to ensure consistency
                    cached = _cachedEnabledCapabilities;
                    if (!_capabilitiesCacheValid || cached == null)
                    {
                        cached = _enabledCapabilities.ToHashSet();
                        _cachedEnabledCapabilities = cached;
                        _capabilitiesCacheValid = true;
                    }
                }
            }
            return cached;
        }
    }// Core IRC Events

    /// <summary>Raised when a user joins a channel.</summary>
    public event EventHandler<UserJoinedChannelEvent>? UserJoinedChannel;
    /// <summary>Raised when a user leaves (PARTs) a channel.</summary>
    public event EventHandler<UserLeftChannelEvent>? UserLeftChannel;
    /// <summary>Raised when a user quits the IRC network.</summary>
    public event EventHandler<UserQuitEvent>? UserQuit;
    /// <summary>Raised when a PRIVMSG is received (channel or private message).</summary>
    public event EventHandler<PrivateMessageEvent>? PrivateMessageReceived;
    /// <summary>Raised when a NOTICE is received.</summary>
    public event EventHandler<NoticeEvent>? NoticeReceived;
    /// <summary>Raised when the client successfully connects and registers with the server.</summary>
    public event EventHandler<ConnectedEvent>? Connected;
    /// <summary>Raised when the client disconnects from the server.</summary>
    public event EventHandler<DisconnectedEvent>? Disconnected;
    /// <summary>Raised when a channel topic is changed.</summary>
    public event EventHandler<TopicChangedEvent>? TopicChanged;
    /// <summary>Raised when a user is kicked from a channel.</summary>
    public event EventHandler<UserKickedEvent>? UserKicked;
    /// <summary>Raised when a user changes their nickname.</summary>
    public event EventHandler<NickChangedEvent>? NickChanged;
    /// <summary>Raised when a nickname collision occurs (ERR_NICKCOLLISION 436).</summary>
    public event EventHandler<NicknameCollisionEvent>? NicknameCollision;
    /// <summary>Raised when the server sends the user list for a channel (RPL_NAMREPLY).</summary>
    public event EventHandler<ChannelUsersEvent>? ChannelUsersReceived;
    /// <summary>Raised for every raw IRC message received from the server.</summary>
    public event EventHandler<RawMessageEvent>? RawMessageReceived;
    /// <summary>Raised when a channel join attempt fails (banned, invite-only, etc.).</summary>
    public event EventHandler<ChannelJoinFailedEvent>? ChannelJoinFailed;

    // IRCv3 Events
    /// <summary>Raised when IRCv3 capability negotiation completes.</summary>
    public event EventHandler<CapabilitiesNegotiatedEvent>? CapabilitiesNegotiated;
    /// <summary>Raised when a user's away status changes (requires away-notify capability).</summary>
    public event EventHandler<UserAwayStatusChangedEvent>? UserAwayStatusChanged;
    /// <summary>Raised when a user's account changes (requires account-notify capability).</summary>
    public event EventHandler<UserAccountChangedEvent>? UserAccountChanged;
    /// <summary>Raised on extended JOIN with account and realname (requires extended-join capability).</summary>
    public event EventHandler<ExtendedUserJoinedChannelEvent>? ExtendedUserJoinedChannel;
    /// <summary>Raised when a user's hostname changes (requires chghost capability).</summary>
    public event EventHandler<UserHostnameChangedEvent>? UserHostnameChanged;
    /// <summary>Raised when a BATCH message is received (requires batch capability).</summary>
    public event EventHandler<BatchEvent>? BatchReceived;
    /// <summary>Raised during SASL authentication with success or failure status.</summary>
    public event EventHandler<SaslAuthenticationEvent>? SaslAuthentication;
    /// <summary>Raised when a message with IRCv3 tags is received.</summary>
    public event EventHandler<MessageTagsEvent>? MessageTagsReceived;

    // RFC 1459 Events
    /// <summary>Raised when a WHO reply is received.</summary>
    public event EventHandler<WhoReceivedEvent>? WhoReceived;
    /// <summary>Raised when a WHOWAS reply is received.</summary>
    public event EventHandler<WhoWasReceivedEvent>? WhoWasReceived;
    /// <summary>Raised for each channel entry in a LIST reply.</summary>
    public event EventHandler<ChannelListReceivedEvent>? ChannelListReceived;
    /// <summary>Raised when the LIST reply ends.</summary>
    public event EventHandler<ChannelListEndEvent>? ChannelListEndReceived;

    // CTCP Events
    /// <summary>Raised when a CTCP request is received (e.g. VERSION, PING, TIME). ACTION requests fire <see cref="CtcpActionReceived"/> instead.</summary>
    public event EventHandler<CtcpRequestEvent>? CtcpRequestReceived;
    /// <summary>Raised when a CTCP reply is received (response to a request we sent).</summary>
    public event EventHandler<CtcpReplyEvent>? CtcpReplyReceived;
    /// <summary>Raised when a CTCP ACTION is received (<c>/me</c> command).</summary>
    public event EventHandler<CtcpActionEvent>? CtcpActionReceived;

    // General events
    /// <summary>Raised when the client is invited to a channel by another user.</summary>
    public event EventHandler<InviteReceivedEvent>? InviteReceived;
    /// <summary>Raised when the server confirms the client's own away status change (RPL_UNAWAY 305 / RPL_NOWAWAY 306).</summary>
    public event EventHandler<OwnAwayStatusChangedEvent>? OwnAwayStatusChanged;
    /// <summary>Raised when channel mode information is received (RPL_CHANNELMODEIS 324, in response to MODE #channel query).</summary>
    public event EventHandler<ChannelModeIsEvent>? ChannelModeIsReceived;
    /// <summary>Raised for general IRC error replies (482 not op, 442 not on channel, 461 need more params, etc.).</summary>
    public event EventHandler<ErrorReplyEvent>? ErrorReplyReceived;

    // Enhanced Events (PircBotX-inspired)
    /// <summary>Enhanced message event with client reference for fluent API access.</summary>
    public event EventHandler<EnhancedMessageEvent>? OnEnhancedMessage;
    /// <summary>Enhanced connected event with client reference.</summary>
    public event EventHandler<EnhancedConnectedEvent>? OnEnhancedConnected;
    /// <summary>Enhanced disconnected event with client reference.</summary>
    public event EventHandler<EnhancedDisconnectedEvent>? OnEnhancedDisconnected;
    /// <summary>Enhanced user joined event with client reference.</summary>
    public event EventHandler<EnhancedUserJoinedChannelEvent>? OnEnhancedUserJoined;
    /// <summary>Generic async message handler for polymorphic message processing.</summary>
    public event Func<IMessageEvent, Task>? OnGenericMessage;
    /// <summary>Pre-send interception event, fired before each outgoing message.</summary>
    public event Action<PreSendMessageEvent>? OnPreSendMessage;

    /// <summary>
    /// Creates a new IRC client with the specified options.
    /// </summary>
    /// <param name="options">Connection and protocol configuration.</param>
    public IrcClient(IrcClientOptions options)
        : this(options, (ILogger?)null)
    {
    }

    /// <summary>
    /// Creates a new IRC client with the specified options and typed logger.
    /// </summary>
    /// <param name="options">Connection and protocol configuration.</param>
    /// <param name="logger">Optional typed logger for diagnostics.</param>
    public IrcClient(IrcClientOptions options, ILogger<IrcClient>? logger = null)
        : this(options, (ILogger?)logger)
    {
    }

    /// <summary>
    /// Creates a new IRC client with the specified options and logger.
    /// </summary>
    /// <param name="options">Connection and protocol configuration.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public IrcClient(IrcClientOptions options, ILogger? logger)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Clone();
        _options.Validate();
        _logger = logger;
        _currentNick = _options.Nick;

        // Initialize protocol enhancements
        _rateLimiter = new IrcRateLimiter();
        _isupportParser = new IsupportParser();
    }

    /// <summary>
    /// Connects to the IRC server specified in <see cref="Configuration"/>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    /// <exception cref="InvalidOperationException">Already connected or connection already in progress.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!await _connectLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A connection attempt is already in progress");

        try
        {
            if (_isConnected)
                throw new InvalidOperationException("Already connected");

            try
            {
                _logger?.LogInformation("Connecting to {Endpoint}",
                    !string.IsNullOrWhiteSpace(_options.WebSocketUri)
                        ? _options.WebSocketUri
                        : $"{_options.Server}:{_options.Port} (SSL: {_options.UseSsl})"); _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Create transport (WebSocket or TCP)
                if (!string.IsNullOrWhiteSpace(_options.WebSocketUri))
                {
                    _transport = new WebSocketIrcTransport(_options, _logger);
                }
                else
                {
                    _transport = new TcpIrcTransport(_options, _logger);
                }

                await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

                _isConnected = true;
                _logger?.LogInformation("Connected to IRC server");            // Start read loop
                _readLoopTask = ReadLoopAsync(_cancellationTokenSource.Token);

                // Start ping timer
                _pingTimer = new Timer(SendPing, null, _options.PingIntervalMs, _options.PingIntervalMs);

                // Begin registration
                await RegisterAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to IRC server");
                await DisconnectInternalAsync("Connection failed during setup").ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Disconnects from the IRC server, optionally sending a QUIT reason.
    /// </summary>
    /// <param name="reason">Optional quit message sent to the server.</param>
    public async Task DisconnectAsync(string? reason = null)
    {
        // If not connected, just raise the event for testing purposes
        if (!_isConnected)
        {
            var eventArgs = new DisconnectedEvent(new IrcMessage { Command = "QUIT" }, reason ?? "Client disconnected");
            RaiseEventAsync(Disconnected, eventArgs);

            // Fire enhanced disconnect event
            var enhancedDisconnectEvent = new EnhancedDisconnectedEvent(new IrcMessage { Command = "QUIT" }, this, _options.Server, reason ?? "Client disconnected", false);
            RaiseEventAsync(OnEnhancedDisconnected, enhancedDisconnectEvent);
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(reason))
            {
                await SendRawAsync($"QUIT :{reason}").ConfigureAwait(false);
            }
            else
            {
                await SendRawAsync("QUIT").ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException)
        {
            // If cancellation was requested, skip sending QUIT and proceed with disconnect
            _logger?.LogDebug("Skipping QUIT message due to cancellation");
        }
        catch (InvalidOperationException)
        {
            // Connection already closed, proceed with cleanup
            _logger?.LogDebug("Connection already closed, proceeding with cleanup");
        }

        await DisconnectInternalAsync(reason).ConfigureAwait(false);

        // DisconnectInternalAsync now handles event raising
    }
    private async Task DisconnectInternalAsync(string? reason = null)
    {
        bool wasConnected = false;

        // Use state lock to ensure atomic state updates
        lock (_stateLock)
        {
            wasConnected = _isConnected;
            _isConnected = false;
            _isRegistered = false;
            _currentNick = _options.Nick; // Reset to original nick
        }

        // Stop and dispose ping timer first to prevent new pings
        try
        {
            _pingTimer?.Dispose();
            _pingTimer = null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing ping timer during disconnect");
        }

        // Cancel any ongoing operations
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error canceling operations during disconnect");
        }

        // Wait for read loop to complete gracefully
        try
        {
            if (_readLoopTask != null)
                await _readLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error waiting for read loop completion during disconnect");
        }

        // Dispose transport (handles writer, reader, stream, socket/websocket)
        try
        {
            if (_transport != null)
                await _transport.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing transport during disconnect");
        }

        try
        {
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing cancellation token source during disconnect");
        }

        // Clean up rate limiter buckets to prevent memory leaks
        try
        {
            _rateLimiter?.Cleanup(TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error cleaning up rate limiter during disconnect");
        }

        // Clear all state atomically
        lock (_stateLock)
        {
            _transport = null;
            _cancellationTokenSource = null;
            _readLoopTask = null;

            // Clear all collections and state
            _channels.Clear();
            _userInfo.Clear();
            _enabledCapabilities.Clear();
            _supportedCapabilities.Clear();
            _monitoredNicks.Clear();

            // Clear cached data
            _cachedChannels = null;
            _cachedEnabledCapabilities = null;
            _channelsCacheValid = false;

            // Reset authentication state
            _saslInProgress = false;
            _currentSaslMechanism = null;
            _lastPongReceived = DateTimeOffset.UtcNow;
        }

        // Raise disconnect event only if we were actually connected
        if (wasConnected)
        {
            var disconnectedEvent = new DisconnectedEvent(new IrcMessage { Command = "QUIT" }, reason ?? "Disconnected");
            RaiseEventAsync(Disconnected, disconnectedEvent);

            // Fire enhanced disconnect event
            var enhancedDisconnectEvent = new EnhancedDisconnectedEvent(new IrcMessage { Command = "QUIT" }, this, _options.Server, reason ?? "Disconnected", false);
            RaiseEventAsync(OnEnhancedDisconnected, enhancedDisconnectEvent);
        }

        _logger?.LogInformation("Disconnected from IRC server and cleaned up all resources");
    }

    /// <summary>
    /// Sends a raw IRC protocol message to the server.
    /// </summary>
    /// <param name="message">The raw IRC line to send (without trailing CRLF).</param>
    /// <exception cref="ArgumentException">Message is null, empty, or exceeds IRC length limits.</exception>
    /// <exception cref="InvalidOperationException">Not connected to a server.</exception>
    public async Task SendRawAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null or empty", nameof(message));

        // Quick validation without async operations
        var lengthValidation = IrcMessageValidator.ValidateMessageLength(message);
        if (!lengthValidation.IsValid)
        {
            _logger?.LogWarning("Invalid IRC message length: {Message} - {Error}", message, lengthValidation.ErrorMessage);
            throw new ArgumentException($"Invalid IRC message length: {lengthValidation.ErrorMessage}", nameof(message));
        }

        // Early connection check - fail fast if not connected
        if (!_isConnected)
            throw new InvalidOperationException("Not connected");

        // Fire pre-send event (allows cancellation)
        if (OnPreSendMessage != null)
        {
            var preSendEvent = new PreSendMessageEvent(new IrcMessage { Command = "RAW" }, this, "RAW", message);
            OnPreSendMessage(preSendEvent);
            if (preSendEvent.IsCancelled)
            {
                return; // Event was cancelled, don't send the message
            }
        }

        // Verify connection state atomically
        CancellationToken cancellationToken;

        lock (_stateLock)
        {
            if (!_isConnected || _transport == null)
                throw new InvalidOperationException("Not connected");

            cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        // Apply rate limiting to prevent flooding (if enabled)
        // Skip rate limiting during registration — CAP/NICK/USER must not be delayed
        if (_options.EnableRateLimit && _isRegistered)
        {
            try
            {
                var rateLimitConfig = _options.RateLimitConfig ?? IrcRateLimiter.DefaultConfig;
                if (!_rateLimiter.IsAllowed("send", rateLimitConfig))
                {
                    await _rateLimiter.WaitForAllowedAsync("send", rateLimitConfig, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) when (!_isConnected)
            {
                // If rate limiting was cancelled due to disconnection, throw the appropriate exception
                throw new InvalidOperationException("Not connected");
            }
            catch (OperationCanceledException) when (!_isConnected)
            {
                // If rate limiting was cancelled due to disconnection, throw the appropriate exception
                throw new InvalidOperationException("Not connected");
            }
            catch (TaskCanceledException ex) when (_isConnected)
            {
                // Preserve cancellation semantics so callers can catch OperationCanceledException
                throw new OperationCanceledException("Rate limiting wait was cancelled.", ex);
            }
            catch (OperationCanceledException ex) when (_isConnected)
            {
                // Preserve cancellation semantics so callers can catch OperationCanceledException
                throw new OperationCanceledException("Rate limiting wait was cancelled.", ex);
            }
        }

        // Use a unified timeout approach to prevent deadlocks
        // Use configurable timeout values from options
        var timeoutMs = cancellationToken.IsCancellationRequested
            ? _options.SendTimeoutCancelledMs
            : _options.SendTimeoutMs;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _sendLock.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_isConnected)
        {
            throw new InvalidOperationException("Not connected");
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting for send lock — preserve cancellation semantics
            throw;
        }

        try
        {
            // Re-verify connection state after acquiring the lock
            lock (_stateLock)
            {
                if (!_isConnected)
                    throw new InvalidOperationException("Not connected");
            }

            _logger?.LogDebug("Sending: {Message}", message);
            await _transport!.WriteLineAsync(message, combinedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HandleSendException(ex, message);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a raw IRC protocol message to the server with cancellation support.
    /// </summary>
    /// <param name="message">The raw IRC line to send (without trailing CRLF).</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <exception cref="ArgumentException">Message is null, empty, or exceeds IRC length limits.</exception>
    /// <exception cref="InvalidOperationException">Not connected to a server.</exception>
    public async Task SendRawWithCancellationAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null or empty", nameof(message));

        // Validate message length early, before any async operations
        var lengthValidation = IrcMessageValidator.ValidateMessageLength(message);
        if (!lengthValidation.IsValid)
        {
            _logger?.LogWarning("Invalid IRC message length: {Message} - {Error}", message, lengthValidation.ErrorMessage);
            throw new ArgumentException($"Invalid IRC message length: {lengthValidation.ErrorMessage}", nameof(message));
        }

        if (!_isConnected)
            throw new InvalidOperationException("Not connected");

        // Verify connection state atomically
        CancellationToken internalCancellationToken;

        lock (_stateLock)
        {
            if (!_isConnected || _transport == null)
                throw new InvalidOperationException("Not connected");

            internalCancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        // Use a unified timeout approach to prevent deadlocks, combining provided token with internal token
        var timeoutMs = cancellationToken.IsCancellationRequested || internalCancellationToken.IsCancellationRequested
            ? _options.SendTimeoutCancelledMs
            : _options.SendTimeoutWithCancellationMs;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalCancellationToken, timeoutCts.Token);

        // Apply rate limiting BEFORE acquiring the send lock to avoid holding the lock
        // while waiting for rate limit tokens (which would starve all other senders)
        if (_options.EnableRateLimit)
        {
            var rateLimitConfig = _options.RateLimitConfig ?? IrcRateLimiter.DefaultConfig;
            if (!_rateLimiter.IsAllowed("send", rateLimitConfig))
            {
                await _rateLimiter.WaitForAllowedAsync("send", rateLimitConfig, combinedCts.Token).ConfigureAwait(false);
            }
        }

        try
        {
            await _sendLock.WaitAsync(combinedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't attempt another wait if we couldn't acquire the lock
            throw;
        }

        try
        {
            // Re-verify connection state after acquiring the lock
            lock (_stateLock)
            {
                if (!_isConnected)
                    throw new InvalidOperationException("Not connected");
            }

            _logger?.LogDebug("Sending: {Message}", message);
            await _transport!.WriteLineAsync(message, combinedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HandleSendException(ex, message);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a PRIVMSG to a user or channel.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="message">The message text.</param>
    public virtual async Task SendMessageAsync(string target, string message)
    {
        await SendMessageWithCancellationAsync(target, message, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a PRIVMSG to a user or channel with cancellation support.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="message">The message text. Must not contain newline characters.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    public virtual async Task SendMessageWithCancellationAsync(string target, string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        // Reject embedded newlines — they would terminate the IRC line early and
        // cause everything after the first \r or \n to be interpreted as a separate
        // (potentially malicious) raw IRC command.
        if (message.Contains('\r') || message.Contains('\n'))
            throw new ArgumentException("Message must not contain newline characters. Split into separate calls.", nameof(message));

        if (target.Contains('\r') || target.Contains('\n'))
            throw new ArgumentException("Target must not contain newline characters.", nameof(target));

        await SendRawWithCancellationAsync($"PRIVMSG {target} :{message}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a NOTICE to a user or channel.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="message">The notice text.</param>
    public async Task SendNoticeAsync(string target, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        if (message.Contains('\r') || message.Contains('\n'))
            throw new ArgumentException("Message must not contain newline characters.", nameof(message));
        if (target.Contains('\r') || target.Contains('\n'))
            throw new ArgumentException("Target must not contain newline characters.", nameof(target));

        await SendRawAsync($"NOTICE {target} :{message}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a NOTICE to a user or channel with cancellation support.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="message">The notice text.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    public async Task SendNoticeWithCancellationAsync(string target, string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        if (message.Contains('\r') || message.Contains('\n'))
            throw new ArgumentException("Message must not contain newline characters.", nameof(message));
        if (target.Contains('\r') || target.Contains('\n'))
            throw new ArgumentException("Target must not contain newline characters.", nameof(target));

        await SendRawWithCancellationAsync($"NOTICE {target} :{message}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Joins an IRC channel.
    /// </summary>
    /// <param name="channel">Channel name (e.g. "#general").</param>
    /// <param name="key">Optional channel key (password) if the channel is +k.</param>
    public virtual async Task JoinChannelAsync(string channel, string? key = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));

        if (string.IsNullOrEmpty(key))
        {
            await SendRawAsync($"JOIN {channel}").ConfigureAwait(false);
        }
        else
        {
            await SendRawAsync($"JOIN {channel} {key}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Leaves (PARTs) an IRC channel.
    /// </summary>
    /// <param name="channel">Channel name to leave.</param>
    /// <param name="reason">Optional part message.</param>
    public async Task LeaveChannelAsync(string channel, string? reason = null)
    {
        if (string.IsNullOrEmpty(reason))
        {
            await SendRawAsync($"PART {channel}").ConfigureAwait(false);
        }
        else
        {
            await SendRawAsync($"PART {channel} :{reason}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Changes the client's nickname.
    /// </summary>
    /// <param name="newNick">The new nickname to use.</param>
    public async Task ChangeNickAsync(string newNick)
    {
        await SendRawAsync($"NICK {newNick}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the topic for a channel.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <param name="topic">The new topic text.</param>
    public async Task SetTopicAsync(string channel, string topic)
    {
        await SendRawAsync($"TOPIC {channel} :{topic}").ConfigureAwait(false);
    }

    /// <summary>
    /// Requests the current topic of a channel from the server.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    public async Task GetTopicAsync(string channel)
    {
        await SendRawAsync($"TOPIC {channel}").ConfigureAwait(false);
    }

    /// <summary>
    /// Requests the user list (NAMES) for a channel.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    public async Task GetChannelUsersAsync(string channel)
    {
        await SendRawAsync($"NAMES {channel}").ConfigureAwait(false);
    }

    /// <summary>
    /// Request channel list from the server (sends LIST command).
    /// Results arrive via ChannelListReceived events, ending with ChannelListEndReceived.
    /// </summary>
    public async Task RequestChannelListAsync()
    {
        await SendRawAsync("LIST").ConfigureAwait(false);
    }

    /// <summary>
    /// Collects the full channel list from the server into a single list.
    /// Sends LIST, accumulates ChannelListReceived events, returns when ChannelListEndReceived fires.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the list (default 30 seconds).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of channel entries with name, user count, and topic.</returns>
    public async Task<List<ChannelListEntry>> GetChannelListAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var entries = new System.Collections.Concurrent.ConcurrentBag<ChannelListEntry>();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEntry(object? sender, ChannelListReceivedEvent e)
        {
            entries.Add(new ChannelListEntry(e.Channel, e.UserCount, e.Topic));
        }

        void OnEnd(object? sender, ChannelListEndEvent e)
        {
            tcs.TrySetResult(true);
        }

        ChannelListReceived += OnEntry;
        ChannelListEndReceived += OnEnd;

        try
        {
            await RequestChannelListAsync().ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            ChannelListReceived -= OnEntry;
            ChannelListEndReceived -= OnEnd;
        }

        return entries.ToList();
    }

    /// <summary>
    /// Sends a WHOIS query for the specified user.
    /// </summary>
    /// <param name="nick">Nickname to look up.</param>
    public async Task GetUserInfoAsync(string nick)
    {
        await SendRawAsync($"WHOIS {nick}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sets or clears the client's away status.
    /// </summary>
    /// <param name="message">Away message. Pass <c>null</c> or empty to clear away status.</param>
    public async Task SetAwayAsync(string? message = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            await SendRawAsync("AWAY").ConfigureAwait(false);
        }
        else
        {
            await SendRawAsync($"AWAY :{message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Kicks a user from a channel (requires operator privileges).
    /// </summary>
    /// <param name="channel">Channel to kick from.</param>
    /// <param name="nick">Nickname of the user to kick.</param>
    /// <param name="reason">Optional kick reason.</param>
    public async Task KickUserAsync(string channel, string nick, string? reason = null)
    {
        if (string.IsNullOrEmpty(reason))
        {
            await SendRawAsync($"KICK {channel} {nick}").ConfigureAwait(false);
        }
        else
        {
            await SendRawAsync($"KICK {channel} {nick} :{reason}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invites a user to a channel.
    /// </summary>
    /// <param name="nick">Nickname of the user to invite.</param>
    /// <param name="channel">Channel to invite the user to.</param>
    public async Task InviteUserAsync(string nick, string channel)
    {
        await SendRawAsync($"INVITE {nick} {channel}").ConfigureAwait(false);
    }
    private async Task RegisterAsync()
    {
        // Request capabilities with enhanced logic
        var requestedCaps = _options.RequestAllCapabilities
            ? IrcCapabilities.DefaultCapabilities.Where(cap => !_options.BlacklistedCapabilities.Contains(cap)).ToList()
            : _options.RequestedCapabilities.Where(cap => !_options.BlacklistedCapabilities.Contains(cap)).ToList();

        if (requestedCaps.Count > 0)
        {
            if (_options.VerboseCapabilityNegotiation)
                _logger?.LogDebug("Requesting capabilities: {Capabilities}", string.Join(", ", requestedCaps));

            await SendRawAsync("CAP LS 302").ConfigureAwait(false);
        }

        // Send password if provided
        if (!string.IsNullOrEmpty(_options.Password))
        {
            await SendRawAsync($"PASS {_options.Password}").ConfigureAwait(false);
        }

        // Send nick and user
        await SendRawAsync($"NICK {_options.Nick}").ConfigureAwait(false);
        await SendRawAsync($"USER {_options.UserName} 0 * :{_options.RealName}").ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _transport != null)
            {
                var line = await _transport.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;

                _logger?.LogTrace("Received: {Line}", line);

                try
                {
                    var message = IrcMessage.Parse(line);
                    await ProcessMessageAsync(message).ConfigureAwait(false);

                    // Raise raw message event
                    RaiseEventAsync(RawMessageReceived, new RawMessageEvent(message));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to process message: {Line}", line);
                }
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            _logger?.LogError(ex, "Read loop error");
        }

        if (_isConnected)
        {
            // Save channels to rejoin after reconnection
            _channelsToRejoin = _channels.Keys.ToList();

            await DisconnectInternalAsync("Connection lost").ConfigureAwait(false);

            // Auto-reconnect if enabled
            if (_options.AutoReconnect)
            {
                SafeFireAndForget(() => AttemptReconnectAsync(), "AutoReconnect");
            }
        }
    }
    private async Task ProcessMessageAsync(IrcMessage message)
    {
        // Process message tags for IRCv3 features
        if (_options.EnableMessageTags && message.Tags.Count > 0)
        {
            ProcessMessageTags(message);
        }

        switch (message.Command)
        {
            case IrcCommands.PING:
                await HandlePingAsync(message).ConfigureAwait(false);
                break;
            case IrcCommands.PONG:
                HandlePong(message);
                break;
            case IrcCommands.JOIN:
                HandleJoin(message);
                break;
            case IrcCommands.PART:
                HandlePart(message);
                break;
            case IrcCommands.QUIT:
                HandleQuit(message);
                break;
            case IrcCommands.PRIVMSG:
                HandlePrivateMessage(message);
                break;
            case IrcCommands.NOTICE:
                HandleNotice(message);
                break;
            case IrcCommands.NICK:
                HandleNickChange(message);
                break;
            case IrcCommands.TOPIC:
                HandleTopic(message);
                break;
            case IrcCommands.KICK:
                HandleKick(message);
                break;
            case IrcCommands.MODE:
                HandleMode(message);
                break;
            case IrcCommands.INVITE:
                HandleInvite(message);
                break;
            case IrcCommands.CAP:
                await HandleCapabilityAsync(message).ConfigureAwait(false);
                break;
            case IrcCommands.AUTHENTICATE:
                await HandleAuthenticateAsync(message).ConfigureAwait(false);
                break;
            case IrcCommands.AWAY:
                HandleAwayChange(message);
                break;
            case IrcCommands.ACCOUNT:
                HandleAccountChange(message);
                break;
            case IrcCommands.CHGHOST:
                HandleHostnameChange(message);
                break;
            case IrcCommands.BATCH:
                HandleBatch(message);
                break;
            case IrcCommands.TAGMSG:
                HandleTagMessage(message);
                break;
            case IrcCommands.WHO:
                HandleWho(message);
                break;
            case IrcCommands.WHOWAS:
                HandleWhoWas(message);
                break;
            case IrcCommands.LIST:
                HandleList(message);
                break;
            case IrcNumericReplies.RPL_WELCOME:
                HandleWelcome(message);
                break;
            case IrcNumericReplies.RPL_NAMREPLY:
                HandleNamesReply(message);
                break;
            case IrcNumericReplies.RPL_ENDOFNAMES:
                HandleEndOfNames(message);
                break;
            case IrcNumericReplies.RPL_TOPIC:
            case IrcNumericReplies.RPL_NOTOPIC:
                HandleTopicReply(message);
                break;
            case IrcNumericReplies.ERR_NICKNAMEINUSE:
                await HandleNicknameInUseAsync(message).ConfigureAwait(false);
                break;
            case IrcNumericReplies.ERR_ERRONEUSNICKNAME:
                _logger?.LogWarning("Erroneous nickname rejected by server: {Nick}",
                    message.Parameters.Count > 1 ? message.Parameters[1] : _currentNick);
                await HandleNicknameInUseAsync(message).ConfigureAwait(false);
                break;
            case IrcNumericReplies.ERR_NICKCOLLISION:
                await HandleNicknameCollisionAsync(message).ConfigureAwait(false);
                break;
            case IrcNumericReplies.RPL_SASLSUCCESS:
                HandleSaslSuccess(message);
                break;
            case IrcNumericReplies.ERR_SASLFAIL:
                HandleSaslFailure(message);
                break;
            case IrcNumericReplies.ERR_SASLABORTED:
                HandleSaslAborted(message);
                break;
            case IrcNumericReplies.ERR_SASLTOOLONG:
                HandleSaslTooLong(message);
                break;
            case IrcNumericReplies.ERR_SASLALREADY:
                HandleSaslAlready(message);
                break;
            case IrcNumericReplies.RPL_SASLMECHS:
                HandleSaslMechs(message);
                break;

            // WHO command responses
            case IrcNumericReplies.RPL_WHOREPLY:
            case IrcNumericReplies.RPL_ENDOFWHO:
                HandleWho(message);
                break;

            // WHOWAS command responses
            case IrcNumericReplies.RPL_WHOWASUSER:
            case IrcNumericReplies.RPL_ENDOFWHOWAS:
                HandleWhoWas(message);
                break;

            // LIST command responses
            case IrcNumericReplies.RPL_LIST:
            case IrcNumericReplies.RPL_LISTEND:
                HandleList(message);
                break;

            // Server Information (RFC 1459 Section 6.1)
            case IrcNumericReplies.RPL_YOURHOST:
            case IrcNumericReplies.RPL_CREATED:
            case IrcNumericReplies.RPL_MYINFO:
                HandleServerInfo(message);
                break;
            case IrcNumericReplies.RPL_ISUPPORT:
                HandleServerSupport(message);
                break;

            // MOTD Handling (RFC 1459 Section 6.2)
            case IrcNumericReplies.RPL_MOTDSTART:
            case IrcNumericReplies.RPL_MOTD:
            case IrcNumericReplies.RPL_ENDOFMOTD:
            case IrcNumericReplies.ERR_NOMOTD:
                HandleMotd(message);
                break;

            // User Information (RFC 1459 Section 6.1)
            case IrcNumericReplies.RPL_AWAY:
                HandleAwayReply(message);
                break;
            case IrcNumericReplies.RPL_UNAWAY:
            case IrcNumericReplies.RPL_NOWAWAY:
                HandleOwnAwayStatus(message);
                break;
            case IrcNumericReplies.RPL_CHANNELMODEIS:
                HandleChannelModeIs(message);
                break;
            case IrcNumericReplies.RPL_TOPICWHOTIME:
                // Topic metadata (who set it and when) — logged but not critical
                _logger?.LogDebug("Topic for {Channel} set by {Who} at {Time}",
                    message.Parameters.Count > 1 ? message.Parameters[1] : "?",
                    message.Parameters.Count > 2 ? message.Parameters[2] : "?",
                    message.Parameters.Count > 3 ? message.Parameters[3] : "?");
                break;
            case IrcNumericReplies.RPL_WHOISUSER:
            case IrcNumericReplies.RPL_WHOISSERVER:
            case IrcNumericReplies.RPL_WHOISOPERATOR:
            case IrcNumericReplies.RPL_WHOISIDLE:
            case IrcNumericReplies.RPL_ENDOFWHOIS:
            case IrcNumericReplies.RPL_WHOISCHANNELS:
                HandleWhoisReply(message);
                break;

            // Error Handling (RFC 1459 Section 6.1)
            case IrcNumericReplies.ERR_NOSUCHNICK:
            case IrcNumericReplies.ERR_NOSUCHSERVER:
            case IrcNumericReplies.ERR_NOSUCHCHANNEL:
            case IrcNumericReplies.ERR_CANNOTSENDTOCHAN:
            case IrcNumericReplies.ERR_UNKNOWNCOMMAND:
            case IrcNumericReplies.ERR_INVITEONLYCHAN:
            case IrcNumericReplies.ERR_BANNEDFROMCHAN:
            case IrcNumericReplies.ERR_BADCHANNELKEY:
            case IrcNumericReplies.ERR_CHANNELISFULL:
            case IrcNumericReplies.ERR_SSL_REQUIRED:
            case IrcNumericReplies.ERR_NOCHANMODES:
            case IrcNumericReplies.ERR_TOOMANYCHANNELS:
            case IrcNumericReplies.ERR_CHANOPRIVSNEEDED:
            case IrcNumericReplies.ERR_NOTONCHANNEL:
            case IrcNumericReplies.ERR_NEEDMOREPARAMS:
                HandleErrorReply(message);
                break;
        }
    }
    private async Task HandlePingAsync(IrcMessage message)
    {
        if (message.Parameters.Count > 0)
        {
            await SendRawAsync($"PONG :{message.Parameters[0]}").ConfigureAwait(false);
        }
    }

    private void HandlePong(IrcMessage message)
    {
        _lastPongReceived = DateTimeOffset.UtcNow;
    }

    private async Task HandleCapabilityAsync(IrcMessage message)
    {
        if (message.Parameters.Count < 2)
        {
            _logger?.LogWarning("CAP message with insufficient parameters: {Count}", message.Parameters.Count);
            return;
        }

        var subCommand = message.Parameters[1].ToUpperInvariant();
        _logger?.LogInformation("CAP {SubCommand} received (params: {ParamCount})", subCommand, message.Parameters.Count);

        switch (subCommand)
        {
            case "LS":
                await HandleCapabilityListAsync(message).ConfigureAwait(false);
                break;
            case "ACK":
                await HandleCapabilityAckAsync(message).ConfigureAwait(false);
                break;
            case "NAK":
                await HandleCapabilityNakAsync(message).ConfigureAwait(false);
                break;
            case "NEW":
                await HandleCapabilityNewAsync(message).ConfigureAwait(false);
                break;
            case "DEL":
                HandleCapabilityDel(message);
                break;
        }
    }

    private async Task HandleCapabilityListAsync(IrcMessage message)
    {
        if (message.Parameters.Count < 3)
        {
            _logger?.LogWarning("CAP LS with insufficient parameters: {Count}", message.Parameters.Count);
            return;
        }

        var isMultiline = message.Parameters.Count > 3 && message.Parameters[2] == "*";
        var capString = isMultiline ? message.Parameters[3] : message.Parameters[2];

        _logger?.LogInformation("CAP LS server capabilities: {Capabilities} (multiline: {IsMultiline})", capString, isMultiline);

        var capabilities = capString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var cap in capabilities)
        {
            // Handle capability values (e.g., "sasl=PLAIN,EXTERNAL")
            var capName = cap.Contains('=') ? cap.Split('=')[0] : cap;
            _supportedCapabilities.Add(capName);

            if (_options.VerboseCapabilityNegotiation)
                _logger?.LogDebug("Server supports capability: {Capability}", cap);
        }

        // If this is not a multiline response, process capability requests
        if (!isMultiline)
        {
            var requestedCaps = _options.RequestAllCapabilities
                ? _supportedCapabilities.Where(cap => !_options.BlacklistedCapabilities.Contains(cap)).ToList()
                : _options.RequestedCapabilities.Where(_supportedCapabilities.Contains).Where(cap => !_options.BlacklistedCapabilities.Contains(cap)).ToList();

            if (requestedCaps.Count > 0)
            {
                if (_options.VerboseCapabilityNegotiation)
                    _logger?.LogDebug("Requesting capabilities: {Capabilities}", string.Join(" ", requestedCaps));

                await SendRawAsync($"CAP REQ :{string.Join(" ", requestedCaps)}").ConfigureAwait(false);
            }
            else
            {
                await EndCapabilityNegotiationAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task HandleCapabilityAckAsync(IrcMessage message)
    {
        if (message.Parameters.Count < 3)
        {
            _logger?.LogWarning("CAP ACK with insufficient parameters: {Count}", message.Parameters.Count);
            return;
        }

        _logger?.LogInformation("CAP ACK received: {Capabilities}", message.Parameters[2]);

        var capabilities = message.Parameters[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var cap in capabilities)
        {
            var capName = cap.StartsWith('-') ? cap.Substring(1) : cap;
            if (cap.StartsWith('-'))
            {
                _enabledCapabilities.Remove(capName);
                InvalidateCapabilitiesCache();
                if (_options.VerboseCapabilityNegotiation)
                    _logger?.LogDebug("Capability disabled: {Capability}", capName);
            }
            else
            {
                _enabledCapabilities.Add(capName);
                InvalidateCapabilitiesCache();
                if (_options.VerboseCapabilityNegotiation)
                    _logger?.LogDebug("Capability enabled: {Capability}", capName);
            }
        }

        // Check if SASL is enabled and start authentication
        if (_enabledCapabilities.Contains(IrcCapabilities.SASL) && _options.Sasl != null)
        {
            await StartSaslAuthenticationAsync().ConfigureAwait(false);
        }
        else
        {
            await EndCapabilityNegotiationAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleCapabilityNakAsync(IrcMessage message)
    {
        if (_options.VerboseCapabilityNegotiation && message.Parameters.Count >= 3)
        {
            _logger?.LogWarning("Capabilities rejected: {Capabilities}", message.Parameters[2]);
        }

        await EndCapabilityNegotiationAsync().ConfigureAwait(false);
    }

    private async Task HandleCapabilityNewAsync(IrcMessage message)
    {
        if (message.Parameters.Count < 3) return;

        var capabilities = message.Parameters[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var cap in capabilities)
        {
            var capName = cap.Contains('=') ? cap.Split('=')[0] : cap;
            _supportedCapabilities.Add(capName);
            InvalidateCapabilitiesCache();

            if (_options.VerboseCapabilityNegotiation)
                _logger?.LogDebug("New capability available: {Capability}", cap);
        }

        // Auto-request new capabilities if configured
        if (_options.RequestAllCapabilities)
        {
            var newCaps = capabilities.Where(cap =>
            {
                var capName = cap.Contains('=') ? cap.Split('=')[0] : cap;
                return !_enabledCapabilities.Contains(capName) && !_options.BlacklistedCapabilities.Contains(capName);
            }).ToList();

            if (newCaps.Count > 0)
            {
                await SendRawAsync($"CAP REQ :{string.Join(" ", newCaps)}").ConfigureAwait(false);
            }
        }
    }

    private void HandleCapabilityDel(IrcMessage message)
    {
        if (message.Parameters.Count < 3) return;

        var capabilities = message.Parameters[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var cap in capabilities)
        {
            _supportedCapabilities.Remove(cap);
            _enabledCapabilities.Remove(cap);
            InvalidateCapabilitiesCache();

            if (_options.VerboseCapabilityNegotiation)
                _logger?.LogDebug("Capability removed: {Capability}", cap);
        }
    }
    private async Task EndCapabilityNegotiationAsync()
    {
        await SendRawAsync("CAP END").ConfigureAwait(false);        // Raise capabilities negotiated event
        RaiseEventAsync(CapabilitiesNegotiated, new CapabilitiesNegotiatedEvent(
            new IrcMessage { Command = IrcCommands.CAP }, _enabledCapabilities.ToHashSet(), _supportedCapabilities.ToHashSet()));

        if (_options.VerboseCapabilityNegotiation)
            _logger?.LogInformation("Capability negotiation complete. Enabled: {Capabilities}",
                string.Join(", ", _enabledCapabilities));
    }

    private async Task StartSaslAuthenticationAsync()
    {
        if (_options.Sasl == null) return;

        _saslInProgress = true;
        _currentSaslMechanism = _options.Sasl.Mechanism;

        _logger?.LogDebug("Starting SASL authentication with mechanism: {Mechanism}", _currentSaslMechanism);
        await SendRawAsync($"AUTHENTICATE {_currentSaslMechanism}").ConfigureAwait(false);
    }

    private async Task HandleAuthenticateAsync(IrcMessage message)
    {
        if (!_saslInProgress || _options.Sasl == null) return;

        if (message.Parameters.Count == 0) return;

        var response = message.Parameters[0];

        if (response == "+")
        {
            // Server is ready for authentication data
            var authData = GenerateSaslAuthData(_options.Sasl);
            if (authData != null)
            {
                // Split into 400-byte chunks as per IRC spec
                var chunks = ChunkAuthData(authData);
                foreach (var chunk in chunks)
                {
                    await SendRawAsync($"AUTHENTICATE {chunk}").ConfigureAwait(false);
                }

                if (authData.Length == 0)
                {
                    await SendRawAsync("AUTHENTICATE +").ConfigureAwait(false);
                }
            }
            else
            {
                await SendRawAsync("AUTHENTICATE *").ConfigureAwait(false);
                _saslInProgress = false;
            }
        }
    }

    private string? GenerateSaslAuthData(SaslOptions sasl)
    {
        switch (sasl.Mechanism.ToUpperInvariant())
        {
            case SaslMechanisms.PLAIN:
                // PLAIN format: \0username\0password
                var plainData = $"\0{sasl.Username}\0{sasl.Password}";
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainData));

            case SaslMechanisms.EXTERNAL:
                // EXTERNAL typically uses empty data or username only
                var externalData = string.IsNullOrEmpty(sasl.Username) ? "" : sasl.Username;
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(externalData));

            default:
                _logger?.LogWarning("Unsupported SASL mechanism: {Mechanism}", sasl.Mechanism);
                return null;
        }
    }

    private List<string> ChunkAuthData(string data)
    {
        const int maxChunkSize = 400;
        var chunks = new List<string>();

        if (string.IsNullOrEmpty(data))
        {
            chunks.Add("+");
            return chunks;
        }

        for (int i = 0; i < data.Length; i += maxChunkSize)
        {
            var chunk = data.Substring(i, Math.Min(maxChunkSize, data.Length - i));
            chunks.Add(chunk);
        }

        // Add final + if the last chunk was exactly maxChunkSize
        if (data.Length % maxChunkSize == 0)
        {
            chunks.Add("+");
        }

        return chunks;
    }

    private void HandleSaslSuccess(IrcMessage message)
    {
        _saslInProgress = false;
        _logger?.LogInformation("SASL authentication successful");
        RaiseEventAsync(SaslAuthentication, new SaslAuthenticationEvent(message, true, _currentSaslMechanism));
        SafeFireAndForget(() => EndCapabilityNegotiationAsync(), "SaslSuccessCapEnd");
    }

    private void HandleSaslFailure(IrcMessage message)
    {
        _saslInProgress = false; var errorMessage = message.Parameters.Count > 1 ? message.Parameters[1] : "Authentication failed";
        _logger?.LogError("SASL authentication failed: {Error}", errorMessage);
        RaiseEventAsync(SaslAuthentication, new SaslAuthenticationEvent(message, false, _currentSaslMechanism, errorMessage));

        if (_options.Sasl?.Required == true)
        {
            SafeFireAndForget(() => DisconnectAsync("SASL authentication required but failed"), "SaslRequiredDisconnect");
        }
        else
        {
            SafeFireAndForget(() => EndCapabilityNegotiationAsync(), "SaslFailureCapEnd");
        }
    }

    private void HandleSaslAborted(IrcMessage message)
    {
        _saslInProgress = false;
        _logger?.LogWarning("SASL authentication aborted");
        RaiseEventAsync(SaslAuthentication, new SaslAuthenticationEvent(message, false, _currentSaslMechanism, "Authentication aborted"));
        SafeFireAndForget(() => EndCapabilityNegotiationAsync(), "SaslAbortedCapEnd");
    }

    private void HandleSaslTooLong(IrcMessage message)
    {
        _saslInProgress = false;
        _logger?.LogError("SASL authentication failed: AUTHENTICATE message too long");
        RaiseEventAsync(SaslAuthentication, new SaslAuthenticationEvent(message, false, _currentSaslMechanism, "AUTHENTICATE message too long"));
        SafeFireAndForget(() => EndCapabilityNegotiationAsync(), "SaslTooLongCapEnd");
    }

    private void HandleSaslAlready(IrcMessage message)
    {
        _saslInProgress = false;
        _logger?.LogWarning("SASL authentication skipped: already authenticated");
        SafeFireAndForget(() => EndCapabilityNegotiationAsync(), "SaslAlreadyCapEnd");
    }

    private void HandleSaslMechs(IrcMessage message)
    {
        // 908 RPL_SASLMECHS: server lists available mechanisms after a failed attempt
        var mechs = message.Parameters.Count > 1 ? message.Parameters[1] : "";
        _logger?.LogInformation("Server available SASL mechanisms: {Mechanisms}", mechs);
        // Don't end CAP here — server will also send 904 (ERR_SASLFAIL) which handles CAP END
    }

    private void HandleJoin(IrcMessage message)
    {
        if (message.Parameters.Count == 0 || string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var channel = message.Parameters[0];

        // Extended JOIN support (IRCv3)
        string? account = null;
        string? realName = null;

        if (_enabledCapabilities.Contains(IrcCapabilities.EXTENDED_JOIN))
        {
            if (message.Parameters.Count >= 2)
                account = message.Parameters[1] == "*" ? null : message.Parameters[1];
            if (message.Parameters.Count >= 3)
                realName = message.Parameters[2];

            // Update user info
            UpdateUserInfo(nick, user, host, account, realName);
        }

        // If it's us joining, add to our channel list
        if (nick == _currentNick)
        {
            _channels[channel] = new ConcurrentHashSet<string>();
            InvalidateChannelsCache();
        }
        else if (_channels.ContainsKey(channel))
        {
            _channels[channel].Add(nick);
            InvalidateChannelsCache();
        }        // Raise appropriate events
        if (_enabledCapabilities.Contains(IrcCapabilities.EXTENDED_JOIN) && (account != null || realName != null))
        {
            RaiseEventAsync(ExtendedUserJoinedChannel, new ExtendedUserJoinedChannelEvent(message, nick, user, host, channel, account, realName));
        }

        RaiseEventAsync(UserJoinedChannel, new UserJoinedChannelEvent(message, nick, user, host, channel));

        // Fire enhanced user joined event
        var enhancedUserJoinedEvent = new EnhancedUserJoinedChannelEvent(message, this, nick, user, host, channel);
        RaiseEventAsync(OnEnhancedUserJoined, enhancedUserJoinedEvent);
    }

    private void HandlePart(IrcMessage message)
    {
        if (message.Parameters.Count == 0 || string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var channel = message.Parameters[0];
        var reason = message.Parameters.Count > 1 ? message.Parameters[1] : null;        // If it's us leaving, remove from our channel list
        if (nick == _currentNick)
        {
            _channels.TryRemove(channel, out _);
            InvalidateChannelsCache();
        }
        else if (_channels.ContainsKey(channel))
        {
            _channels[channel].Remove(nick);
            InvalidateChannelsCache();
        }

        RaiseEventAsync(UserLeftChannel, new UserLeftChannelEvent(message, nick, user, host, channel, reason));
    }

    private void HandleQuit(IrcMessage message)
    {
        if (string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var reason = message.Parameters.Count > 0 ? message.Parameters[0] : null;

        // Remove from all channels
        foreach (var channel in _channels.Values)
        {
            channel.Remove(nick);
        }
        InvalidateChannelsCache();

        // Remove from user info tracking
        _userInfo.TryRemove(nick, out _);

        RaiseEventAsync(UserQuit, new UserQuitEvent(message, nick, user, host, reason));
    }

    private void HandlePrivateMessage(IrcMessage message)
    {
        if (message.Parameters.Count < 2 || string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var target = message.Parameters[0];
        var text = message.Parameters[1]; UpdateUserInfo(nick, user, host);

        // Detect echo-message: if the source nick is ourselves, this is our own echoed message.
        // Must be checked BEFORE CTCP detection — otherwise echoed CTCP requests trigger auto-replies to ourselves.
        var isEcho = _enabledCapabilities.Contains("echo-message") &&
                     string.Equals(nick, _currentNick, StringComparison.OrdinalIgnoreCase);

        // CTCP detection: messages starting with \x01 are CTCP requests.
        // Tolerate missing trailing \x01 (some clients/bouncers omit it).
        if (text.Length >= 2 && text[0] == '\u0001')
        {
            // Skip echoed CTCP — we sent this request ourselves, don't process or auto-reply
            if (isEcho) return;

            var ctcpContent = text[^1] == '\u0001' ? text[1..^1] : text[1..];
            HandleCtcpRequest(message, nick, user, host, target, ctcpContent);
            return; // CTCP messages are not regular PRIVMSGs
        }

        RaiseEventAsync(PrivateMessageReceived, new PrivateMessageEvent(message, nick, user, host, target, text, isEcho));

        // Fire enhanced event
        var enhancedEvent = new EnhancedMessageEvent(message, this, nick, user, host, target, text);
        RaiseEventAsync(OnEnhancedMessage, enhancedEvent);

        // Fire generic event
        if (OnGenericMessage != null)
        {
            SafeFireAndForget(async () => await OnGenericMessage(enhancedEvent).ConfigureAwait(false), "GenericMessageEvent");
        }
    }

    private void HandleNotice(IrcMessage message)
    {
        if (message.Parameters.Count < 2 || string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var target = message.Parameters[0];
        var text = message.Parameters[1]; UpdateUserInfo(nick, user, host);

        // Skip echoed NOTICE — echo-message capability echoes our own CTCP replies back to us
        var isEchoNotice = _enabledCapabilities.Contains("echo-message") &&
                           string.Equals(nick, _currentNick, StringComparison.OrdinalIgnoreCase);

        // CTCP reply detection: NOTICE messages starting with \x01 are CTCP replies.
        // Tolerate missing trailing \x01.
        if (text.Length >= 2 && text[0] == '\u0001')
        {
            // Skip echoed CTCP replies — we sent this reply ourselves
            if (isEchoNotice) return;

            var ctcpContent = text[^1] == '\u0001' ? text[1..^1] : text[1..];
            HandleCtcpReply(message, nick, user, host, ctcpContent);
            return; // CTCP replies are not regular NOTICEs
        }

        // Reactive NickServ identification: detect NickServ asking us to IDENTIFY
        if (!_nickServIdentified
            && !string.IsNullOrEmpty(_options.NickServPassword)
            && nick.Equals("NickServ", StringComparison.OrdinalIgnoreCase)
            && text.Contains("IDENTIFY", StringComparison.OrdinalIgnoreCase))
        {
            _nickServIdentified = true;
            SafeFireAndForget(async () =>
            {
                await SendMessageAsync("NickServ", $"IDENTIFY {_currentNick} {_options.NickServPassword}").ConfigureAwait(false);
                _logger?.LogInformation("Sent NickServ IDENTIFY for {Nick} (triggered by NickServ notice)", _currentNick);
            }, "NickServIdentify");
        }

        RaiseEventAsync(NoticeReceived, new NoticeEvent(message, nick, user, host, target, text));
    }

    private void HandleNickChange(IrcMessage message)
    {
        if (message.Parameters.Count == 0 || string.IsNullOrEmpty(message.Source)) return;

        var (oldNick, user, host) = ParseNickUserHost(message.Source);
        var newNick = message.Parameters[0];

        // Use state lock to prevent race conditions during nick changes
        lock (_stateLock)
        {
            // If it's our nick change
            if (oldNick == _currentNick)
            {
                _currentNick = newNick;
            }

            // Update in all channels atomically
            foreach (var channel in _channels.Values)
            {
                if (channel.Remove(oldNick))
                {
                    channel.Add(newNick);
                }
            }

            // Update user info tracking atomically
            if (_userInfo.TryRemove(oldNick, out var userInfo))
            {
                userInfo.Nick = newNick;
                _userInfo[newNick] = userInfo;
            }

            InvalidateChannelsCache();
        }

        RaiseEventAsync(NickChanged, new NickChangedEvent(message, oldNick, newNick, user, host));
    }

    private void HandleTopic(IrcMessage message)
    {
        if (message.Parameters.Count < 1) return;

        var channel = message.Parameters[0];
        var topic = message.Parameters.Count > 1 ? message.Parameters[1] : null;
        var setBy = string.IsNullOrEmpty(message.Source) ? null : ParseNickUserHost(message.Source).nick;

        RaiseEventAsync(TopicChanged, new TopicChangedEvent(message, channel, topic, setBy));
    }

    private void HandleKick(IrcMessage message)
    {
        if (message.Parameters.Count < 2 || string.IsNullOrEmpty(message.Source)) return;

        var kickedByNick = ParseNickUserHost(message.Source).nick;
        var channel = message.Parameters[0];
        var kickedNick = message.Parameters[1];
        var reason = message.Parameters.Count > 2 ? message.Parameters[2] : null;

        // If it's us being kicked
        if (kickedNick == _currentNick)
        {
            _channels.TryRemove(channel, out _);
            InvalidateChannelsCache();

            // Auto-rejoin if enabled
            if (_options.AutoRejoinOnKick)
            {
                SafeFireAndForget(async () =>
                {
                    await Task.Delay(1000).ConfigureAwait(false); // Wait a second before rejoining
                    await JoinChannelAsync(channel).ConfigureAwait(false);
                }, "AutoRejoinOnKick");
            }
        }
        else if (_channels.ContainsKey(channel))
        {
            _channels[channel].Remove(kickedNick);
            InvalidateChannelsCache();
        }

        RaiseEventAsync(UserKicked, new UserKickedEvent(message, channel, kickedNick, kickedByNick, reason));
    }

    private void HandleInvite(IrcMessage message)
    {
        if (message.Parameters.Count < 2 || string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var channel = message.Parameters[1];

        _logger?.LogInformation("Invited to {Channel} by {Nick}", channel, nick);
        RaiseEventAsync(InviteReceived, new InviteReceivedEvent(message, nick, user, host, channel));
    }

    private void HandleOwnAwayStatus(IrcMessage message)
    {
        var isAway = message.Command == IrcNumericReplies.RPL_NOWAWAY;
        var serverMessage = message.Parameters.Count > 1 ? message.Parameters[1] : string.Empty;

        _logger?.LogDebug("Own away status: {IsAway} - {Message}", isAway, serverMessage);
        RaiseEventAsync(OwnAwayStatusChanged, new OwnAwayStatusChangedEvent(message, isAway, serverMessage));
    }

    private void HandleChannelModeIs(IrcMessage message)
    {
        if (message.Parameters.Count < 2) return;

        var channel = message.Parameters[1];
        var modes = message.Parameters.Count > 2 ? message.Parameters[2] : string.Empty;
        var modeParams = message.Parameters.Count > 3 ? string.Join(" ", message.Parameters.Skip(3)) : string.Empty;

        _logger?.LogDebug("Channel {Channel} modes: {Modes} {ModeParams}", channel, modes, modeParams);
        RaiseEventAsync(ChannelModeIsReceived, new ChannelModeIsEvent(message, channel, modes, modeParams));
    }

    private void HandleWelcome(IrcMessage message)
    {
        _isRegistered = true;
        _reconnectAttempts = 0;

        // Extract our nick from the welcome message
        if (message.Parameters.Count > 0)
        {
            var welcomeTarget = message.Parameters[0];
            if (!string.IsNullOrEmpty(welcomeTarget))
            {
                _currentNick = welcomeTarget;
            }
        }

        // Reset NickServ state for this connection
        _nickServIdentified = false;

        var network = message.Source ?? "Unknown";
        RaiseEventAsync(Connected, new ConnectedEvent(message, network, _currentNick));

        // Fire enhanced connected event
        var enhancedConnectedEvent = new EnhancedConnectedEvent(message, this, network, _currentNick, _options.Server, "Connected successfully");
        RaiseEventAsync(OnEnhancedConnected, enhancedConnectedEvent);
    }

    private void HandleNamesReply(IrcMessage message)
    {
        if (message.Parameters.Count < 4) return;

        var channel = message.Parameters[2];
        var userList = message.Parameters[3]; if (!_channels.ContainsKey(channel))
            _channels[channel] = new ConcurrentHashSet<string>();

        var users = ParseChannelUsers(userList);
        foreach (var user in users)
        {
            _channels[channel].Add(user.Nick);
        }
        InvalidateChannelsCache();
    }

    private void HandleEndOfNames(IrcMessage message)
    {
        if (message.Parameters.Count < 2) return;

        var channel = message.Parameters[1];
        if (_channels.ContainsKey(channel))
        {
            var users = _channels[channel].Select(nick => new ChannelUser { Nick = nick }).ToList();
            RaiseEventAsync(ChannelUsersReceived, new ChannelUsersEvent(message, channel, users));
        }
    }

    private void HandleTopicReply(IrcMessage message)
    {
        if (message.Parameters.Count < 2) return;

        var channel = message.Parameters[1];
        // RPL_NOTOPIC (331): no topic set — fire with empty string
        // RPL_TOPIC (332): topic text is in parameter 2
        var topic = message.Command == IrcNumericReplies.RPL_NOTOPIC
            ? string.Empty
            : (message.Parameters.Count > 2 ? message.Parameters[2] : string.Empty);

        RaiseEventAsync(TopicChanged, new TopicChangedEvent(message, channel, topic));
    }

    private async Task HandleNicknameInUseAsync(IrcMessage message)
    {
        if (!_isRegistered && _options.AlternativeNicks.Count > 0)
        {
            // Use more efficient IndexOf on the list directly
            var currentIndex = _options.AlternativeNicks.IndexOf(_currentNick);
            if (currentIndex >= 0 && currentIndex < _options.AlternativeNicks.Count - 1)
            {
                _currentNick = _options.AlternativeNicks[currentIndex + 1];
                await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
            }
            else
            {
                // All alternative nicks exhausted, try adding a timestamp-based suffix for uniqueness
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 10000;
                _currentNick = $"{_options.Nick}{timestamp}";
                await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
            }
        }
    }

    private async Task HandleNicknameCollisionAsync(IrcMessage message)
    {
        // Extract the colliding nickname from the message parameters
        // Format: ":server 436 * badnick :Nickname collision KILL from user@host"
        var collidingNick = message.Parameters.Count > 1 ? message.Parameters[1] : _currentNick;
        var errorMessage = message.Parameters.Count > 2 ? message.Parameters[2] : "Nickname collision";

        _logger?.LogWarning("Nickname collision detected for '{Nick}': {Error}", collidingNick, errorMessage);

        string? fallbackNick = null;

        // During connection registration, try fallback strategies
        if (!_isRegistered)
        {
            // First, try alternative nicknames if we have them
            if (_options.AlternativeNicks.Count > 0)
            {
                var currentIndex = _options.AlternativeNicks.IndexOf(_currentNick);
                if (currentIndex >= 0 && currentIndex < _options.AlternativeNicks.Count - 1)
                {
                    fallbackNick = _options.AlternativeNicks[currentIndex + 1];
                    _currentNick = fallbackNick;
                    await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
                    _logger?.LogInformation("Trying alternative nickname '{Nick}' after collision", _currentNick);
                }
                else
                {
                    // All alternative nicks exhausted, generate a unique nick
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 10000;
                    var random = new Random().Next(10, 99);
                    fallbackNick = $"{_options.Nick}_{timestamp}_{random}";
                    _currentNick = fallbackNick;
                    await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
                    _logger?.LogInformation("Generated unique nickname '{Nick}' after collision", _currentNick);
                }
            }
            else
            {
                // No alternative nicks configured, generate a unique nick based on the original
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 10000;
                var random = new Random().Next(10, 99);
                fallbackNick = $"{_options.Nick}_{timestamp}_{random}";
                _currentNick = fallbackNick;
                await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
                _logger?.LogInformation("Generated unique nickname '{Nick}' after collision", _currentNick);
            }
        }
        else
        {
            // Already registered - nickname collision during runtime
            // This is more serious and typically means the server is forcing a nick change
            _logger?.LogError("Nickname collision occurred for registered user '{Nick}' - connection may be unstable", collidingNick);

            // Try to change to a safe nickname
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 10000;
            var random = new Random().Next(10, 99);
            fallbackNick = $"Guest_{timestamp}_{random}";
            _currentNick = fallbackNick;

            try
            {
                await SendRawAsync($"NICK {_currentNick}").ConfigureAwait(false);
                _logger?.LogInformation("Changed to emergency nickname '{Nick}' after collision", _currentNick);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to change nickname after collision");
                // In severe cases, this might require disconnection and reconnection
            }
        }

        // Raise the nickname collision event
        RaiseEventAsync(NicknameCollision, new NicknameCollisionEvent(message, collidingNick, collidingNick, fallbackNick, _isRegistered));
    }

    private static (string nick, string user, string host) ParseNickUserHost(string source)
    {
        var match = NickUserHostRegex.Match(source);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        }
        return (source, "", "");
    }

    private List<ChannelUser> ParseChannelUsers(string userList)
    {
        var users = new List<ChannelUser>();
        var userEntries = userList.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Use server-advertised prefixes from ISUPPORT, fall back to common defaults
        var knownPrefixes = _isupportParser.ChannelModePrefix.Prefixes;
        if (string.IsNullOrEmpty(knownPrefixes))
            knownPrefixes = "~&@%+";

        foreach (var entry in userEntries)
        {
            var user = new ChannelUser();
            var nick = entry;

            // Extract prefixes using server-advertised PREFIX
            while (!string.IsNullOrEmpty(nick) && knownPrefixes.Contains(nick[0]))
            {
                user.Prefixes.Add(nick[0]);
                nick = nick.Substring(1);
            }

            user.Nick = nick;
            users.Add(user);
        }

        return users;
    }

    private void SendPing(object? state)
    {
        try
        {
            // Use state lock to safely check connection state and ensure atomicity
            bool isConnected;
            bool shouldDisconnect = false;

            lock (_stateLock)
            {
                isConnected = _isConnected;

                // Check if we've received a pong recently while holding the lock
                if (isConnected && DateTimeOffset.UtcNow - _lastPongReceived > TimeSpan.FromMilliseconds(_options.PingTimeoutMs))
                {
                    _logger?.LogWarning("Ping timeout, disconnecting");
                    shouldDisconnect = true;
                    isConnected = false; // Prevent sending ping
                }
            }

            if (shouldDisconnect)
            {
                SafeFireAndForget(() => DisconnectInternalAsync("Ping timeout"), "PingTimeoutDisconnect");
                return;
            }

            if (!isConnected) return;
            var pingToken = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            SafeFireAndForget(async () =>
            {
                // Double-check connection state before sending
                if (_isConnected)
                {
                    await SendRawAsync($"PING :{pingToken}").ConfigureAwait(false);
                }
            }, "PingTimerSend");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in ping timer");
        }
    }

    private async Task AttemptReconnectAsync()
    {
        if (_options.MaxReconnectAttempts > 0 && _reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            _logger?.LogWarning("Maximum reconnection attempts reached");
            return;
        }

        _reconnectAttempts++;
        var delay = Math.Min(_options.ReconnectDelayMs * _reconnectAttempts, _options.MaxReconnectDelayMs);

        _logger?.LogInformation("Attempting to reconnect in {Delay}ms (attempt {Attempt})", delay, _reconnectAttempts);
        await Task.Delay(delay).ConfigureAwait(false);

        try
        {
            await ConnectAsync().ConfigureAwait(false);
            _logger?.LogInformation("Reconnection successful");

            // Rejoin channels that were active before disconnect
            var channelsToRejoin = _channelsToRejoin;
            _channelsToRejoin = new();
            foreach (var channel in channelsToRejoin)
            {
                try
                {
                    await JoinChannelAsync(channel).ConfigureAwait(false);
                }
                catch (Exception joinEx)
                {
                    _logger?.LogWarning(joinEx, "Failed to rejoin channel {Channel} after reconnect", channel);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Reconnection failed");
            // Use a small delay before scheduling next attempt to prevent tight loops
            if (_reconnectAttempts < _options.MaxReconnectAttempts || _options.MaxReconnectAttempts <= 0)
            {
                SafeFireAndForget(async () =>
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    await AttemptReconnectAsync().ConfigureAwait(false);
                }, "ReconnectRetry");
            }
        }
    }

    // CTCP send methods

    /// <summary>
    /// Sends a CTCP ACTION to a channel or user, displayed as <c>* nick actionText</c> (the <c>/me</c> command).
    /// </summary>
    /// <param name="target">Channel name (e.g. <c>"#general"</c>) or nickname to send the action to.</param>
    /// <param name="actionText">The action description (e.g. <c>"waves hello"</c>). Must not contain newlines.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> or <paramref name="actionText"/> is null/whitespace or contains newlines.</exception>
    public async Task SendActionAsync(string target, string actionText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(actionText, nameof(actionText));

        if (actionText.Contains('\r') || actionText.Contains('\n'))
            throw new ArgumentException("Action text must not contain newline characters.", nameof(actionText));
        if (target.Contains('\r') || target.Contains('\n'))
            throw new ArgumentException("Target must not contain newline characters.", nameof(target));

        await SendRawAsync($"PRIVMSG {target} :\u0001ACTION {actionText}\u0001").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a CTCP request to a user. The remote client's reply (if any) arrives via the <see cref="CtcpReplyReceived"/> event.
    /// </summary>
    /// <param name="target">Nickname of the user to query (not a channel).</param>
    /// <param name="command">The CTCP command to send (e.g. <c>"VERSION"</c>, <c>"PING"</c>, <c>"TIME"</c>, <c>"CLIENTINFO"</c>).</param>
    /// <param name="parameters">Optional command-specific parameters (e.g. a timestamp for PING). Must not contain newlines.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> or <paramref name="command"/> is null/whitespace, or parameters contain newlines.</exception>
    public async Task SendCtcpRequestAsync(string target, string command, string? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(command, nameof(command));

        if (target.Contains('\r') || target.Contains('\n'))
            throw new ArgumentException("Target must not contain newline characters.", nameof(target));
        if (parameters != null && (parameters.Contains('\r') || parameters.Contains('\n')))
            throw new ArgumentException("Parameters must not contain newline characters.", nameof(parameters));

        var ctcpContent = string.IsNullOrEmpty(parameters) ? command : $"{command} {parameters}";
        await SendRawAsync($"PRIVMSG {target} :\u0001{ctcpContent}\u0001").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a CTCP reply via NOTICE to a user (in response to a received <see cref="CtcpRequestReceived"/> event).
    /// The auto-reply system calls this automatically for standard commands when <see cref="Configuration.IrcClientOptions.EnableCtcpAutoReply"/> is enabled.
    /// </summary>
    /// <param name="target">Nickname of the user who sent the original CTCP request.</param>
    /// <param name="command">The CTCP command being replied to (e.g. <c>"VERSION"</c>, <c>"PING"</c>).</param>
    /// <param name="replyText">The reply payload (e.g. <c>"IRCDotNet.Core 2.0.2.0"</c> for VERSION, or the echoed timestamp for PING). <c>null</c> to send only the command name.</param>
    public async Task SendCtcpReplyAsync(string target, string command, string? replyText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(command, nameof(command));

        var ctcpContent = string.IsNullOrEmpty(replyText) ? command : $"{command} {replyText}";
        await SendNoticeAsync(target, $"\u0001{ctcpContent}\u0001").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a PRIVMSG with optional IRCv3 message tags.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="message">The message text.</param>
    /// <param name="tags">Optional IRCv3 tags to attach to the message.</param>
    public async Task SendMessageWithTagsAsync(string target, string message, Dictionary<string, string?>? tags = null)
    {
        var ircMessage = new IrcMessage
        {
            Command = IrcCommands.PRIVMSG,
            Parameters = { target, message }
        };

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                ircMessage.Tags[tag.Key] = tag.Value;
            }
        }

        await SendRawAsync(ircMessage.Serialize()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a TAGMSG (tag-only message with no text body). Requires the message-tags capability.
    /// </summary>
    /// <param name="target">Nickname or channel name to send to.</param>
    /// <param name="tags">IRCv3 tags to send.</param>
    /// <exception cref="InvalidOperationException">The message-tags capability is not enabled.</exception>
    public async Task SendTagMessageAsync(string target, Dictionary<string, string?> tags)
    {
        if (!_enabledCapabilities.Contains(IrcCapabilities.MESSAGE_TAGS))
        {
            throw new InvalidOperationException("message-tags capability not enabled");
        }

        var ircMessage = new IrcMessage
        {
            Command = IrcCommands.TAGMSG,
            Parameters = { target }
        };

        foreach (var tag in tags)
        {
            ircMessage.Tags[tag.Key] = tag.Value;
        }

        await SendRawAsync(ircMessage.Serialize()).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a nickname to the server-side MONITOR list for online/offline notifications. Requires the monitor capability.
    /// </summary>
    /// <param name="nick">Nickname to monitor.</param>
    /// <exception cref="InvalidOperationException">The monitor capability is not enabled.</exception>
    public async Task MonitorNickAsync(string nick)
    {
        if (!_enabledCapabilities.Contains(IrcCapabilities.MONITOR))
        {
            throw new InvalidOperationException("monitor capability not enabled");
        }

        await SendRawAsync($"MONITOR + {nick}").ConfigureAwait(false);
        _monitoredNicks.Add(nick);
    }

    /// <summary>
    /// Removes a nickname from the server-side MONITOR list. Requires the monitor capability.
    /// </summary>
    /// <param name="nick">Nickname to stop monitoring.</param>
    /// <exception cref="InvalidOperationException">The monitor capability is not enabled.</exception>
    public async Task UnmonitorNickAsync(string nick)
    {
        if (!_enabledCapabilities.Contains(IrcCapabilities.MONITOR))
        {
            throw new InvalidOperationException("monitor capability not enabled");
        }

        await SendRawAsync($"MONITOR - {nick}").ConfigureAwait(false);
        _monitoredNicks.Remove(nick);
    }

    /// <summary>
    /// Changes the client's real name (GECOS) without reconnecting. Requires the setname capability.
    /// </summary>
    /// <param name="realName">The new real name.</param>
    /// <exception cref="InvalidOperationException">The setname capability is not enabled.</exception>
    public async Task SetRealNameAsync(string realName)
    {
        if (!_enabledCapabilities.Contains(IrcCapabilities.SETNAME))
        {
            throw new InvalidOperationException("setname capability not enabled");
        }

        await SendRawAsync($"SETNAME :{realName}").ConfigureAwait(false);
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return; // Thread-safe disposal check

        try
        {
            // Don't block - just signal cancellation and cleanup synchronously
            _cancellationTokenSource?.Cancel();

            // Clean up rate limiter buckets to prevent memory leaks
            _rateLimiter?.Cleanup(TimeSpan.FromHours(1));

            // Dispose synchronous resources only
            _pingTimer?.Dispose();
            _pingTimer = null;

            // Dispose network resources to prevent leaks when sync Dispose is called
            try
            {
                _transport?.Dispose();
                _transport = null;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Suppressed exception during network resource cleanup");
            }

            _isConnected = false;

            _sendLock?.Dispose();
            _connectLock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during dispose");
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return; // Thread-safe disposal check

        try
        {
            await DisconnectInternalAsync("Client disposed").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during async dispose");
        }
        finally
        {
            _sendLock?.Dispose();
            _connectLock?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Gets the configuration options used to create this client.
    /// </summary>
    public IrcClientOptions Configuration => _options;

    /// <summary>
    /// Safely raise an event without blocking the IRC processing loop
    /// </summary>
    private void RaiseEventAsync<T>(EventHandler<T>? eventHandler, T eventArgs) where T : IrcEvent
    {
        if (eventHandler == null) return;

        // Fire events asynchronously to prevent blocking the read loop
        SafeFireAndForget(() =>
        {
            // Take a snapshot of the invocation list to prevent race conditions
            var handlers = eventHandler.GetInvocationList().Cast<EventHandler<T>>().ToArray();

            // Invoke each handler individually to ensure exceptions in one handler don't stop others
            foreach (var handler in handlers)
            {
                try
                {
                    handler.Invoke(this, eventArgs);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in event handler for {EventType}", typeof(T).Name);
                }
            }
            return Task.CompletedTask;
        }, $"EventHandler_{typeof(T).Name}");
    }

    /// <summary>
    /// Safely execute a fire-and-forget task with proper error handling
    /// </summary>
    private void SafeFireAndForget(Func<Task> taskFactory, string operationName)
    {
        Utilities.TaskExtensions.SafeFireAndForget(taskFactory, _logger, operationName);
    }

    private void ProcessMessageTags(IrcMessage message)
    {        // Fire message tags event for interested listeners
        RaiseEventAsync(MessageTagsReceived, new MessageTagsEvent(message));

        // Process server-time tag
        if (message.Tags.TryGetValue(MessageTags.TIME, out var timeValue) && !string.IsNullOrEmpty(timeValue))
        {
            if (DateTimeOffset.TryParse(timeValue, out var serverTime))
            {
                // Update message timestamp if server-time is available
                // This could be used for proper message ordering
            }
        }

        // Process account tag for account-notify
        if (message.Tags.TryGetValue(MessageTags.ACCOUNT, out var account) && !string.IsNullOrEmpty(message.Source))
        {
            var (nick, user, host) = ParseNickUserHost(message.Source);
            UpdateUserInfo(nick, user, host, account);
        }
    }

    private void HandleAwayChange(IrcMessage message)
    {
        if (string.IsNullOrEmpty(message.Source)) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var awayMessage = message.Parameters.Count > 0 ? message.Parameters[0] : null;
        var isAway = !string.IsNullOrEmpty(awayMessage); UpdateUserInfo(nick, user, host, awayMessage: awayMessage, isAway: isAway);
        RaiseEventAsync(UserAwayStatusChanged, new UserAwayStatusChangedEvent(message, nick, user, host, isAway, awayMessage));
    }

    private void HandleAccountChange(IrcMessage message)
    {
        if (string.IsNullOrEmpty(message.Source) || message.Parameters.Count == 0) return;

        var (nick, user, host) = ParseNickUserHost(message.Source);
        var account = message.Parameters[0] == "*" ? null : message.Parameters[0]; UpdateUserInfo(nick, user, host, account);
        RaiseEventAsync(UserAccountChanged, new UserAccountChangedEvent(message, nick, user, host, account));
    }

    private void HandleHostnameChange(IrcMessage message)
    {
        if (string.IsNullOrEmpty(message.Source) || message.Parameters.Count < 2) return;

        var (nick, oldUser, oldHost) = ParseNickUserHost(message.Source);
        var newUser = message.Parameters[0];
        var newHost = message.Parameters[1]; UpdateUserInfo(nick, newUser, newHost);
        RaiseEventAsync(UserHostnameChanged, new UserHostnameChangedEvent(message, nick, oldUser, oldHost, newUser, newHost));
    }

    private void HandleBatch(IrcMessage message)
    {
        if (message.Parameters.Count == 0) return;

        var batchId = message.Parameters[0];
        var isStarting = batchId.StartsWith('+');
        batchId = batchId.TrimStart('+', '-');

        string? batchType = null;
        var parameters = new List<string>();

        if (isStarting && message.Parameters.Count > 1)
        {
            batchType = message.Parameters[1];
            parameters = message.Parameters.Skip(2).ToList();
        }

        RaiseEventAsync(BatchReceived, new BatchEvent(message, batchId, batchType, parameters, isStarting));
    }

    private void HandleTagMessage(IrcMessage message)
    {
        // TAGMSG is just for tags, no content - process tags and fire appropriate events
        if (message.Tags.Count > 0)
        {
            ProcessMessageTags(message);
        }
    }

    private void HandleCtcpRequest(IrcMessage message, string nick, string user, string host, string target, string ctcpContent)
    {
        // Parse "COMMAND params" from the content between \x01 delimiters
        var spaceIndex = ctcpContent.IndexOf(' ');
        var command = spaceIndex == -1 ? ctcpContent : ctcpContent[..spaceIndex];
        var parameters = spaceIndex == -1 ? string.Empty : ctcpContent[(spaceIndex + 1)..];
        command = command.ToUpperInvariant();

        _logger?.LogDebug("CTCP {Command} request from {Nick} (target: {Target})", command, nick, target);

        // ACTION is special — it's displayed as "* nick action-text", not as a CTCP dialog
        if (command == "ACTION")
        {
            RaiseEventAsync(CtcpActionReceived, new CtcpActionEvent(message, nick, user, host, target, parameters));
            return;
        }

        // Raise the generic CTCP request event
        RaiseEventAsync(CtcpRequestReceived, new CtcpRequestEvent(message, nick, user, host, target, command, parameters));

        // Auto-reply to standard CTCP commands
        if (_options.EnableCtcpAutoReply)
        {
            switch (command)
            {
                case "VERSION":
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "VERSION", _options.CtcpVersionString), "CtcpVersionReply");
                    break;
                case "PING":
                    // Echo the parameter back for latency measurement
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "PING", parameters), "CtcpPingReply");
                    break;
                case "TIME":
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "TIME", DateTimeOffset.Now.ToString("ddd MMM dd HH:mm:ss yyyy")), "CtcpTimeReply");
                    break;
                case "CLIENTINFO":
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "CLIENTINFO", "ACTION VERSION PING TIME CLIENTINFO FINGER SOURCE USERINFO"), "CtcpClientInfoReply");
                    break;
                case "FINGER":
                    // FINGER traditionally returns user + idle time. We reply with the configured real name.
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "FINGER", _options.RealName), "CtcpFingerReply");
                    break;
                case "SOURCE":
                    // SOURCE returns where the client source code can be obtained.
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "SOURCE", "https://github.com/etushar89/IRCDotNet.Core"), "CtcpSourceReply");
                    break;
                case "USERINFO":
                    // USERINFO returns a user-settable info string. We use the real name.
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "USERINFO", _options.RealName), "CtcpUserInfoReply");
                    break;
                case "ERRMSG":
                    // ERRMSG echoes the query back with an error description.
                    SafeFireAndForget(() => SendCtcpReplyAsync(nick, "ERRMSG", $"{parameters} :No error"), "CtcpErrMsgReply");
                    break;
            }
        }
    }

    private void HandleCtcpReply(IrcMessage message, string nick, string user, string host, string ctcpContent)
    {
        var spaceIndex = ctcpContent.IndexOf(' ');
        var command = spaceIndex == -1 ? ctcpContent : ctcpContent[..spaceIndex];
        var replyText = spaceIndex == -1 ? string.Empty : ctcpContent[(spaceIndex + 1)..];
        command = command.ToUpperInvariant();

        _logger?.LogDebug("CTCP {Command} reply from {Nick}: {Reply}", command, nick, replyText);

        RaiseEventAsync(CtcpReplyReceived, new CtcpReplyEvent(message, nick, user, host, command, replyText));
    }

    private void UpdateUserInfo(string nick, string? user = null, string? host = null, string? account = null, string? realName = null, string? awayMessage = null, bool? isAway = null)
    {
        // Use GetOrAdd pattern to prevent race conditions
        var userInfo = _userInfo.GetOrAdd(nick, _ => new UserInfo { Nick = nick });

        // Use lock for atomic updates of multiple properties
        lock (userInfo)
        {
            if (user != null) userInfo.User = user;
            if (host != null) userInfo.Host = host;
            if (account != null) userInfo.Account = account;
            if (realName != null) userInfo.RealName = realName;
            if (awayMessage != null) userInfo.AwayMessage = awayMessage;
            if (isAway.HasValue) userInfo.IsAway = isAway.Value;
            userInfo.LastSeen = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Invalidate cached collections when data changes
    /// </summary>
    private void InvalidateChannelsCache()
    {
        lock (_stateLock)
        {
            _channelsCacheValid = false;
            _cachedChannels = null;
        }
    }

    private void InvalidateCapabilitiesCache()
    {
        lock (_stateLock)
        {
            _cachedEnabledCapabilities = null;
            _capabilitiesCacheValid = false;
        }
    }

    // RFC 1459 Compliance Handler Methods

    private void HandleServerInfo(IrcMessage message)
    {
        // Handle RPL_YOURHOST (002), RPL_CREATED (003), RPL_MYINFO (004)
        // These contain server information that clients may want to display
        if (message.Parameters.Count > 1)
        {
            _logger?.LogDebug("Server info ({Command}): {Info}", message.Command, message.Parameters[1]);
            // Could raise a ServerInfoReceived event if needed in the future
        }
    }

    private void HandleServerSupport(IrcMessage message)
    {
        // Handle RPL_ISUPPORT (005) - server capability/feature information
        // This is critical for understanding server limitations and features
        if (message.Parameters.Count > 1)
        {
            // Use the enhanced IsupportParser instead of basic parsing
            _isupportParser.ParseIsupport(message);

            // Log the parsed network information
            if (!string.IsNullOrEmpty(_isupportParser.NetworkName))
            {
                _logger?.LogTrace("Connected to network: {Network}", _isupportParser.NetworkName);
            }

            // Log important server limits
            _logger?.LogTrace("Server limits - MaxNickLen: {MaxNick}, MaxChannelLen: {MaxChannel}, MaxChannels: {MaxChannels}",
                _isupportParser.MaxNicknameLength, _isupportParser.MaxChannelLength, _isupportParser.MaxChannels);

            // Log supported channel types and prefixes
            _logger?.LogTrace("Channel types: {Types}, Mode prefixes: {Prefixes}",
                _isupportParser.ChannelTypes, _isupportParser.ChannelModePrefix.Prefixes);
        }
    }

    private void HandleMotd(IrcMessage message)
    {
        // Handle MOTD-related numerics: RPL_MOTDSTART (375), RPL_MOTD (372), RPL_ENDOFMOTD (376), ERR_NOMOTD (422)
        switch (message.Command)
        {
            case IrcNumericReplies.RPL_MOTDSTART:
                _logger?.LogDebug("MOTD start received");
                break;
            case IrcNumericReplies.RPL_MOTD:
                if (message.Parameters.Count > 1)
                    _logger?.LogDebug("MOTD: {Line}", message.Parameters[1]);
                break;
            case IrcNumericReplies.RPL_ENDOFMOTD:
                _logger?.LogDebug("MOTD end received");
                break;
            case IrcNumericReplies.ERR_NOMOTD:
                _logger?.LogDebug("No MOTD available");
                break;
        }
    }

    private void HandleAwayReply(IrcMessage message)
    {
        // Handle RPL_AWAY (301) - user is away
        if (message.Parameters.Count >= 3)
        {
            var nick = message.Parameters[1];
            var awayMessage = message.Parameters[2];
            _logger?.LogDebug("User {Nick} is away: {Message}", nick, awayMessage);

            // Update user info with away status
            UpdateUserInfo(nick, awayMessage: awayMessage, isAway: true);
        }
    }

    private void HandleWhoisReply(IrcMessage message)
    {
        // Handle various WHOIS replies
        if (message.Parameters.Count < 2) return;

        var nick = message.Parameters[1];

        switch (message.Command)
        {
            case IrcNumericReplies.RPL_WHOISUSER:
                // Format: <nick> <user> <host> * :<real name>
                if (message.Parameters.Count >= 6)
                {
                    var user = message.Parameters[2];
                    var host = message.Parameters[3];
                    var realName = message.Parameters[5];
                    UpdateUserInfo(nick, user, host, realName: realName);
                    _logger?.LogDebug("WHOIS {Nick}: {User}@{Host} ({RealName})", nick, user, host, realName);
                }
                break;
            case IrcNumericReplies.RPL_WHOISSERVER:
                // Format: <nick> <server> :<server info>
                if (message.Parameters.Count >= 4)
                {
                    _logger?.LogDebug("User {Nick} is on server {Server}: {Info}", nick, message.Parameters[2], message.Parameters[3]);
                }
                break;
            case IrcNumericReplies.RPL_WHOISOPERATOR:
                _logger?.LogDebug("User {Nick} is an IRC operator", nick);
                break;
            case IrcNumericReplies.RPL_WHOISIDLE:
                // Format: <nick> <integer> :seconds idle
                if (message.Parameters.Count >= 3)
                {
                    _logger?.LogDebug("User {Nick} idle: {Seconds} seconds", nick, message.Parameters[2]);
                }
                break;
            case IrcNumericReplies.RPL_WHOISCHANNELS:
                // Format: <nick> :*( ( "@" / "+" ) <channel> " " )
                if (message.Parameters.Count >= 3)
                {
                    _logger?.LogDebug("User {Nick} channels: {Channels}", nick, message.Parameters[2]);
                }
                break;
            case IrcNumericReplies.RPL_ENDOFWHOIS:
                _logger?.LogDebug("End of WHOIS for {Nick}", nick);
                break;
        }
    }

    private void HandleErrorReply(IrcMessage message)
    {
        // Handle various error replies for better protocol compliance
        var errorMessage = message.Parameters.Count > 1 ? message.Parameters[1] : "Unknown error";
        var reason = message.Parameters.Count > 2 ? message.Parameters[2] : errorMessage;

        switch (message.Command)
        {
            case IrcNumericReplies.ERR_NOSUCHNICK:
                _logger?.LogWarning("No such nick: {Error}", errorMessage);
                break;
            case IrcNumericReplies.ERR_NOSUCHSERVER:
                _logger?.LogWarning("No such server: {Error}", errorMessage);
                break;
            case IrcNumericReplies.ERR_NOSUCHCHANNEL:
                _logger?.LogWarning("No such channel: {Error}", errorMessage);
                break;
            case IrcNumericReplies.ERR_CANNOTSENDTOCHAN:
                _logger?.LogWarning("Cannot send to channel: {Error}", errorMessage);
                break;
            case IrcNumericReplies.ERR_UNKNOWNCOMMAND:
                _logger?.LogWarning("Unknown command: {Error}", errorMessage);
                break;
            case IrcNumericReplies.ERR_CHANNELISFULL:
            case IrcNumericReplies.ERR_INVITEONLYCHAN:
            case IrcNumericReplies.ERR_BANNEDFROMCHAN:
            case IrcNumericReplies.ERR_BADCHANNELKEY:
            case IrcNumericReplies.ERR_SSL_REQUIRED:
            case IrcNumericReplies.ERR_NOCHANMODES: // 477 — "You need to be identified with services" on Libera/Solanum
            case IrcNumericReplies.ERR_TOOMANYCHANNELS: // 405
                _logger?.LogWarning("Cannot join channel {Channel}: {Reason}", errorMessage, reason);
                _channels.TryRemove(errorMessage, out _);
                RaiseEventAsync(ChannelJoinFailed, new ChannelJoinFailedEvent(message, errorMessage, reason, message.Command));
                break;
            case IrcNumericReplies.ERR_CHANOPRIVSNEEDED:
                _logger?.LogWarning("Not channel operator for {Channel}: {Reason}", errorMessage, reason);
                break;
            case IrcNumericReplies.ERR_NOTONCHANNEL:
                _logger?.LogWarning("Not on channel {Channel}: {Reason}", errorMessage, reason);
                break;
            case IrcNumericReplies.ERR_NEEDMOREPARAMS:
                _logger?.LogWarning("Need more parameters: {Error}", errorMessage);
                break;
        }

        // Always fire the general error event so library consumers can handle any error
        RaiseEventAsync(ErrorReplyReceived, new ErrorReplyEvent(message, message.Command, errorMessage, reason));
    }

    private void HandleMode(IrcMessage message)
    {
        // Handle MODE command for both user and channel modes
        if (message.Parameters.Count < 2) return;

        var target = message.Parameters[0];
        var modes = message.Parameters[1];
        var modeParams = message.Parameters.Skip(2).ToArray();

        var source = string.IsNullOrEmpty(message.Source) ? null : ParseNickUserHost(message.Source).nick;

        if (target.StartsWith("#") || target.StartsWith("&"))
        {
            // Channel mode change
            _logger?.LogDebug("Channel mode change on {Channel}: {Modes} by {Source}", target, modes, source);
        }
        else
        {
            // User mode change
            _logger?.LogDebug("User mode change for {User}: {Modes} by {Source}", target, modes, source);

            // If it's our own mode change, we might want to track it
            if (target == _currentNick)
            {
                _logger?.LogInformation("Our user modes changed: {Modes}", modes);
            }
        }
    }

    private void HandleWho(IrcMessage message)
    {
        // Handle WHO command responses (RPL_WHOREPLY 352 and RPL_ENDOFWHO 315)
        switch (message.Command)
        {
            case IrcNumericReplies.RPL_WHOREPLY:
                // Format: <channel> <user> <host> <server> <nick> <H|G>[*][@|+] :<hopcount> <real name>
                if (message.Parameters.Count >= 8)
                {
                    var channel = message.Parameters[1];
                    var user = message.Parameters[2];
                    var host = message.Parameters[3];
                    var server = message.Parameters[4];
                    var nick = message.Parameters[5];
                    var flags = message.Parameters[6];
                    var realNameWithHops = message.Parameters[7];

                    // Update user info
                    var isAway = flags.Contains('G'); // G = gone (away), H = here
                    var realName = realNameWithHops.Contains(' ') ? realNameWithHops.Substring(realNameWithHops.IndexOf(' ') + 1) : realNameWithHops;

                    UpdateUserInfo(nick, user, host, realName: realName, isAway: isAway);
                    _logger?.LogDebug("WHO reply for {Nick}: {User}@{Host} on {Channel} ({Flags})", nick, user, host, channel, flags);

                    // Raise event
                    RaiseEventAsync(WhoReceived, new WhoReceivedEvent(message, channel, user, host, server, nick, flags, realName, isAway));
                }
                break;

            case IrcNumericReplies.RPL_ENDOFWHO:
                if (message.Parameters.Count >= 2)
                {
                    var target = message.Parameters[1];
                    _logger?.LogDebug("End of WHO for {Target}", target);
                }
                break;

            default:
                _logger?.LogDebug("WHO response received: {Command} with {ParamCount} parameters", message.Command, message.Parameters.Count);
                break;
        }
    }

    private void HandleWhoWas(IrcMessage message)
    {
        // Handle WHOWAS command responses
        switch (message.Command)
        {
            case IrcNumericReplies.RPL_WHOWASUSER:
                // Format: <nick> <user> <host> * :<real name>
                if (message.Parameters.Count >= 6)
                {
                    var nick = message.Parameters[1];
                    var user = message.Parameters[2];
                    var host = message.Parameters[3];
                    var realName = message.Parameters[5];

                    _logger?.LogDebug("WHOWAS {Nick}: {User}@{Host} ({RealName})", nick, user, host, realName);

                    // Raise event
                    RaiseEventAsync(WhoWasReceived, new WhoWasReceivedEvent(message, nick, user, host, realName));
                }
                break;

            case IrcNumericReplies.RPL_ENDOFWHOWAS:
                if (message.Parameters.Count >= 2)
                {
                    var nick = message.Parameters[1];
                    _logger?.LogDebug("End of WHOWAS for {Nick}", nick);
                }
                break;

            default:
                _logger?.LogDebug("WHOWAS response received: {Command} with {ParamCount} parameters", message.Command, message.Parameters.Count);
                break;
        }
    }

    private void HandleList(IrcMessage message)
    {
        // Handle LIST command responses (RPL_LIST 322 and RPL_LISTEND 323)
        switch (message.Command)
        {
            case IrcNumericReplies.RPL_LIST:
                // Format: <channel> <# visible> :<topic>
                if (message.Parameters.Count >= 4)
                {
                    var channel = message.Parameters[1];
                    var userCountStr = message.Parameters[2];
                    var topic = message.Parameters[3];

                    if (int.TryParse(userCountStr, out var userCount))
                    {
                        _logger?.LogDebug("LIST: {Channel} ({Users} users) - {Topic}", channel, userCount, topic);

                        // Raise event
                        RaiseEventAsync(ChannelListReceived, new ChannelListReceivedEvent(message, channel, userCount, topic));
                    }
                }
                break;

            case IrcNumericReplies.RPL_LISTEND:
                _logger?.LogDebug("End of channel list");
                RaiseEventAsync(ChannelListEndReceived, new ChannelListEndEvent(message));
                break;

            default:
                _logger?.LogDebug("LIST response received: {Command} with {ParamCount} parameters", message.Command, message.Parameters.Count);
                break;
        }
    }

    // RFC 1459 Additional Commands

    /// <summary>
    /// Sends a WHO query to retrieve information about users matching a target.
    /// </summary>
    /// <param name="target">A channel name, nickname, or mask to query.</param>
    public async Task WhoAsync(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        await SendRawAsync($"WHO {target}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a WHOWAS query for a user who has disconnected.
    /// </summary>
    /// <param name="nick">Nickname to look up.</param>
    public async Task WhoWasAsync(string nick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick, nameof(nick));
        await SendRawAsync($"WHOWAS {nick}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a LIST command to retrieve available channels from the server.
    /// </summary>
    /// <param name="pattern">Optional filter pattern (e.g. "#test*"). If <c>null</c>, lists all channels.</param>
    public async Task ListChannelsAsync(string? pattern = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            await SendRawAsync("LIST").ConfigureAwait(false);
        }
        else
        {
            await SendRawAsync($"LIST {pattern}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sets user modes for the current user.
    /// </summary>
    /// <param name="modes">Mode string (e.g. "+i" for invisible).</param>
    public async Task SetUserModeAsync(string modes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modes, nameof(modes));
        await SendRawAsync($"MODE {_currentNick} {modes}").ConfigureAwait(false);
    }

    /// <summary>
    /// Sets channel modes (requires appropriate privileges).
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <param name="modes">Mode string (e.g. "+o" to grant operator).</param>
    /// <param name="parameters">Optional mode parameters (e.g. nickname for +o).</param>
    public async Task SetChannelModeAsync(string channel, string modes, params string[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        ArgumentException.ThrowIfNullOrWhiteSpace(modes, nameof(modes));

        var command = $"MODE {channel} {modes}";
        if (parameters.Length > 0)
        {
            command += " " + string.Join(" ", parameters);
        }

        await SendRawAsync(command).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests the current mode string for a channel.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    public async Task GetChannelModeAsync(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel, nameof(channel));
        await SendRawAsync($"MODE {channel}").ConfigureAwait(false);
    }

    // Protocol enhancement utility methods

    /// <summary>
    /// Gets the server's maximum nickname length from ISUPPORT (005) parameters.
    /// </summary>
    /// <returns>Maximum allowed nickname length.</returns>
    public int GetServerMaxNicknameLength() => _isupportParser.MaxNicknameLength;

    /// <summary>
    /// Gets the server's maximum channel name length from ISUPPORT (005) parameters.
    /// </summary>
    /// <returns>Maximum allowed channel name length.</returns>
    public int GetServerMaxChannelLength() => _isupportParser.MaxChannelLength;

    /// <summary>
    /// Gets the server's network name from ISUPPORT (005), or <c>null</c> if not advertised.
    /// </summary>
    /// <returns>Network name (e.g. "Libera.Chat"), or <c>null</c>.</returns>
    public string? GetServerNetworkName() => _isupportParser.NetworkName;

    /// <summary>
    /// Gets the channel prefix characters supported by the server (e.g. "#&amp;").
    /// </summary>
    /// <returns>String of channel type prefix characters.</returns>
    public string GetServerChannelTypes() => _isupportParser.ChannelTypes;

    /// <summary>
    /// Compares two nicknames using the server's CASEMAPPING rules.
    /// </summary>
    /// <param name="nick1">First nickname.</param>
    /// <param name="nick2">Second nickname.</param>
    /// <returns><c>true</c> if the nicknames are equivalent under the server's case mapping.</returns>
    public bool NicknamesEqual(string nick1, string nick2)
    {
        return IrcCaseMapping.Equals(nick1, nick2, _isupportParser.CaseMapping);
    }

    /// <summary>
    /// Compares two channel names using the server's CASEMAPPING rules.
    /// </summary>
    /// <param name="channel1">First channel name.</param>
    /// <param name="channel2">Second channel name.</param>
    /// <returns><c>true</c> if the channel names are equivalent under the server's case mapping.</returns>
    public bool ChannelNamesEqual(string channel1, string channel2)
    {
        return IrcCaseMapping.Equals(channel1, channel2, _isupportParser.CaseMapping);
    }

    /// <summary>
    /// Encodes a message string to bytes using the default IRC encoding (UTF-8).
    /// </summary>
    /// <param name="message">The message to encode.</param>
    /// <returns>The encoded byte array.</returns>
    public byte[] EncodeMessage(string message)
    {
        return IrcEncoding.EncodeMessage(message, IrcEncoding.DefaultEncoding);
    }

    /// <summary>
    /// Validates whether a nickname conforms to IRC protocol rules.
    /// </summary>
    /// <param name="nickname">The nickname to validate.</param>
    /// <returns><c>true</c> if the nickname is valid.</returns>
    public bool IsValidNickname(string nickname)
    {
        var validation = IrcMessageValidator.ValidateNickname(nickname);
        return validation.IsValid;
    }

    /// <summary>
    /// Validates whether a channel name conforms to IRC protocol rules.
    /// </summary>
    /// <param name="channelName">The channel name to validate.</param>
    /// <returns><c>true</c> if the channel name is valid.</returns>
    public bool IsValidChannelName(string channelName)
    {
        var validation = IrcMessageValidator.ValidateChannelName(channelName);
        return validation.IsValid;
    }

    /// <summary>
    /// Handles exceptions that occur during send operations, determining whether to disconnect
    /// and translating exceptions for consistent API behavior
    /// </summary>
    /// <param name="ex">The exception that occurred</param>
    /// <param name="message">The message that was being sent (for logging)</param>
    /// <exception cref="InvalidOperationException">Thrown for connection failures or when not connected</exception>
    private void HandleSendException(Exception ex, string message)
    {
        _logger?.LogError(ex, "Failed to send message: {Message}", message);

        // Only disconnect on actual connection failures, not timeouts or cancellations
        bool shouldDisconnect = ex is IOException || ex is ObjectDisposedException ||
            ex is System.Net.WebSockets.WebSocketException ||
            (ex is SocketException sockEx && (sockEx.SocketErrorCode == SocketError.ConnectionAborted ||
                                              sockEx.SocketErrorCode == SocketError.ConnectionReset ||
                                              sockEx.SocketErrorCode == SocketError.NotConnected));

        if (shouldDisconnect)
        {
            SafeFireAndForget(() => DisconnectInternalAsync("Send operation failed"), "SendFailureDisconnect");
            throw new InvalidOperationException("Connection failed during send operation", ex);
        }

        // For other exceptions (timeouts, cancellations), let them propagate but check connection
        if (!_isConnected)
        {
            throw new InvalidOperationException("Not connected", ex);
        }

        // Re-throw the original exception for other cases (preserving stack trace)
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }
}
