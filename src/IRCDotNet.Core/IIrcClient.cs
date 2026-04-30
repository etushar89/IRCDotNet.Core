namespace IRCDotNet.Core;

/// <summary>
/// Interface for IRC client operations. Enables dependency injection and unit testing
/// by allowing consumers to mock the IRC client in their tests.
/// </summary>
public interface IIrcClient : IDisposable, IAsyncDisposable
{
    // ── Properties ───────────────────────────────────────────────────

    /// <summary>Whether the client is currently connected to the server.</summary>
    bool IsConnected { get; }

    /// <summary>Whether IRC registration (NICK/USER) has completed.</summary>
    bool IsRegistered { get; }

    /// <summary>The client's current nickname on the server.</summary>
    string CurrentNick { get; }

    /// <summary>Snapshot of joined channels mapped to their user sets.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> Channels { get; }

    /// <summary>IRCv3 capabilities enabled for this connection.</summary>
    IReadOnlySet<string> EnabledCapabilities { get; }

    /// <summary>The configuration snapshot used to create this client.</summary>
    Configuration.IrcClientOptions Configuration { get; }

    // ── Connection ───────────────────────────────────────────────────

    /// <summary>Connects to the IRC server.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects from the server with an optional quit message.</summary>
    Task DisconnectAsync(string? reason = null);

    /// <summary>Sends a raw IRC message to the server.</summary>
    Task SendRawAsync(string message);

    /// <summary>Sends a raw IRC message with cancellation support.</summary>
    Task SendRawWithCancellationAsync(string message, CancellationToken cancellationToken);

    // ── Messaging ────────────────────────────────────────────────────

    /// <summary>Sends a PRIVMSG to a channel or user.</summary>
    Task SendMessageAsync(string target, string message);

    /// <summary>Sends a PRIVMSG with cancellation support.</summary>
    Task SendMessageWithCancellationAsync(string target, string message, CancellationToken cancellationToken);

    /// <summary>Sends a NOTICE to a channel or user.</summary>
    Task SendNoticeAsync(string target, string message);

    /// <summary>Sends a NOTICE with cancellation support.</summary>
    Task SendNoticeWithCancellationAsync(string target, string message, CancellationToken cancellationToken);

    /// <summary>Sends a PRIVMSG with IRCv3 message tags.</summary>
    Task SendMessageWithTagsAsync(string target, string message, Dictionary<string, string?>? tags = null);

    /// <summary>Sends a TAGMSG (tags-only, no text) to a target.</summary>
    Task SendTagMessageAsync(string target, Dictionary<string, string?> tags);

    // ── Channels ─────────────────────────────────────────────────────

    /// <summary>Joins an IRC channel.</summary>
    Task JoinChannelAsync(string channel, string? key = null);

    /// <summary>Leaves (PARTs) an IRC channel.</summary>
    Task LeaveChannelAsync(string channel, string? reason = null);

    /// <summary>Sets the topic for a channel.</summary>
    Task SetTopicAsync(string channel, string topic);

    /// <summary>Queries the topic for a channel.</summary>
    Task GetTopicAsync(string channel);

    /// <summary>Requests the user list for a channel (NAMES).</summary>
    Task GetChannelUsersAsync(string channel);

    /// <summary>Requests the full channel list from the server (LIST).</summary>
    Task RequestChannelListAsync();

    /// <summary>Gets the channel list as an awaitable result with timeout.</summary>
    Task<List<Events.ChannelListEntry>> GetChannelListAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    // ── CTCP ─────────────────────────────────────────────────────────

    /// <summary>Sends a CTCP ACTION (/me) to a channel or user.</summary>
    Task SendActionAsync(string target, string actionText);

    /// <summary>Sends a CTCP request (VERSION, PING, TIME, etc.).</summary>
    Task SendCtcpRequestAsync(string target, string command, string? parameters = null);

    /// <summary>Sends a CTCP reply.</summary>
    Task SendCtcpReplyAsync(string target, string command, string? replyText = null);

    // ── User ─────────────────────────────────────────────────────────

    /// <summary>Changes the client's nickname.</summary>
    Task ChangeNickAsync(string newNick);

    /// <summary>Sets or clears the away message.</summary>
    Task SetAwayAsync(string? message = null);

