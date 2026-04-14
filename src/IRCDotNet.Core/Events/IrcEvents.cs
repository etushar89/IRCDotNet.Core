using IRCDotNet.Core.Protocol;

namespace IRCDotNet.Core.Events;

/// <summary>
/// Base class for all IRC events.
/// </summary>
public abstract class IrcEvent
{
    /// <summary>
    /// The original IRC message that triggered this event.
    /// </summary>
    public IrcMessage Message { get; }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initializes the event from the raw IRC message.
    /// </summary>
    /// <param name="message">The raw IRC message that triggered this event.</param>
    protected IrcEvent(IrcMessage message)
    {
        Message = message;
    }
}

/// <summary>
/// Event raised when a user joins a channel.
/// </summary>
public class UserJoinedChannelEvent : IrcEvent
{
    /// <summary>Nickname of the user who joined.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Channel that was joined.</summary>
    public string Channel { get; }

    /// <summary>
    /// Initializes a new <see cref="UserJoinedChannelEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user who joined.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="channel">Channel that was joined.</param>
    public UserJoinedChannelEvent(IrcMessage message, string nick, string user, string host, string channel)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Channel = channel;
    }
}

/// <summary>
/// Event raised when a user leaves a channel.
/// </summary>
public class UserLeftChannelEvent : IrcEvent
{
    /// <summary>Nickname of the user who left.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Channel that was left.</summary>
    public string Channel { get; }
    /// <summary>Optional part message.</summary>
    public string? Reason { get; }

    /// <summary>
    /// Initializes a new <see cref="UserLeftChannelEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user who left.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="channel">Channel that was left.</param>
    /// <param name="reason">Optional part message.</param>
    public UserLeftChannelEvent(IrcMessage message, string nick, string user, string host, string channel, string? reason = null)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Channel = channel;
        Reason = reason;
    }
}

/// <summary>
/// Event raised when a user quits the network.
/// </summary>
public class UserQuitEvent : IrcEvent
{
    /// <summary>Nickname of the user who quit.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Optional quit message.</summary>
    public string? Reason { get; }

    /// <summary>
    /// Initializes a new <see cref="UserQuitEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user who quit.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="reason">Optional quit message.</param>
    public UserQuitEvent(IrcMessage message, string nick, string user, string host, string? reason = null)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Reason = reason;
    }
}

/// <summary>
/// Event raised when a private message is received.
/// </summary>
public class PrivateMessageEvent : IrcEvent
{
    /// <summary>Nickname of the sender.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the sender.</summary>
    public string User { get; }
    /// <summary>Hostname of the sender.</summary>
    public string Host { get; }
    /// <summary>Target of the message (channel name or the client's own nickname for PMs).</summary>
    public string Target { get; }
    /// <summary>The message text.</summary>
    public string Text { get; }
    /// <summary>Whether this message was sent to a channel (vs. a private message).</summary>
    public bool IsChannelMessage => Target.StartsWith('#') || Target.StartsWith('&');
    /// <summary>Whether this message is an echo of the client's own message (requires echo-message capability).</summary>
    public bool IsEcho { get; }

    /// <summary>
    /// Initializes a new <see cref="PrivateMessageEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the sender.</param>
    /// <param name="user">Username (ident) of the sender.</param>
    /// <param name="host">Hostname of the sender.</param>
    /// <param name="target">Target of the message (channel or nickname).</param>
    /// <param name="text">The message text.</param>
    /// <param name="isEcho">Whether this is an echo of the client's own message.</param>
    public PrivateMessageEvent(IrcMessage message, string nick, string user, string host, string target, string text, bool isEcho = false)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        Text = text;
        IsEcho = isEcho;
    }
}

/// <summary>
/// Event raised when a notice is received.
/// </summary>
public class NoticeEvent : IrcEvent
{
    /// <summary>Nickname of the sender (or server name for server notices).</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the sender.</summary>
    public string User { get; }
    /// <summary>Hostname of the sender.</summary>
    public string Host { get; }
    /// <summary>Target of the notice (channel name or the client's own nickname).</summary>
    public string Target { get; }
    /// <summary>The notice text.</summary>
    public string Text { get; }

