using IRCDotNet.Core.Protocol;

namespace IRCDotNet.Core.Configuration;

/// <summary>
/// Server endpoint configuration for multi-server support.
/// </summary>
/// <param name="Hostname">The IRC server hostname or IP address.</param>
/// <param name="Port">The server port number.</param>
/// <param name="UseSsl">Whether to connect using SSL/TLS.</param>
public record ServerConfig(string Hostname, int Port, bool UseSsl);

/// <summary>
/// SASL authentication configuration for IRCv3 capability negotiation.
/// </summary>
public class SaslOptions
{
    /// <summary>
    /// SASL mechanism to use. Supported values: <c>"PLAIN"</c>, <c>"EXTERNAL"</c>.
    /// </summary>
    public string Mechanism { get; set; } = "PLAIN";

    /// <summary>
    /// Account username for SASL PLAIN authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Account password for SASL PLAIN authentication.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Maximum time in milliseconds to wait for SASL authentication to complete.
    /// </summary>
    public int TimeoutMs { get; set; } = 15000;

    /// <summary>
    /// When <c>true</c>, the client disconnects if SASL authentication fails.
    /// When <c>false</c>, the client continues connecting without authentication.
    /// </summary>
    public bool Required { get; set; } = false;

    /// <summary>
    /// Additional key-value parameters for advanced SASL mechanisms beyond PLAIN/EXTERNAL.
    /// </summary>
    public Dictionary<string, string> AdditionalParameters { get; set; } = new();

    /// <summary>
    /// Validates the SASL configuration, throwing if any values are invalid.
    /// </summary>
    /// <exception cref="ArgumentException">A required field is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Timeout is not positive.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Mechanism))
            throw new ArgumentException("SASL mechanism is required", nameof(Mechanism));

        if (Mechanism.Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Username))
                throw new ArgumentException("Username is required for PLAIN SASL", nameof(Username));
            if (string.IsNullOrWhiteSpace(Password))
                throw new ArgumentException("Password is required for PLAIN SASL", nameof(Password));
        }

        if (TimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "SASL timeout must be positive");
    }

    /// <summary>
    /// Creates a deep copy of the SASL options.
    /// </summary>
    /// <returns>A new <see cref="SaslOptions"/> instance with copied values.</returns>
    public SaslOptions Clone()
    {
        return new SaslOptions
        {
            Mechanism = Mechanism,
            Username = Username,
            Password = Password,
            TimeoutMs = TimeoutMs,
            Required = Required,
            AdditionalParameters = new Dictionary<string, string>(AdditionalParameters)
        };
    }
}

/// <summary>
/// Configuration options for an <see cref="IRCDotNet.IrcClient"/> connection.
/// </summary>
public class IrcClientOptions
{
    /// <summary>
    /// IRC server hostname or IP address (e.g. <c>"irc.libera.chat"</c>).
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// IRC server port. Default is <c>6667</c> for plain connections, use <c>6697</c> for SSL.
    /// </summary>
    public int Port { get; set; } = 6667;

    /// <summary>
    /// Whether to connect using SSL/TLS encryption.
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Whether to apply token-bucket rate limiting on outgoing messages to prevent flood kicks.
    /// </summary>
    public bool EnableRateLimit { get; set; } = true;

    /// <summary>
    /// Custom rate limit configuration. When <c>null</c>, uses the default (1 msg/sec, burst of 5).
    /// </summary>
    public Protocol.RateLimitConfig? RateLimitConfig { get; set; } = null;

    /// <summary>
    /// Whether to accept invalid SSL certificates (self-signed, expired, wrong hostname).
    /// Only applies when <see cref="UseSsl"/> is <c>true</c>. Use with caution in production.
    /// </summary>
    public bool AcceptInvalidSslCertificates { get; set; } = false;

