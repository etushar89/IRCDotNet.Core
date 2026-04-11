namespace IRCDotNet.Core.Protocol;

/// <summary>
/// IRCv3 capability name constants for use with CAP negotiation.
/// </summary>
public static class IrcCapabilities
{
    // Core IRCv3 capabilities

    /// <summary>Include all prefix modes (e.g. <c>@+</c>) in NAMES replies, not just the highest.</summary>
    public const string MULTI_PREFIX = "multi-prefix";
    /// <summary>Receive real-time AWAY notifications when users in shared channels change away status.</summary>
    public const string AWAY_NOTIFY = "away-notify";
    /// <summary>Receive notifications when users in shared channels log in or out of services accounts.</summary>
    public const string ACCOUNT_NOTIFY = "account-notify";
    /// <summary>JOIN messages include the user's account name and real name.</summary>
    public const string EXTENDED_JOIN = "extended-join";
    /// <summary>Messages include a <c>time</c> tag with the server-side timestamp.</summary>
    public const string SERVER_TIME = "server-time";
    /// <summary>Receive CAP NEW/DEL when the server adds or removes capabilities at runtime.</summary>
    public const string CAP_NOTIFY = "cap-notify";
    /// <summary>Receive notifications when a user's displayed hostname or ident changes.</summary>
    public const string CHGHOST = "chghost";
    /// <summary>Receive notifications when a user is invited to a channel you are in.</summary>
    public const string INVITE_NOTIFY = "invite-notify";
    /// <summary>NAMES replies include full <c>user@host</c> for each nick.</summary>
    public const string USERHOST_IN_NAMES = "userhost-in-names";

    // Message tags

    /// <summary>Enable sending and receiving arbitrary IRCv3 message tags.</summary>
    public const string MESSAGE_TAGS = "message-tags";
    /// <summary>Enable grouping related messages into labeled batches.</summary>
    public const string BATCH = "batch";
    /// <summary>Server echoes a label on responses to identify which command triggered them.</summary>
    public const string LABELED_RESPONSE = "labeled-response";
    /// <summary>Server echoes the client's own PRIVMSG/NOTICE back, confirming delivery.</summary>
    public const string ECHO_MESSAGE = "echo-message";

    // SASL

    /// <summary>Enable SASL authentication during connection registration.</summary>
    public const string SASL = "sasl";

    // Advanced capabilities

    /// <summary>Track online/offline status of specific nicknames via the MONITOR command.</summary>
    public const string MONITOR = "monitor";
    /// <summary>Change the client's real name (GECOS) without reconnecting via the SETNAME command.</summary>
    public const string SETNAME = "setname";
    /// <summary>Messages include an <c>account</c> tag with the sender's services account name.</summary>
    public const string ACCOUNT_TAG = "account-tag";
    /// <summary>Request message history from the server using the CHATHISTORY command.</summary>
    public const string CHATHISTORY = "chathistory";
    /// <summary>Draft/experimental version of the chathistory capability.</summary>
    public const string DRAFT_CHATHISTORY = "draft/chathistory";

    // Vendor-specific capabilities

    /// <summary>ZNC bouncer: request buffered messages from the playback module.</summary>
    public const string ZNC_PLAYBACK = "znc.in/playback";
    /// <summary>ZNC bouncer: receive echoes of your own messages sent from other clients.</summary>
    public const string ZNC_SELF_MESSAGE = "znc.in/self-message";

    /// <summary>
    /// Default recommended set of IRCv3 capabilities requested during CAP negotiation.
    /// </summary>
    public static readonly string[] DefaultCapabilities =
    {
        MULTI_PREFIX,
        AWAY_NOTIFY,
        ACCOUNT_NOTIFY,
        EXTENDED_JOIN,
        SERVER_TIME,
        CAP_NOTIFY,
        CHGHOST,
        INVITE_NOTIFY,
        MESSAGE_TAGS,
        BATCH,
        LABELED_RESPONSE,
        ECHO_MESSAGE,
        SASL,
        MONITOR,
        SETNAME,
        ACCOUNT_TAG
    };
}

/// <summary>
/// SASL authentication mechanism name constants.
/// </summary>
public static class SaslMechanisms
{
    /// <summary>PLAIN mechanism: credentials sent as base64-encoded <c>\0username\0password</c>.</summary>
    public const string PLAIN = "PLAIN";
    /// <summary>EXTERNAL mechanism: authentication via client TLS certificate (no password).</summary>
    public const string EXTERNAL = "EXTERNAL";
    /// <summary>SCRAM-SHA-1 mechanism: challenge-response with SHA-1 hashing.</summary>
    public const string SCRAM_SHA_1 = "SCRAM-SHA-1";
    /// <summary>SCRAM-SHA-256 mechanism: challenge-response with SHA-256 hashing.</summary>
    public const string SCRAM_SHA_256 = "SCRAM-SHA-256";
    /// <summary>OAUTHBEARER mechanism: authentication via OAuth 2.0 bearer token.</summary>
    public const string OAUTHBEARER = "OAUTHBEARER";
}

/// <summary>
/// IRCv3 message tag key constants for use with <see cref="IrcMessage.Tags"/>.
/// </summary>
public static class MessageTags
{
    /// <summary>Server-side timestamp in ISO 8601 format (requires <c>server-time</c> capability).</summary>
    public const string TIME = "time";
    /// <summary>Unique message identifier assigned by the server.</summary>
    public const string MSGID = "msgid";
    /// <summary>Services account name of the message sender.</summary>
    public const string ACCOUNT = "account";
    /// <summary>Batch identifier grouping this message with related messages.</summary>
    public const string BATCH = "batch";
    /// <summary>Label echoed back by the server to match a response to its originating command.</summary>
    public const string LABEL = "label";
    /// <summary>Indicates this message is an echo of the client's own outgoing message.</summary>
    public const string ECHO = "echo-message";
    /// <summary>Client tag indicating the user is currently typing in a conversation.</summary>
    public const string TYPING = "+typing";
    /// <summary>Client tag referencing the <c>msgid</c> of the message being replied to.</summary>
    public const string REPLY = "+draft/reply";
    /// <summary>Client tag containing a reaction emoji for the referenced message.</summary>
    public const string REACT = "+draft/react";

    /// <summary>
    /// Prefix character for client-only tags (tags that start with <c>+</c> are not forwarded by the server).
    /// </summary>
    public const string CLIENT_PREFIX = "+";
}