    /// <summary>
    /// Initializes a new <see cref="NoticeEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the sender.</param>
    /// <param name="user">Username (ident) of the sender.</param>
    /// <param name="host">Hostname of the sender.</param>
    /// <param name="target">Target of the notice.</param>
    /// <param name="text">The notice text.</param>
    public NoticeEvent(IrcMessage message, string nick, string user, string host, string target, string text)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        Text = text;
    }
}

/// <summary>
/// Event raised when the client successfully connects and registers.
/// </summary>
public class ConnectedEvent : IrcEvent
{
    /// <summary>Network name reported by the server.</summary>
    public string Network { get; }
    /// <summary>The nickname assigned to the client after registration.</summary>
    public string Nick { get; }

    /// <summary>
    /// Initializes a new <see cref="ConnectedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="network">Network name reported by the server.</param>
    /// <param name="nick">The nickname assigned to the client.</param>
    public ConnectedEvent(IrcMessage message, string network, string nick)
        : base(message)
    {
        Network = network;
        Nick = nick;
    }
}

/// <summary>
/// Event raised when the client disconnects.
/// </summary>
public class DisconnectedEvent : IrcEvent
{
    /// <summary>The reason for the disconnection, if available.</summary>
    public string? Reason { get; }

    /// <summary>
    /// Initializes a new <see cref="DisconnectedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="reason">Optional disconnection reason.</param>
    public DisconnectedEvent(IrcMessage message, string? reason = null)
        : base(message)
    {
        Reason = reason;
    }
}

/// <summary>
/// Event raised when a channel topic changes.
/// </summary>
public class TopicChangedEvent : IrcEvent
{
    /// <summary>Channel whose topic changed.</summary>
    public string Channel { get; }
    /// <summary>The new topic text, or <c>null</c> if the topic was cleared.</summary>
    public string? Topic { get; }
    /// <summary>Nickname of the user who set the topic, if available.</summary>
    public string? SetBy { get; }

    /// <summary>
    /// Initializes a new <see cref="TopicChangedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel whose topic changed.</param>
    /// <param name="topic">The new topic text.</param>
    /// <param name="setBy">Nickname of the user who set the topic.</param>
    public TopicChangedEvent(IrcMessage message, string channel, string? topic, string? setBy = null)
        : base(message)
    {
        Channel = channel;
        Topic = topic;
        SetBy = setBy;
    }
}

/// <summary>
/// Event raised when a user is kicked from a channel.
/// </summary>
public class UserKickedEvent : IrcEvent
{
    /// <summary>Channel the user was kicked from.</summary>
    public string Channel { get; }
    /// <summary>Nickname of the user who was kicked.</summary>
    public string KickedNick { get; }
    /// <summary>Nickname of the operator who performed the kick.</summary>
    public string KickedByNick { get; }
    /// <summary>Optional kick reason.</summary>
    public string? Reason { get; }

    /// <summary>
    /// Initializes a new <see cref="UserKickedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel the user was kicked from.</param>
    /// <param name="kickedNick">Nickname of the user who was kicked.</param>
    /// <param name="kickedByNick">Nickname of the operator who kicked.</param>
    /// <param name="reason">Optional kick reason.</param>
    public UserKickedEvent(IrcMessage message, string channel, string kickedNick, string kickedByNick, string? reason = null)
        : base(message)
    {
        Channel = channel;
        KickedNick = kickedNick;
        KickedByNick = kickedByNick;
        Reason = reason;
    }
}

/// <summary>
/// Event raised when a nick change occurs.
/// </summary>
public class NickChangedEvent : IrcEvent
{
    /// <summary>The user's previous nickname.</summary>
    public string OldNick { get; }
    /// <summary>The user's new nickname.</summary>
    public string NewNick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }

    /// <summary>
    /// Initializes a new <see cref="NickChangedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="oldNick">The user's previous nickname.</param>
    /// <param name="newNick">The user's new nickname.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    public NickChangedEvent(IrcMessage message, string oldNick, string newNick, string user, string host)
        : base(message)
    {
        OldNick = oldNick;
        NewNick = newNick;
        User = user;
        Host = host;
    }
}