    /// <summary>
    /// Timeout in milliseconds for normal send operations. Default: 5000 (5 seconds).
    /// </summary>
    public int SendTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Timeout in milliseconds for send operations when cancellation is already pending. Default: 1000 (1 second).
    /// </summary>
    public int SendTimeoutCancelledMs { get; set; } = 1000;

    /// <summary>
    /// Timeout in milliseconds for send operations initiated with an explicit cancellation token. Default: 30000 (30 seconds).
    /// </summary>
    public int SendTimeoutWithCancellationMs { get; set; } = 30000;

    /// <summary>
    /// The primary nickname to use when connecting. Required.
    /// </summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>
    /// Fallback nicknames to try in order if <see cref="Nick"/> is already in use during registration.
    /// </summary>
    public List<string> AlternativeNicks { get; set; } = new();

    /// <summary>
    /// The username (ident) sent in the USER command during registration.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The real name (GECOS field) sent in the USER command. Visible in WHOIS responses.
    /// </summary>
    public string RealName { get; set; } = string.Empty;

    /// <summary>
    /// Server password sent via the PASS command before registration. Used by some servers and bouncers.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Maximum time in milliseconds to wait for the TCP connection and SSL handshake. Default: 30000 (30 seconds).
    /// </summary>
    public int ConnectionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Maximum time in milliseconds to wait for data from the server before considering the connection dead. Default: 300000 (5 minutes).
    /// </summary>
    public int ReadTimeoutMs { get; set; } = 300000; // 5 minutes

    /// <summary>
    /// Maximum time in milliseconds to wait for a PONG response before disconnecting. Must be greater than <see cref="PingIntervalMs"/>. Default: 60000 (1 minute).
    /// </summary>
    public int PingTimeoutMs { get; set; } = 60000; // 1 minute

    /// <summary>
    /// Interval in milliseconds between PING keepalive messages sent to the server. Default: 30000 (30 seconds).
    /// </summary>
    public int PingIntervalMs { get; set; } = 30000; // 30 seconds

    /// <summary>
    /// Whether to automatically rejoin a channel after being kicked. Default: <c>false</c>.
    /// </summary>
    public bool AutoRejoinOnKick { get; set; } = false;

    /// <summary>
    /// Whether to automatically reconnect when the connection is lost. Default: <c>true</c>.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Maximum number of reconnection attempts. Set to <c>0</c> for unlimited retries. Default: <c>0</c>.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>
    /// Initial delay in milliseconds before the first reconnection attempt. Increases with exponential backoff. Default: 5000 (5 seconds).
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum delay in milliseconds between reconnection attempts (caps exponential backoff). Default: 300000 (5 minutes).
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 300000; // 5 minutes

    /// <summary>
    /// IRCv3 capabilities to request during CAP negotiation. Defaults to a comprehensive set including
    /// multi-prefix, away-notify, account-notify, extended-join, server-time, message-tags, batch, and more.
    /// </summary>
    public List<string> RequestedCapabilities { get; set; } = new()
    {
        "multi-prefix",
        "away-notify",
        "account-notify",
        "extended-join",
        "server-time",
        "cap-notify",
        "chghost",
        "invite-notify",
        "message-tags",
        "batch",
        "labeled-response",
        "echo-message"
    };

    /// <summary>
    /// Text encoding for IRC messages. Default: <c>"UTF-8"</c>. Use <c>"ISO-8859-1"</c> for legacy servers.
    /// </summary>
    public string Encoding { get; set; } = "UTF-8";

    /// <summary>
    /// SASL authentication configuration. When <c>null</c>, SASL is not attempted.
    /// </summary>
    public SaslOptions? Sasl { get; set; }

    /// <summary>
    /// NickServ IDENTIFY password. When set, the client automatically sends
    /// <c>PRIVMSG NickServ :IDENTIFY &lt;nick&gt; &lt;password&gt;</c> when prompted by NickServ after registration.
    /// </summary>
    public string? NickServPassword { get; set; }