    /// <summary>Changes the client's real name (requires setname capability).</summary>
    Task SetRealNameAsync(string realName);

    /// <summary>Sets user modes.</summary>
    Task SetUserModeAsync(string modes);

    // ── Admin ────────────────────────────────────────────────────────

    /// <summary>Kicks a user from a channel.</summary>
    Task KickUserAsync(string channel, string nick, string? reason = null);

    /// <summary>Invites a user to a channel.</summary>
    Task InviteUserAsync(string nick, string channel);

    /// <summary>Sets channel modes.</summary>
    Task SetChannelModeAsync(string channel, string modes, params string[] parameters);

    /// <summary>Queries the current channel modes.</summary>
    Task GetChannelModeAsync(string channel);

    // ── Queries ──────────────────────────────────────────────────────

    /// <summary>Sends a WHO query.</summary>
    Task WhoAsync(string target);

    /// <summary>Sends a WHOWAS query.</summary>
    Task WhoWasAsync(string nick);

    /// <summary>Sends a WHOIS query for a user.</summary>
    Task GetUserInfoAsync(string nick);

    /// <summary>Sends a LIST query with an optional pattern.</summary>
    Task ListChannelsAsync(string? pattern = null);

    // ── IRCv3 ────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a nickname to the MONITOR list.
    /// MONITOR does not infer nickname changes; <see cref="Events.NickChangedEvent"/> is raised only when the server sends an explicit rename signal.
    /// </summary>
    Task MonitorNickAsync(string nick);

    /// <summary>Removes a nickname from the MONITOR list.</summary>
    Task UnmonitorNickAsync(string nick);

    // ── Server Info ──────────────────────────────────────────────────

    /// <summary>Gets the server network name from ISUPPORT.</summary>
    string? GetServerNetworkName();

    /// <summary>Gets the maximum nickname length from ISUPPORT.</summary>
    int GetServerMaxNicknameLength();

    /// <summary>Gets the maximum channel name length from ISUPPORT.</summary>
    int GetServerMaxChannelLength();

    /// <summary>Gets the supported channel types from ISUPPORT.</summary>
    string GetServerChannelTypes();

    /// <summary>Gets the server case mapping from ISUPPORT.</summary>
    /// <returns>The server case mapping used for nickname and channel comparison.</returns>
    Protocol.CaseMappingType GetServerCaseMapping();

    // ── Utilities ────────────────────────────────────────────────────

    /// <summary>Compares two nicknames using the server's case mapping rules.</summary>
    bool NicknamesEqual(string nick1, string nick2);

    /// <summary>Compares two channel names using the server's case mapping rules.</summary>
    bool ChannelNamesEqual(string channel1, string channel2);

    /// <summary>Validates whether a string is a valid IRC nickname.</summary>
    bool IsValidNickname(string nickname);

    /// <summary>Validates whether a string is a valid IRC channel name.</summary>
    bool IsValidChannelName(string channelName);

    /// <summary>Encodes a message string to bytes using the configured encoding.</summary>
    byte[] EncodeMessage(string message);

    // ── Core Events ──────────────────────────────────────────────────