/// <summary>
/// Event raised when a nickname collision occurs (ERR_NICKCOLLISION).
/// </summary>
public class NicknameCollisionEvent : IrcEvent
{
    /// <summary>The nickname that collided.</summary>
    public string CollidingNick { get; }
    /// <summary>The nickname that was attempted, if different from <see cref="CollidingNick"/>.</summary>
    public string? AttemptedNick { get; }
    /// <summary>The fallback nickname the client switched to, if any.</summary>
    public string? FallbackNick { get; }
    /// <summary>Whether the client was already registered when the collision occurred.</summary>
    public bool IsRegistered { get; }

    /// <summary>
    /// Initializes a new <see cref="NicknameCollisionEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="collidingNick">The nickname that collided.</param>
    /// <param name="attemptedNick">The nickname that was attempted.</param>
    /// <param name="fallbackNick">The fallback nickname chosen.</param>
    /// <param name="isRegistered">Whether the client was already registered.</param>
    public NicknameCollisionEvent(IrcMessage message, string collidingNick, string? attemptedNick = null, string? fallbackNick = null, bool isRegistered = false)
        : base(message)
    {
        CollidingNick = collidingNick;
        AttemptedNick = attemptedNick;
        FallbackNick = fallbackNick;
        IsRegistered = isRegistered;
    }
}

/// <summary>
/// Event raised when channel users list is received.
/// </summary>
public class ChannelUsersEvent : IrcEvent
{
    /// <summary>Channel the user list belongs to.</summary>
    public string Channel { get; }
    /// <summary>List of users in the channel with their privilege prefixes.</summary>
    public List<ChannelUser> Users { get; }

    /// <summary>
    /// Initializes a new <see cref="ChannelUsersEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel the user list belongs to.</param>
    /// <param name="users">List of users with their privileges.</param>
    public ChannelUsersEvent(IrcMessage message, string channel, List<ChannelUser> users)
        : base(message)
    {
        Channel = channel;
        Users = users;
    }
}

/// <summary>
/// Represents a user in a channel with their privilege prefixes.
/// </summary>
public class ChannelUser
{
    /// <summary>The user's nickname.</summary>
    public string Nick { get; set; } = string.Empty;
    /// <summary>Privilege prefix characters for this user (e.g. '@', '+', '%').</summary>
    public List<char> Prefixes { get; set; } = new();
    /// <summary>Whether the user has operator (@) status.</summary>
    public bool IsOperator => Prefixes.Contains('@');
    /// <summary>Whether the user has voice (+) status.</summary>
    public bool IsVoiced => Prefixes.Contains('+');
    /// <summary>Whether the user has half-operator (%) status.</summary>
    public bool IsHalfOperator => Prefixes.Contains('%');
}

/// <summary>
/// Extended user information for IRCv3 tracking.
/// </summary>
public class UserInfo
{
    /// <summary>The user's nickname.</summary>
    public string Nick { get; set; } = string.Empty;
    /// <summary>The user's username (ident).</summary>
    public string User { get; set; } = string.Empty;
    /// <summary>The user's hostname.</summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>The user's services account name, or <c>null</c> if not identified.</summary>
    public string? Account { get; set; }
    /// <summary>The user's real name (GECOS field).</summary>
    public string? RealName { get; set; }
    /// <summary>Whether the user is currently marked as away.</summary>
    public bool IsAway { get; set; }
    /// <summary>The user's away message, if set.</summary>
    public string? AwayMessage { get; set; }
    /// <summary>Timestamp of the last activity seen from this user.</summary>
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Additional extended information key-value pairs.</summary>
    public Dictionary<string, string> ExtendedInfo { get; set; } = new();
}

/// <summary>
/// Event raised when a raw IRC message is received.
/// </summary>
public class RawMessageEvent : IrcEvent
{
    /// <summary>
    /// Initializes a new <see cref="RawMessageEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    public RawMessageEvent(IrcMessage message) : base(message)
    {
    }
}