    /// <summary>
    /// Whether to log detailed capability negotiation messages at Debug level.
    /// </summary>
    public bool VerboseCapabilityNegotiation { get; set; } = false;

    /// <summary>
    /// Maximum time in milliseconds to wait for CAP negotiation to complete before giving up. Default: 15000 (15 seconds).
    /// </summary>
    public int CapabilityNegotiationTimeoutMs { get; set; } = 15000;

    /// <summary>
    /// When <c>true</c>, requests all capabilities the server advertises (ignoring <see cref="RequestedCapabilities"/>).
    /// Capabilities in <see cref="BlacklistedCapabilities"/> are still excluded.
    /// </summary>
    public bool RequestAllCapabilities { get; set; } = false;

    /// <summary>
    /// Capability names to never request, even when <see cref="RequestAllCapabilities"/> is <c>true</c>.
    /// </summary>
    public List<string> BlacklistedCapabilities { get; set; } = new();

    /// <summary>
    /// Whether to process IRCv3 BATCH messages. Default: <c>true</c>.
    /// </summary>
    public bool EnableBatchProcessing { get; set; } = true;

    /// <summary>
    /// Whether to process IRCv3 message tags and fire <see cref="IRCDotNet.IrcClient.MessageTagsReceived"/> events. Default: <c>true</c>.
    /// </summary>
    public bool EnableMessageTags { get; set; } = true;

    /// <summary>
    /// Whether to automatically reply to standard CTCP requests (VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, ERRMSG). Default: <c>true</c>.
    /// </summary>
    public bool EnableCtcpAutoReply { get; set; } = true;

    /// <summary>
    /// Version string returned in response to CTCP VERSION requests. Default: assembly version (e.g. <c>"IRCDotNet.Core 2.0.2.0"</c>).
    /// </summary>
    public string CtcpVersionString { get; set; } = $"IRCDotNet.Core {typeof(IrcClientOptions).Assembly.GetName().Version}";

    /// <summary>
    /// Additional server endpoints for future multi-server support. Currently populated by <see cref="IrcClientOptionsBuilder"/> when multiple servers are added.
    /// </summary>
    public List<ServerConfig> AdditionalServers { get; set; } = new();

    /// <summary>
    /// Channel names to automatically join after the <c>Connected</c> event fires.
    /// </summary>
    public List<string> AutoJoinChannels { get; set; } = new();

    /// <summary>
    /// WebSocket URI for connecting via WebSocket transport (e.g. <c>"wss://irc.unrealircd.org/"</c>).
    /// When set, the client uses <see cref="IRCDotNet.Transport.WebSocketIrcTransport"/> instead of
    /// <see cref="IRCDotNet.Transport.TcpIrcTransport"/>. The <see cref="Server"/> and <see cref="Port"/> properties
    /// are not used for connection but are still available for display/logging purposes.
    /// When <c>null</c> (default), connects via raw TCP.
    /// </summary>
    public string? WebSocketUri { get; set; }