    /// <summary>Raised when a user joins a channel.</summary>
    event EventHandler<Events.UserJoinedChannelEvent>? UserJoinedChannel;
    /// <summary>Raised when a user leaves a channel.</summary>
    event EventHandler<Events.UserLeftChannelEvent>? UserLeftChannel;
    /// <summary>Raised when a user quits the IRC network.</summary>
    event EventHandler<Events.UserQuitEvent>? UserQuit;
    /// <summary>Raised when a PRIVMSG is received.</summary>
    event EventHandler<Events.PrivateMessageEvent>? PrivateMessageReceived;
    /// <summary>Raised when a NOTICE is received.</summary>
    event EventHandler<Events.NoticeEvent>? NoticeReceived;
    /// <summary>Raised when the client connects and registers.</summary>
    event EventHandler<Events.ConnectedEvent>? Connected;
    /// <summary>Raised when the client disconnects.</summary>
    event EventHandler<Events.DisconnectedEvent>? Disconnected;
    /// <summary>Raised when a channel topic changes.</summary>
    event EventHandler<Events.TopicChangedEvent>? TopicChanged;
    /// <summary>Raised when a user is kicked from a channel.</summary>
    event EventHandler<Events.UserKickedEvent>? UserKicked;
    /// <summary>Raised when a user changes their nickname.</summary>
    event EventHandler<Events.NickChangedEvent>? NickChanged;
    /// <summary>Raised when a nickname collision occurs.</summary>
    event EventHandler<Events.NicknameCollisionEvent>? NicknameCollision;
    /// <summary>Raised when the server sends a channel user list.</summary>
    event EventHandler<Events.ChannelUsersEvent>? ChannelUsersReceived;
    /// <summary>Raised for every raw IRC message received.</summary>
    event EventHandler<Events.RawMessageEvent>? RawMessageReceived;
    /// <summary>Raised after an ISUPPORT reply has been parsed into server feature state.</summary>
    event EventHandler<Events.IsupportReceivedEvent>? IsupportReceived;
    /// <summary>Raised when a channel join attempt fails.</summary>
    event EventHandler<Events.ChannelJoinFailedEvent>? ChannelJoinFailed;
    /// <summary>Raised when IRCv3 capability negotiation completes.</summary>
    event EventHandler<Events.CapabilitiesNegotiatedEvent>? CapabilitiesNegotiated;
    /// <summary>Raised when a user's away status changes.</summary>
    event EventHandler<Events.UserAwayStatusChangedEvent>? UserAwayStatusChanged;
    /// <summary>Raised when a user's account changes.</summary>
    event EventHandler<Events.UserAccountChangedEvent>? UserAccountChanged;
    /// <summary>Raised on extended JOIN with account and realname.</summary>
    event EventHandler<Events.ExtendedUserJoinedChannelEvent>? ExtendedUserJoinedChannel;
    /// <summary>Raised when a user's hostname changes.</summary>
    event EventHandler<Events.UserHostnameChangedEvent>? UserHostnameChanged;
    /// <summary>Raised when a BATCH message is received.</summary>
    event EventHandler<Events.BatchEvent>? BatchReceived;
    /// <summary>Raised during SASL authentication.</summary>
    event EventHandler<Events.SaslAuthenticationEvent>? SaslAuthentication;
    /// <summary>Raised when a message with IRCv3 tags is received.</summary>
    event EventHandler<Events.MessageTagsEvent>? MessageTagsReceived;
    /// <summary>Raised when a <c>+typing</c> notification is received via TAGMSG (IRCv3 client tag).</summary>
    event EventHandler<Events.TypingIndicatorEvent>? TypingIndicatorReceived;
    /// <summary>Raised when a WHO reply is received.</summary>
    event EventHandler<Events.WhoReceivedEvent>? WhoReceived;
    /// <summary>Raised when a WHOWAS reply is received.</summary>
    event EventHandler<Events.WhoWasReceivedEvent>? WhoWasReceived;
    /// <summary>Raised for each channel entry in a LIST reply.</summary>
    event EventHandler<Events.ChannelListReceivedEvent>? ChannelListReceived;
    /// <summary>Raised when the LIST reply ends.</summary>
    event EventHandler<Events.ChannelListEndEvent>? ChannelListEndReceived;
    /// <summary>Raised when a CTCP request is received.</summary>
    event EventHandler<Events.CtcpRequestEvent>? CtcpRequestReceived;
    /// <summary>Raised when a CTCP reply is received.</summary>
    event EventHandler<Events.CtcpReplyEvent>? CtcpReplyReceived;
    /// <summary>Raised when a CTCP ACTION is received.</summary>
    event EventHandler<Events.CtcpActionEvent>? CtcpActionReceived;
    /// <summary>Raised when the client is invited to a channel.</summary>
    event EventHandler<Events.InviteReceivedEvent>? InviteReceived;
    /// <summary>Raised when the server confirms the client's away status.</summary>
    event EventHandler<Events.OwnAwayStatusChangedEvent>? OwnAwayStatusChanged;
    /// <summary>Raised when channel mode information is received.</summary>
    event EventHandler<Events.ChannelModeIsEvent>? ChannelModeIsReceived;
    /// <summary>Raised for general IRC error replies.</summary>
    event EventHandler<Events.ErrorReplyEvent>? ErrorReplyReceived;
    /// <summary>Raised when the server's Message of the Day has been fully received.</summary>
    event EventHandler<Events.MotdReceivedEvent>? MotdReceived;
}