/// <summary>
/// Event raised when joining a channel fails (banned, invite-only, full, bad key, etc.).
/// </summary>
public class ChannelJoinFailedEvent : IrcEvent
{
    /// <summary>Channel that could not be joined.</summary>
    public string Channel { get; }
    /// <summary>Human-readable reason for the failure.</summary>
    public string Reason { get; }
    /// <summary>IRC numeric error code (e.g. "473" for invite-only).</summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Initializes a new <see cref="ChannelJoinFailedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel that could not be joined.</param>
    /// <param name="reason">Human-readable failure reason.</param>
    /// <param name="errorCode">IRC numeric error code.</param>
    public ChannelJoinFailedEvent(IrcMessage message, string channel, string reason, string errorCode) : base(message)
    {
        Channel = channel;
        Reason = reason;
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Event raised when IRCv3 capabilities are negotiated.
/// </summary>
public class CapabilitiesNegotiatedEvent : IrcEvent
{
    /// <summary>Set of capabilities that were successfully enabled.</summary>
    public HashSet<string> EnabledCapabilities { get; }
    /// <summary>Set of capabilities the server advertised as available.</summary>
    public HashSet<string> SupportedCapabilities { get; }

    /// <summary>
    /// Initializes a new <see cref="CapabilitiesNegotiatedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="enabledCapabilities">Capabilities that were enabled.</param>
    /// <param name="supportedCapabilities">Capabilities the server supports.</param>
    public CapabilitiesNegotiatedEvent(IrcMessage message, HashSet<string> enabledCapabilities, HashSet<string> supportedCapabilities)
        : base(message)
    {
        EnabledCapabilities = new HashSet<string>(enabledCapabilities);
        SupportedCapabilities = new HashSet<string>(supportedCapabilities);
    }
}

/// <summary>
/// Event raised when a user's away status changes (away-notify capability).
/// </summary>
public class UserAwayStatusChangedEvent : IrcEvent
{
    /// <summary>Nickname of the user whose away status changed.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Whether the user is now away.</summary>
    public bool IsAway { get; }
    /// <summary>The user's away message, or <c>null</c> if returning from away.</summary>
    public string? AwayMessage { get; }

    /// <summary>
    /// Initializes a new <see cref="UserAwayStatusChangedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="isAway">Whether the user is now away.</param>
    /// <param name="awayMessage">The away message, if any.</param>
    public UserAwayStatusChangedEvent(IrcMessage message, string nick, string user, string host, bool isAway, string? awayMessage = null)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        IsAway = isAway;
        AwayMessage = awayMessage;
    }
}

/// <summary>
/// Event raised when a user's account status changes (account-notify capability).
/// </summary>
public class UserAccountChangedEvent : IrcEvent
{
    /// <summary>Nickname of the user.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>The user's new account name, or <c>null</c> if they logged out.</summary>
    public string? Account { get; }

    /// <summary>
    /// Initializes a new <see cref="UserAccountChangedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="account">The user's new account name, or <c>null</c> if logged out.</param>
    public UserAccountChangedEvent(IrcMessage message, string nick, string user, string host, string? account)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Account = account;
    }
}

/// <summary>
/// Event raised when a user joins with extended information (extended-join capability).
/// </summary>
public class ExtendedUserJoinedChannelEvent : UserJoinedChannelEvent
{
    /// <summary>The user's services account name, or <c>null</c> if not identified.</summary>
    public string? Account { get; }
    /// <summary>The user's real name (GECOS field).</summary>
    public string? RealName { get; }

    /// <summary>
    /// Initializes a new <see cref="ExtendedUserJoinedChannelEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user who joined.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="channel">Channel that was joined.</param>
    /// <param name="account">The user's services account name.</param>
    /// <param name="realName">The user's real name (GECOS).</param>
    public ExtendedUserJoinedChannelEvent(IrcMessage message, string nick, string user, string host, string channel, string? account, string? realName)
        : base(message, nick, user, host, channel)
    {
        Account = account;
        RealName = realName;
    }
}

/// <summary>
/// Event raised when a user's hostname changes (chghost capability).
/// </summary>
public class UserHostnameChangedEvent : IrcEvent
{
    /// <summary>Nickname of the user.</summary>
    public string Nick { get; }
    /// <summary>The user's previous username (ident).</summary>
    public string OldUser { get; }
    /// <summary>The user's previous hostname.</summary>
    public string OldHost { get; }
    /// <summary>The user's new username (ident).</summary>
    public string NewUser { get; }
    /// <summary>The user's new hostname.</summary>
    public string NewHost { get; }