    /// <summary>
    /// Validates the configuration, throwing if any values are invalid.
    /// </summary>
    /// <exception cref="ArgumentException">A required field is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric value is out of range.</exception>
    public void Validate()
    {
        // Server/Port validation is skipped when using WebSocket (URI contains the endpoint)
        if (string.IsNullOrWhiteSpace(WebSocketUri))
        {
            if (string.IsNullOrWhiteSpace(Server))
                throw new ArgumentException("Server is required", nameof(Server));

            if (Port <= 0 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535");
        }
        else
        {
            // Validate WebSocketUri is a well-formed absolute URI with ws/wss scheme
            if (!Uri.TryCreate(WebSocketUri, UriKind.Absolute, out var wsUri) ||
                (wsUri.Scheme != "ws" && wsUri.Scheme != "wss"))
            {
                throw new ArgumentException(
                    "WebSocketUri must be a valid absolute URI with ws:// or wss:// scheme",
                    nameof(WebSocketUri));
            }
        }

        if (string.IsNullOrWhiteSpace(Nick))
            throw new ArgumentException("Nick is required", nameof(Nick));

        if (string.IsNullOrWhiteSpace(UserName))
            throw new ArgumentException("UserName is required", nameof(UserName));

        if (string.IsNullOrWhiteSpace(RealName))
            throw new ArgumentException("RealName is required", nameof(RealName));

        if (ConnectionTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(ConnectionTimeoutMs), "Connection timeout must be positive");

        if (ReadTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReadTimeoutMs), "Read timeout must be positive");

        if (PingTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(PingTimeoutMs), "Ping timeout must be positive");

        if (PingIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(PingIntervalMs), "Ping interval must be positive");

        if (PingTimeoutMs <= PingIntervalMs)
            throw new ArgumentOutOfRangeException(nameof(PingTimeoutMs), "Ping timeout must be greater than ping interval");

        if (ReconnectDelayMs < 0)
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelayMs), "Reconnect delay cannot be negative");

        if (MaxReconnectDelayMs < ReconnectDelayMs)
            throw new ArgumentOutOfRangeException(nameof(MaxReconnectDelayMs), "Max reconnect delay must be >= reconnect delay");

        if (SendTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(SendTimeoutMs), "Send timeout must be positive");

        if (SendTimeoutCancelledMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(SendTimeoutCancelledMs), "Send timeout (cancelled) must be positive");

        if (SendTimeoutWithCancellationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(SendTimeoutWithCancellationMs), "Send timeout (with cancellation) must be positive");
    }

    /// <summary>
    /// Creates a deep copy of the current options, including cloned collections and SASL config.
    /// </summary>
    /// <returns>A new <see cref="IrcClientOptions"/> instance with independently mutable copies of all values.</returns>
    public IrcClientOptions Clone()
    {
        return new IrcClientOptions
        {
            Server = Server,
            Port = Port,
            UseSsl = UseSsl,
            EnableRateLimit = EnableRateLimit,
            RateLimitConfig = RateLimitConfig,
            AcceptInvalidSslCertificates = AcceptInvalidSslCertificates,
            SendTimeoutMs = SendTimeoutMs,
            SendTimeoutCancelledMs = SendTimeoutCancelledMs,
            SendTimeoutWithCancellationMs = SendTimeoutWithCancellationMs,
            Nick = Nick,
            AlternativeNicks = new List<string>(AlternativeNicks),
            UserName = UserName,
            RealName = RealName,
            Password = Password,
            ConnectionTimeoutMs = ConnectionTimeoutMs,
            ReadTimeoutMs = ReadTimeoutMs,
            PingTimeoutMs = PingTimeoutMs,
            PingIntervalMs = PingIntervalMs,
            AutoRejoinOnKick = AutoRejoinOnKick,
            AutoReconnect = AutoReconnect,
            MaxReconnectAttempts = MaxReconnectAttempts,
            ReconnectDelayMs = ReconnectDelayMs,
            MaxReconnectDelayMs = MaxReconnectDelayMs,
            RequestedCapabilities = new List<string>(RequestedCapabilities),
            Encoding = Encoding,
            Sasl = Sasl?.Clone(),
            VerboseCapabilityNegotiation = VerboseCapabilityNegotiation,
            CapabilityNegotiationTimeoutMs = CapabilityNegotiationTimeoutMs,
            RequestAllCapabilities = RequestAllCapabilities,
            BlacklistedCapabilities = new List<string>(BlacklistedCapabilities),
            EnableBatchProcessing = EnableBatchProcessing,
            EnableMessageTags = EnableMessageTags,
            EnableCtcpAutoReply = EnableCtcpAutoReply,
            CtcpVersionString = CtcpVersionString,
            AdditionalServers = AdditionalServers.ToList(),
            AutoJoinChannels = new List<string>(AutoJoinChannels),
            WebSocketUri = WebSocketUri
        };
    }
}
