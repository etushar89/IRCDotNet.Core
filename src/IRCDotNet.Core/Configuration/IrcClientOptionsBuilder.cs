using System.Text;
using IRCDotNet.Core.Configuration;

namespace IRCDotNet.Core.Configuration;

/// <summary>
/// Fluent builder for constructing <see cref="IrcClientOptions"/> with validation.
/// </summary>
public class IrcClientOptionsBuilder
{
    private readonly List<ServerConfig> _servers = new();
    private readonly List<string> _autoJoinChannels = new();
    private readonly List<string> _alternativeNicks = new();
    private readonly List<string> _requestedCapabilities = new();
    private readonly Dictionary<string, string> _additionalSaslParameters = new();

    private string _nick = string.Empty;
    private string _userName = string.Empty;
    private string _realName = string.Empty;
    private string? _password;
    private bool _autoReconnect = true;
    private int _maxReconnectAttempts = 0;
    private TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
    private TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(5);
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);
    private TimeSpan _readTimeout = TimeSpan.FromMinutes(5);
    private TimeSpan _pingTimeout = TimeSpan.FromMinutes(3);
    private TimeSpan _pingInterval = TimeSpan.FromMinutes(1);
    private TimeSpan _capabilityNegotiationTimeout = TimeSpan.FromSeconds(15);
    private bool _autoRejoinOnKick = false;
    private Encoding _encoding = Encoding.UTF8;
    private bool _verboseCapabilityNegotiation = false;
    private SaslOptions? _sasl;
    private string? _nickServPassword;

    // Rate limiting
    private bool _enableRateLimit = true;
    private Protocol.RateLimitConfig? _rateLimitConfig = null;

    // Send timeouts
    private int _sendTimeoutMs = 5000;
    private int _sendTimeoutCancelledMs = 1000;
    private int _sendTimeoutWithCancellationMs = 30000;

    // WebSocket
    private string? _webSocketUri;

    // CTCP
    private bool _enableCtcpAutoReply = true;
    private string? _ctcpVersionString;

    /// <summary>
    /// Set the primary nickname to use when connecting.
    /// </summary>
    /// <param name="nick">The desired nickname.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            throw new ArgumentException("Nick cannot be null or whitespace", nameof(nick));

        _nick = nick;
        return this;
    }

    /// <summary>
    /// Set the username (ident) sent in the USER command during registration.
    /// </summary>
    /// <param name="userName">The ident string (typically lowercase, no spaces).</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("UserName cannot be null or whitespace", nameof(userName));

        _userName = userName;
        return this;
    }

    /// <summary>
    /// Set the real name (GECOS field) visible in WHOIS responses.
    /// </summary>
    /// <param name="realName">The real name or application description.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithRealName(string realName)
    {
        if (string.IsNullOrWhiteSpace(realName))
            throw new ArgumentException("RealName cannot be null or whitespace", nameof(realName));

        _realName = realName;
        return this;
    }

    /// <summary>
    /// Add a server endpoint to connect to. The first server added becomes the primary connection.
    /// </summary>
    /// <param name="hostname">The server hostname or IP address.</param>
    /// <param name="port">The server port. Default: <c>6667</c>.</param>
    /// <param name="useSsl">Whether to connect using SSL/TLS.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddServer(string hostname, int port = 6667, bool useSsl = false)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentException("Hostname cannot be null or whitespace", nameof(hostname));
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");

        _servers.Add(new ServerConfig(hostname, port, useSsl));
        return this;
    }

    /// <summary>
    /// Add an SSL/TLS server endpoint. Equivalent to <c>AddServer(hostname, port, useSsl: true)</c>.
    /// </summary>
    /// <param name="hostname">The server hostname or IP address.</param>
    /// <param name="port">The server port. Default: <c>6697</c> (standard IRC SSL port).</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddSslServer(string hostname, int port = 6697)
    {
        return AddServer(hostname, port, true);
    }

    /// <summary>
    /// Add a channel to automatically join after connecting.
    /// </summary>
    /// <param name="channel">The channel name (e.g. <c>"#general"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddAutoJoinChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel cannot be null or whitespace", nameof(channel));

        _autoJoinChannels.Add(channel);
        return this;
    }

    /// <summary>
    /// Add multiple channels to automatically join after connecting.
    /// </summary>
    /// <param name="channels">Channel names to join (e.g. <c>"#general"</c>, <c>"#dev"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddAutoJoinChannels(params string[] channels)
    {
        foreach (var channel in channels)
        {
            AddAutoJoinChannel(channel);
        }
        return this;
    }

    /// <summary>
    /// Add a fallback nickname to try if the primary nick is already in use during registration.
    /// </summary>
    /// <param name="nick">The fallback nickname.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddAlternativeNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            throw new ArgumentException("Alternative nick cannot be null or whitespace", nameof(nick));

        _alternativeNicks.Add(nick);
        return this;
    }

    /// <summary>
    /// Configure auto-reconnection with exponential backoff when the connection drops.
    /// </summary>
    /// <param name="enabled">Whether auto-reconnect is active. Default: <c>true</c>.</param>
    /// <param name="maxAttempts">Maximum reconnect attempts. <c>0</c> for unlimited. Default: <c>0</c>.</param>
    /// <param name="initialDelay">Delay before the first reconnect attempt. Default: 5 seconds.</param>
    /// <param name="maxDelay">Maximum delay between attempts (caps backoff). Default: 5 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithAutoReconnect(bool enabled = true, int maxAttempts = 0, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        _autoReconnect = enabled;
        _maxReconnectAttempts = maxAttempts;

        if (initialDelay.HasValue)
            _reconnectDelay = initialDelay.Value;

        if (maxDelay.HasValue)
            _maxReconnectDelay = maxDelay.Value;

        return this;
    }

    /// <summary>
    /// Configure connection and keepalive timeouts.
    /// </summary>
    /// <param name="connection">Maximum time to wait for TCP connection and SSL handshake.</param>
    /// <param name="read">Maximum idle time before the connection is considered dead.</param>
    /// <param name="ping">Maximum time to wait for a PONG response before disconnecting.</param>
    /// <param name="pingInterval">Interval between PING keepalive messages.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithTimeouts(TimeSpan? connection = null, TimeSpan? read = null, TimeSpan? ping = null, TimeSpan? pingInterval = null)
    {
        if (connection.HasValue)
            _connectionTimeout = connection.Value;

        if (read.HasValue)
            _readTimeout = read.Value;

        if (ping.HasValue)
            _pingTimeout = ping.Value;

        if (pingInterval.HasValue)
            _pingInterval = pingInterval.Value;

        return this;
    }

    /// <summary>
    /// Configure SASL authentication during IRCv3 capability negotiation.
    /// </summary>
    /// <param name="username">The SASL account name.</param>
    /// <param name="password">The SASL account password.</param>
    /// <param name="mechanism">The SASL mechanism. Supported: <c>"PLAIN"</c>, <c>"EXTERNAL"</c>.</param>
    /// <param name="required">When <c>true</c>, disconnects if authentication fails.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithSaslAuthentication(string username, string password, string mechanism = "PLAIN", bool required = false)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("SASL username cannot be null or whitespace", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("SASL password cannot be null or whitespace", nameof(password));

        _sasl = new SaslOptions
        {
            Username = username,
            Password = password,
            Mechanism = mechanism,
            Required = required,
            TimeoutMs = (int)_capabilityNegotiationTimeout.TotalMilliseconds,
            AdditionalParameters = new Dictionary<string, string>(_additionalSaslParameters)
        };

        return this;
    }

    /// <summary>
    /// Set the NickServ IDENTIFY password. The client sends it automatically when prompted by NickServ after registration.
    /// </summary>
    /// <param name="password">The NickServ account password.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithNickServPassword(string password)
    {
        _nickServPassword = password;
        return this;
    }

    /// <summary>
    /// Set the server password sent via the PASS command before registration. Used by some servers and bouncers.
    /// </summary>
    /// <param name="password">The server connection password.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithServerPassword(string password)
    {
        _password = password;
        return this;
    }

    /// <summary>
    /// Whether to automatically rejoin a channel after being kicked. Default: <c>false</c>.
    /// </summary>
    /// <param name="enabled">Whether auto-rejoin is active.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithAutoRejoinOnKick(bool enabled = true)
    {
        _autoRejoinOnKick = enabled;
        return this;
    }

    /// <summary>
    /// Set the text encoding for IRC messages. Default: UTF-8. Use <c>Encoding.GetEncoding("ISO-8859-1")</c> for legacy servers.
    /// </summary>
    /// <param name="encoding">The <see cref="System.Text.Encoding"/> to use for reading and writing IRC lines.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithEncoding(Encoding encoding)
    {
        _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        return this;
    }

    /// <summary>
    /// Add IRCv3 capability names to request during CAP negotiation (e.g. <c>"away-notify"</c>, <c>"message-tags"</c>).
    /// </summary>
    /// <param name="capabilities">One or more IRCv3 capability strings.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder AddCapabilities(params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            if (!string.IsNullOrWhiteSpace(capability))
                _requestedCapabilities.Add(capability);
        }
        return this;
    }

    /// <summary>
    /// Enable detailed logging of CAP LS/REQ/ACK/NAK exchanges at Debug level.
    /// </summary>
    /// <param name="enabled">Whether verbose logging is active.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithVerboseCapabilityNegotiation(bool enabled = true)
    {
        _verboseCapabilityNegotiation = enabled;
        return this;
    }

    /// <summary>
    /// Configure token-bucket rate limiting for outgoing messages.
    /// </summary>
    /// <param name="enabled">Whether rate limiting is active. Default: <c>true</c>.</param>
    /// <param name="config">Custom <see cref="Protocol.RateLimitConfig"/>, or <c>null</c> for default (1 msg/sec, burst of 5).</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithRateLimit(bool enabled = true, Protocol.RateLimitConfig? config = null)
    {
        _enableRateLimit = enabled;
        _rateLimitConfig = config;
        return this;
    }

    /// <summary>
    /// Disable rate limiting for outgoing messages. Useful for testing against local servers.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithoutRateLimit()
    {
        return WithRateLimit(false, null);
    }

    /// <summary>
    /// Set the default send timeout for all outgoing messages.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds. Default: <c>5000</c>.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithSendTimeout(int timeoutMs)
    {
        _sendTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Configure separate send timeouts for normal, cancelled, and explicit-cancellation scenarios.
    /// </summary>
    /// <param name="normalMs">Normal send timeout in milliseconds.</param>
    /// <param name="cancelledMs">Timeout when cancellation is pending, in milliseconds.</param>
    /// <param name="withCancellationMs">Timeout for operations with explicit cancellation tokens, in milliseconds.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithSendTimeouts(int normalMs, int cancelledMs, int withCancellationMs)
    {
        _sendTimeoutMs = normalMs;
        _sendTimeoutCancelledMs = cancelledMs;
        _sendTimeoutWithCancellationMs = withCancellationMs;
        return this;
    }

    /// <summary>
    /// Set a WebSocket URI for connecting via WebSocket transport instead of raw TCP.
    /// When set, <see cref="AddServer"/> is not required. The URI must use <c>ws://</c> or <c>wss://</c> scheme
    /// and is validated during <see cref="Build"/>.
    /// </summary>
    /// <param name="uri">The WebSocket endpoint URI (e.g. <c>"wss://irc.unrealircd.org/"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is null or whitespace.</exception>
    public IrcClientOptionsBuilder WithWebSocket(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("WebSocket URI cannot be null or whitespace", nameof(uri));

        _webSocketUri = uri;
        return this;
    }

    /// <summary>
    /// Configure automatic replies to standard CTCP requests.
    /// When enabled, the client auto-replies to: VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, ERRMSG.
    /// </summary>
    /// <param name="enabled">Whether auto-reply is active. Default: <c>true</c>.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcClientOptionsBuilder WithCtcpAutoReply(bool enabled = true)
    {
        _enableCtcpAutoReply = enabled;
        return this;
    }

    /// <summary>
    /// Set the version string returned in CTCP VERSION auto-replies.
    /// If not set, defaults to <c>"IRCDotNet.Core {assembly-version}"</c>.
    /// </summary>
    /// <param name="versionString">The version identifier (e.g. <c>"MyApp v2.0"</c> or <c>"MyBot 1.0 (Linux)"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="versionString"/> is null or whitespace.</exception>
    public IrcClientOptionsBuilder WithCtcpVersionString(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
            throw new ArgumentException("CTCP version string cannot be null or whitespace", nameof(versionString));

        _ctcpVersionString = versionString;
        return this;
    }

    /// <summary>
    /// Create a new builder pre-populated from an existing <see cref="IrcClientOptions"/> template.
    /// The primary server and WebSocket URI are copied automatically.
    /// </summary>
    /// <param name="template">The template configuration to copy from.</param>
    /// <returns>A new builder pre-filled from the template.</returns>
    public static IrcClientOptionsBuilder FromTemplate(IrcClientOptions template)
    {
        var builder = new IrcClientOptionsBuilder();

        // Copy primary server if set
        if (!string.IsNullOrWhiteSpace(template.Server))
            builder.AddServer(template.Server, template.Port, template.UseSsl);

        // Only set nick if it's not null or whitespace
        if (!string.IsNullOrWhiteSpace(template.Nick))
            builder.WithNick(template.Nick);

        // Only set username if it's not null or whitespace
        if (!string.IsNullOrWhiteSpace(template.UserName))
            builder.WithUserName(template.UserName);

        // Only set real name if it's not null or whitespace
        if (!string.IsNullOrWhiteSpace(template.RealName))
            builder.WithRealName(template.RealName);

        builder
            .WithAutoReconnect(template.AutoReconnect, template.MaxReconnectAttempts,
                TimeSpan.FromMilliseconds(template.ReconnectDelayMs),
                TimeSpan.FromMilliseconds(template.MaxReconnectDelayMs))
            .WithTimeouts(
                TimeSpan.FromMilliseconds(template.ConnectionTimeoutMs),
                TimeSpan.FromMilliseconds(template.ReadTimeoutMs),
                TimeSpan.FromMilliseconds(template.PingTimeoutMs),
                TimeSpan.FromMilliseconds(template.PingIntervalMs))
            .WithAutoRejoinOnKick(template.AutoRejoinOnKick)
            .WithVerboseCapabilityNegotiation(template.VerboseCapabilityNegotiation)
            .WithEncoding(Encoding.GetEncoding(template.Encoding))
            .WithRateLimit(template.EnableRateLimit, template.RateLimitConfig)
            .WithSendTimeouts(template.SendTimeoutMs, template.SendTimeoutCancelledMs, template.SendTimeoutWithCancellationMs);

        if (!string.IsNullOrEmpty(template.Password))
            builder.WithServerPassword(template.Password);

        if (template.Sasl != null)
        {
            builder.WithSaslAuthentication(template.Sasl.Username, template.Sasl.Password,
                template.Sasl.Mechanism, template.Sasl.Required);
        }

        if (!string.IsNullOrEmpty(template.NickServPassword))
            builder.WithNickServPassword(template.NickServPassword);

        if (!string.IsNullOrWhiteSpace(template.WebSocketUri))
            builder.WithWebSocket(template.WebSocketUri);

        builder.WithCtcpAutoReply(template.EnableCtcpAutoReply);

        if (!string.IsNullOrWhiteSpace(template.CtcpVersionString))
            builder.WithCtcpVersionString(template.CtcpVersionString);

        return builder;
    }

    /// <summary>
    /// Set a WebSocket URI and build the configuration in one step.
    /// Equivalent to calling <see cref="WithWebSocket"/> followed by <see cref="Build"/>.
    /// The URI is validated during build (must be <c>ws://</c> or <c>wss://</c>).
    /// </summary>
    /// <param name="uri">The WebSocket endpoint URI (e.g. <c>"wss://irc.unrealircd.org/"</c>).</param>
    /// <returns>The validated <see cref="IrcClientOptions"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is invalid or uses a non-WebSocket scheme.</exception>
    /// <exception cref="InvalidOperationException">Required fields (Nick, UserName, RealName) are missing.</exception>
    public IrcClientOptions BuildForWebSocket(string uri)
    {
        WithWebSocket(uri);
        return Build();
    }

    /// <summary>
    /// Add a server and build the configuration in one step.
    /// </summary>
    /// <param name="hostname">The server hostname or IP address.</param>
    /// <param name="port">The server port. Default: <c>6667</c>.</param>
    /// <param name="useSsl">Whether to connect using SSL/TLS.</param>
    /// <returns>The built <see cref="IrcClientOptions"/>.</returns>
    public IrcClientOptions BuildForServer(string hostname, int port = 6667, bool useSsl = false)
    {
        AddServer(hostname, port, useSsl);
        return Build();
    }

    /// <summary>
    /// Build the final <see cref="IrcClientOptions"/>, validating all required fields.
    /// </summary>
    /// <returns>The validated <see cref="IrcClientOptions"/>.</returns>
    /// <exception cref="InvalidOperationException">Required fields are missing.</exception>
    public IrcClientOptions Build()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(_nick))
            throw new InvalidOperationException("Nick is required");
        if (string.IsNullOrWhiteSpace(_userName))
            throw new InvalidOperationException("UserName is required");
        if (string.IsNullOrWhiteSpace(_realName))
            throw new InvalidOperationException("RealName is required");
        if (_servers.Count == 0 && string.IsNullOrWhiteSpace(_webSocketUri))
            throw new InvalidOperationException("At least one server or a WebSocket URI must be specified");

        // Use the first server for backward compatibility with the existing IrcClientOptions structure
        var primaryServer = _servers.Count > 0 ? _servers[0] : null;

        var options = new IrcClientOptions
        {
            Server = primaryServer?.Hostname ?? string.Empty,
            Port = primaryServer?.Port ?? 6667,
            UseSsl = primaryServer?.UseSsl ?? false,
            EnableRateLimit = _enableRateLimit,
            RateLimitConfig = _rateLimitConfig,
            Nick = _nick,
            UserName = _userName,
            RealName = _realName,
            Password = _password,
            AlternativeNicks = new List<string>(_alternativeNicks),
            AutoReconnect = _autoReconnect,
            MaxReconnectAttempts = _maxReconnectAttempts,
            ReconnectDelayMs = (int)_reconnectDelay.TotalMilliseconds,
            MaxReconnectDelayMs = (int)_maxReconnectDelay.TotalMilliseconds,
            ConnectionTimeoutMs = (int)_connectionTimeout.TotalMilliseconds,
            ReadTimeoutMs = (int)_readTimeout.TotalMilliseconds,
            PingTimeoutMs = (int)_pingTimeout.TotalMilliseconds,
            PingIntervalMs = (int)_pingInterval.TotalMilliseconds,
            AutoRejoinOnKick = _autoRejoinOnKick,
            Encoding = _encoding.WebName,
            VerboseCapabilityNegotiation = _verboseCapabilityNegotiation,
            Sasl = _sasl?.Clone(),
            NickServPassword = _nickServPassword,
            RequestedCapabilities = new List<string>(_requestedCapabilities.Count > 0 ? _requestedCapabilities : GetDefaultCapabilities()),
            CapabilityNegotiationTimeoutMs = (int)_capabilityNegotiationTimeout.TotalMilliseconds,
            SendTimeoutMs = _sendTimeoutMs,
            SendTimeoutCancelledMs = _sendTimeoutCancelledMs,
            SendTimeoutWithCancellationMs = _sendTimeoutWithCancellationMs,
            WebSocketUri = _webSocketUri,
            EnableCtcpAutoReply = _enableCtcpAutoReply,
            CtcpVersionString = _ctcpVersionString ?? $"IRCDotNet.Core {typeof(IrcClientOptions).Assembly.GetName().Version}"
        };

        // Store additional servers and auto-join channels in custom properties for multi-server support later
        options.AdditionalServers = _servers.Count > 1 ? _servers.Skip(1).ToList() : new List<ServerConfig>();
        options.AutoJoinChannels = new List<string>(_autoJoinChannels);

        // Validate the final configuration
        options.Validate();

        return options;
    }

    private static List<string> GetDefaultCapabilities()
    {
        return new List<string>
        {
            "multi-prefix",
            "away-notify",
            "account-notify",
            "extended-join",
            "server-time",
            "cap-notify",            "chghost",
            "invite-notify",
            "message-tags",
            "batch",
            "labeled-response",
            "echo-message"
        };
    }
}