    /// <summary>
    /// Initializes a new <see cref="UserHostnameChangedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="oldUser">Previous username (ident).</param>
    /// <param name="oldHost">Previous hostname.</param>
    /// <param name="newUser">New username (ident).</param>
    /// <param name="newHost">New hostname.</param>
    public UserHostnameChangedEvent(IrcMessage message, string nick, string oldUser, string oldHost, string newUser, string newHost)
        : base(message)
    {
        Nick = nick;
        OldUser = oldUser;
        OldHost = oldHost;
        NewUser = newUser;
        NewHost = newHost;
    }
}

/// <summary>
/// Event raised when batch processing starts/ends (batch capability).
/// </summary>
public class BatchEvent : IrcEvent
{
    /// <summary>Server-assigned batch identifier.</summary>
    public string BatchId { get; }
    /// <summary>Batch type (e.g. "chathistory", "netsplit"), or <c>null</c> for end-of-batch.</summary>
    public string? BatchType { get; }
    /// <summary>Additional parameters associated with this batch.</summary>
    public List<string> Parameters { get; }
    /// <summary>Whether this event starts a batch (<c>true</c>) or ends it (<c>false</c>).</summary>
    public bool IsStarting { get; }

    /// <summary>
    /// Initializes a new <see cref="BatchEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="batchId">Server-assigned batch identifier.</param>
    /// <param name="batchType">Batch type label.</param>
    /// <param name="parameters">Additional batch parameters.</param>
    /// <param name="isStarting">Whether this starts or ends the batch.</param>
    public BatchEvent(IrcMessage message, string batchId, string? batchType, List<string> parameters, bool isStarting)
        : base(message)
    {
        BatchId = batchId;
        BatchType = batchType;
        Parameters = new List<string>(parameters);
        IsStarting = isStarting;
    }
}

/// <summary>
/// Event raised for SASL authentication events.
/// </summary>
public class SaslAuthenticationEvent : IrcEvent
{
    /// <summary>Whether SASL authentication succeeded.</summary>
    public bool IsSuccessful { get; }
    /// <summary>SASL mechanism used (e.g. "PLAIN", "EXTERNAL").</summary>
    public string? Mechanism { get; }
    /// <summary>Error message from the server if authentication failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Initializes a new <see cref="SaslAuthenticationEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="isSuccessful">Whether authentication succeeded.</param>
    /// <param name="mechanism">SASL mechanism used.</param>
    /// <param name="errorMessage">Error message if authentication failed.</param>
    public SaslAuthenticationEvent(IrcMessage message, bool isSuccessful, string? mechanism = null, string? errorMessage = null)
        : base(message)
    {
        IsSuccessful = isSuccessful;
        Mechanism = mechanism;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event raised when message tags are available with additional context.
/// </summary>
public class MessageTagsEvent : IrcEvent
{
    /// <summary>All IRCv3 tags attached to this message.</summary>
    public Dictionary<string, string?> Tags { get; }
    /// <summary>Server-provided timestamp from the <c>time</c> tag, if present.</summary>
    public DateTimeOffset? ServerTime { get; }
    /// <summary>Unique message identifier from the <c>msgid</c> tag, if present.</summary>
    public string? MessageId { get; }
    /// <summary>Sender's account name from the <c>account</c> tag, if present.</summary>
    public string? Account { get; }

    /// <summary>
    /// Initializes a new <see cref="MessageTagsEvent"/> by parsing common tags from the message.
    /// </summary>
    /// <param name="message">The raw IRC message containing tags.</param>
    public MessageTagsEvent(IrcMessage message) : base(message)
    {
        Tags = new Dictionary<string, string?>(message.Tags);

        // Parse common tags
        if (message.Tags.TryGetValue("time", out var timeValue) && !string.IsNullOrEmpty(timeValue))
        {
            if (DateTimeOffset.TryParse(timeValue, out var serverTime))
                ServerTime = serverTime;
        }

        if (message.Tags.TryGetValue("msgid", out var msgId))
            MessageId = msgId;

        if (message.Tags.TryGetValue("account", out var account))
            Account = account;
    }
}

/// <summary>
/// Event raised when a WHO reply is received.
/// </summary>
public class WhoReceivedEvent : IrcEvent
{
    /// <summary>Channel the user is in.</summary>
    public string Channel { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Server the user is connected to.</summary>
    public string Server { get; }
    /// <summary>Nickname of the user.</summary>
    public string Nick { get; }
    /// <summary>WHO flags (e.g. "H" for here, "G" for gone/away, may include "@" or "+").</summary>
    public string Flags { get; }
    /// <summary>The user's real name (GECOS field).</summary>
    public string RealName { get; }
    /// <summary>Whether the user is currently marked as away.</summary>
    public bool IsAway { get; }

    /// <summary>
    /// Initializes a new <see cref="WhoReceivedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel the user is in.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="server">Server the user is connected to.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="flags">WHO flags string.</param>
    /// <param name="realName">The user's real name.</param>
    /// <param name="isAway">Whether the user is away.</param>
    public WhoReceivedEvent(IrcMessage message, string channel, string user, string host,
        string server, string nick, string flags, string realName, bool isAway)
        : base(message)
    {
        Channel = channel;
        User = user;
        Host = host;
        Server = server;
        Nick = nick;
        Flags = flags;
        RealName = realName;
        IsAway = isAway;
    }
}

/// <summary>
/// Event raised when a WHOWAS reply is received.
/// </summary>
public class WhoWasReceivedEvent : IrcEvent
{
    /// <summary>Nickname that was looked up.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) the user had.</summary>
    public string User { get; }
    /// <summary>Hostname the user had.</summary>
    public string Host { get; }
    /// <summary>The user's real name (GECOS field).</summary>
    public string RealName { get; }

    /// <summary>
    /// Initializes a new <see cref="WhoWasReceivedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname that was looked up.</param>
    /// <param name="user">Username (ident) the user had.</param>
    /// <param name="host">Hostname the user had.</param>
    /// <param name="realName">The user's real name.</param>
    public WhoWasReceivedEvent(IrcMessage message, string nick, string user, string host, string realName)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        RealName = realName;
    }
}

/// <summary>
/// Event raised when a channel list entry is received.
/// </summary>
public class ChannelListReceivedEvent : IrcEvent
{
    /// <summary>Channel name.</summary>
    public string Channel { get; }
    /// <summary>Number of users currently in the channel.</summary>
    public int UserCount { get; }
    /// <summary>The channel's topic.</summary>
    public string Topic { get; }

    /// <summary>
    /// Initializes a new <see cref="ChannelListReceivedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="channel">Channel name.</param>
    /// <param name="userCount">Number of users in the channel.</param>
    /// <param name="topic">The channel's topic.</param>
    public ChannelListReceivedEvent(IrcMessage message, string channel, int userCount, string topic)
        : base(message)
    {
        Channel = channel;
        UserCount = userCount;
        Topic = topic;
    }
}

/// <summary>
/// Event raised when the channel list response is complete (RPL_LISTEND 323).
/// </summary>
public class ChannelListEndEvent : IrcEvent
{
    /// <summary>
    /// Initializes a new <see cref="ChannelListEndEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    public ChannelListEndEvent(IrcMessage message) : base(message) { }
}

/// <summary>
/// Represents a single channel entry from a LIST response.
/// </summary>
public class ChannelListEntry
{
    /// <summary>Channel name.</summary>
    public string Channel { get; }
    /// <summary>Number of users currently in the channel.</summary>
    public int UserCount { get; }
    /// <summary>The channel's topic.</summary>
    public string Topic { get; }

    /// <summary>
    /// Initializes a new <see cref="ChannelListEntry"/>.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <param name="userCount">Number of users in the channel.</param>
    /// <param name="topic">The channel's topic.</param>
    public ChannelListEntry(string channel, int userCount, string topic)
    {
        Channel = channel;
        UserCount = userCount;
        Topic = topic;
    }
}

// CTCP (Client-To-Client Protocol) Events

/// <summary>
/// Event raised when a CTCP request is received (PRIVMSG with content wrapped in SOH/\u0001 delimiters).
/// The library auto-replies to VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, and ERRMSG by default
/// (controlled by <see cref="Configuration.IrcClientOptions.EnableCtcpAutoReply"/>).
/// ACTION requests are handled separately and fire <see cref="CtcpActionEvent"/> instead.
/// </summary>
public class CtcpRequestEvent : IrcEvent
{
    /// <summary>Nickname of the user who sent the CTCP request.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the sender.</summary>
    public string User { get; }
    /// <summary>Hostname of the sender.</summary>
    public string Host { get; }
    /// <summary>Target of the request (channel name or the client's own nickname).</summary>
    public string Target { get; }
    /// <summary>The CTCP command (e.g. <c>"VERSION"</c>, <c>"PING"</c>, <c>"TIME"</c>).</summary>
    public string Command { get; }
    /// <summary>The CTCP parameter text after the command, or <see cref="string.Empty"/> if none.</summary>
    public string Parameters { get; }

    /// <summary>
    /// Initializes a new <see cref="CtcpRequestEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the sender.</param>
    /// <param name="user">Username (ident) of the sender.</param>
    /// <param name="host">Hostname of the sender.</param>
    /// <param name="target">Target of the request.</param>
    /// <param name="command">The CTCP command name.</param>
    /// <param name="parameters">Parameters after the command.</param>
    public CtcpRequestEvent(IrcMessage message, string nick, string user, string host, string target, string command, string parameters)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        Command = command;
        Parameters = parameters;
    }
}

/// <summary>
/// Event raised when a CTCP reply is received (NOTICE with content wrapped in SOH/\u0001 delimiters).
/// This is the response to a CTCP request sent via <see cref="IrcClient.SendCtcpRequestAsync"/>.
/// </summary>
public class CtcpReplyEvent : IrcEvent
{
    /// <summary>Nickname of the user who sent the CTCP reply.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the sender.</summary>
    public string User { get; }
    /// <summary>Hostname of the sender.</summary>
    public string Host { get; }
    /// <summary>The CTCP command this is a reply to (e.g. <c>"VERSION"</c>, <c>"PING"</c>).</summary>
    public string Command { get; }
    /// <summary>The reply payload text (e.g. <c>"IRCDotNet.Core 2.0.2.0"</c> for VERSION, or a timestamp for PING).</summary>
    public string ReplyText { get; }

    /// <summary>
    /// Initializes a new <see cref="CtcpReplyEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the sender.</param>
    /// <param name="user">Username (ident) of the sender.</param>
    /// <param name="host">Hostname of the sender.</param>
    /// <param name="command">The CTCP command name.</param>
    /// <param name="replyText">The reply text.</param>
    public CtcpReplyEvent(IrcMessage message, string nick, string user, string host, string command, string replyText)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Command = command;
        ReplyText = replyText;
    }
}

/// <summary>
/// Event raised when a CTCP ACTION is received (<c>/me</c> command).
/// ACTION is a special CTCP that describes an action performed by the user.
/// </summary>
public class CtcpActionEvent : IrcEvent
{
    /// <summary>Nickname of the user who performed the action.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Where the action was performed (channel name or the client's own nickname for private actions).</summary>
    public string Target { get; }
    /// <summary>The action text (e.g. <c>"waves hello"</c> from <c>/me waves hello</c>).</summary>
    public string ActionText { get; }
    /// <summary>Whether this action was performed in a channel (vs. a private message).</summary>
    public bool IsChannelAction => Target.StartsWith('#') || Target.StartsWith('&');

    /// <summary>
    /// Initializes a new <see cref="CtcpActionEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="target">Where the action was performed.</param>
    /// <param name="actionText">The action text.</param>
    public CtcpActionEvent(IrcMessage message, string nick, string user, string host, string target, string actionText)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        ActionText = actionText;
    }
}

/// <summary>
/// Event raised when the client is invited to a channel.
/// </summary>
public class InviteReceivedEvent : IrcEvent
{
    /// <summary>Nickname of the user who sent the invite.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the inviting user.</summary>
    public string User { get; }
    /// <summary>Hostname of the inviting user.</summary>
    public string Host { get; }
    /// <summary>Channel the client was invited to.</summary>
    public string Channel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteReceivedEvent"/> class.
    /// </summary>
    public InviteReceivedEvent(IrcMessage message, string nick, string user, string host, string channel)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Channel = channel;
    }
}

/// <summary>
/// Event raised when the client's own away status is confirmed by the server
/// (RPL_UNAWAY 305 or RPL_NOWAWAY 306).
/// </summary>
public class OwnAwayStatusChangedEvent : IrcEvent
{
    /// <summary>Whether the client is now marked as away.</summary>
    public bool IsAway { get; }
    /// <summary>Server message (e.g. "You have been marked as being away").</summary>
    public string ServerMessage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnAwayStatusChangedEvent"/> class.
    /// </summary>
    public OwnAwayStatusChangedEvent(IrcMessage message, bool isAway, string serverMessage)
        : base(message)
    {
        IsAway = isAway;
        ServerMessage = serverMessage;
    }
}

/// <summary>
/// Event raised when channel mode information is received (RPL_CHANNELMODEIS 324).
/// </summary>
public class ChannelModeIsEvent : IrcEvent
{
    /// <summary>Channel name.</summary>
    public string Channel { get; }
    /// <summary>Current mode string (e.g. "+nt").</summary>
    public string Modes { get; }
    /// <summary>Mode parameters, if any.</summary>
    public string ModeParams { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelModeIsEvent"/> class.
    /// </summary>
    public ChannelModeIsEvent(IrcMessage message, string channel, string modes, string modeParams)
        : base(message)
    {
        Channel = channel;
        Modes = modes;
        ModeParams = modeParams;
    }
}

/// <summary>
/// Event raised for a general IRC error reply from the server.
/// Covers errors like ERR_CHANOPRIVSNEEDED (482), ERR_NOTONCHANNEL (442),
/// ERR_NEEDMOREPARAMS (461), etc.
/// </summary>
public class ErrorReplyEvent : IrcEvent
{
    /// <summary>The numeric error code (e.g. "482").</summary>
    public string ErrorCode { get; }
    /// <summary>The target or subject of the error (channel name, command name, etc.).</summary>
    public string Target { get; }
    /// <summary>Human-readable error message from the server.</summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorReplyEvent"/> class.
    /// </summary>
    public ErrorReplyEvent(IrcMessage message, string errorCode, string target, string errorMessage)
        : base(message)
    {
        ErrorCode = errorCode;
        Target = target;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Typing state values for the <c>+typing</c> client tag (IRCv3).
/// </summary>
public enum TypingState
{
    /// <summary>The user is actively typing.</summary>
    Active,
    /// <summary>The user paused typing but has not cleared their input.</summary>
    Paused,
    /// <summary>The user cleared their input without sending a message.</summary>
    Done
}

/// <summary>
/// Event raised when a <c>+typing</c> notification is received via TAGMSG (IRCv3 client tag).
/// Indicates whether a user is currently typing in a channel or private message.
/// </summary>
/// <remarks>
/// <para>Clients should assume the sender is still typing until one of:</para>
/// <list type="bullet">
///   <item>A message is received from the sender</item>
///   <item>The sender leaves the channel or quits</item>
///   <item>A <c>typing=done</c> notification is received</item>
///   <item>6 seconds elapse since the last <c>typing=active</c> notification</item>
///   <item>30 seconds elapse since the last <c>typing=paused</c> notification</item>
/// </list>
/// </remarks>
public class TypingIndicatorEvent : IrcEvent
{
    /// <summary>Nickname of the user whose typing state changed.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Where the user is typing (channel name or the client's own nickname for private messages).</summary>
    public string Target { get; }
    /// <summary>The typing state: <see cref="TypingState.Active"/>, <see cref="TypingState.Paused"/>, or <see cref="TypingState.Done"/>.</summary>
    public TypingState State { get; }
    /// <summary>Whether this typing notification is for a channel (vs. a private message).</summary>
    public bool IsChannelTyping => Target.StartsWith('#') || Target.StartsWith('&');

    /// <summary>
    /// Initializes a new <see cref="TypingIndicatorEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="nick">Nickname of the user.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="target">Channel name or nickname where the user is typing.</param>
    /// <param name="state">The typing state.</param>
    public TypingIndicatorEvent(IrcMessage message, string nick, string user, string host, string target, TypingState state)
        : base(message)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        State = state;
    }
}
