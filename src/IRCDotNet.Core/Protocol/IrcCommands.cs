namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Standard IRC command name constants as defined in RFC 1459 and the Modern IRC Client Protocol.
/// </summary>
public static class IrcCommands
{
    // Connection Registration

    /// <summary>IRCv3 capability negotiation (CAP LS, CAP REQ, CAP ACK, CAP END).</summary>
    public const string CAP = "CAP";
    /// <summary>SASL authentication data exchange during capability negotiation.</summary>
    public const string AUTHENTICATE = "AUTHENTICATE";
    /// <summary>Send server connection password before registration.</summary>
    public const string PASS = "PASS";
    /// <summary>Set or change the client's nickname.</summary>
    public const string NICK = "NICK";
    /// <summary>Register username, hostname, and real name with the server.</summary>
    public const string USER = "USER";
    /// <summary>Server-to-client keepalive — client must respond with PONG.</summary>
    public const string PING = "PING";
    /// <summary>Response to a server PING, or client-initiated latency check.</summary>
    public const string PONG = "PONG";
    /// <summary>Authenticate as an IRC operator.</summary>
    public const string OPER = "OPER";
    /// <summary>Disconnect from the server with an optional quit message.</summary>
    public const string QUIT = "QUIT";
    /// <summary>Server-sent fatal error message before disconnection.</summary>
    public const string ERROR = "ERROR";

    // Channel Operations

    /// <summary>Join one or more channels, optionally with a key.</summary>
    public const string JOIN = "JOIN";
    /// <summary>Leave a channel with an optional part message.</summary>
    public const string PART = "PART";
    /// <summary>View or set a channel's topic.</summary>
    public const string TOPIC = "TOPIC";
    /// <summary>Request the list of users currently in a channel.</summary>
    public const string NAMES = "NAMES";
    /// <summary>Request a list of channels and their topics from the server.</summary>
    public const string LIST = "LIST";
    /// <summary>Invite a user to a channel.</summary>
    public const string INVITE = "INVITE";
    /// <summary>Remove a user from a channel (requires operator privileges).</summary>
    public const string KICK = "KICK";

    // Server Queries and Commands

    /// <summary>Request the server's Message of the Day.</summary>
    public const string MOTD = "MOTD";
    /// <summary>Query the server software version.</summary>
    public const string VERSION = "VERSION";
    /// <summary>Query server administrator contact information.</summary>
    public const string ADMIN = "ADMIN";
    /// <summary>Instruct the server to connect to another server (operator only).</summary>
    public const string CONNECT = "CONNECT";
    /// <summary>Request network-wide user and server statistics.</summary>
    public const string LUSERS = "LUSERS";
    /// <summary>Query the server's local time.</summary>
    public const string TIME = "TIME";
    /// <summary>Query server statistics (connections, commands, uptime, etc.).</summary>
    public const string STATS = "STATS";
    /// <summary>Request help text from the server.</summary>
    public const string HELP = "HELP";
    /// <summary>Request server information (software, maintainers).</summary>
    public const string INFO = "INFO";
    /// <summary>View or change user or channel modes.</summary>
    public const string MODE = "MODE";

    // Sending Messages

    /// <summary>Send a message to a user or channel.</summary>
    public const string PRIVMSG = "PRIVMSG";
    /// <summary>Send a notice to a user or channel (should not trigger auto-replies).</summary>
    public const string NOTICE = "NOTICE";

    // User-Based Queries

    /// <summary>Query information about users matching a mask or in a channel.</summary>
    public const string WHO = "WHO";
    /// <summary>Query detailed information about a connected user.</summary>
    public const string WHOIS = "WHOIS";
    /// <summary>Query information about a user who has since disconnected.</summary>
    public const string WHOWAS = "WHOWAS";

    // Operator Messages

    /// <summary>Forcibly disconnect a user from the network (operator only).</summary>
    public const string KILL = "KILL";
    /// <summary>Instruct the server to re-read its configuration file (operator only).</summary>
    public const string REHASH = "REHASH";
    /// <summary>Restart the server process (operator only).</summary>
    public const string RESTART = "RESTART";
    /// <summary>Disconnect a server link from the network (operator only).</summary>
    public const string SQUIT = "SQUIT";

    // Optional Messages

    /// <summary>Set or clear the client's away status with an optional message.</summary>
    public const string AWAY = "AWAY";
    /// <summary>List server-to-server links visible to the client.</summary>
    public const string LINKS = "LINKS";
    /// <summary>Query the host information for up to 5 nicknames.</summary>
    public const string USERHOST = "USERHOST";
    /// <summary>Send a message to all users with +w (wallops) mode set (operator only).</summary>
    public const string WALLOPS = "WALLOPS";

    // IRCv3 Specific Commands

    /// <summary>Start or end a labeled batch of related messages.</summary>
    public const string BATCH = "BATCH";
    /// <summary>Server notification that a user's displayed hostname or ident changed.</summary>
    public const string CHGHOST = "CHGHOST";
    /// <summary>Server notification that a user logged in or out of their services account.</summary>
    public const string ACCOUNT = "ACCOUNT";
    /// <summary>Add or remove nicknames from the server-side online/offline watch list.</summary>
    public const string MONITOR = "MONITOR";
    /// <summary>Send a tag-only message (no text body) to a target. Requires message-tags capability.</summary>
    public const string TAGMSG = "TAGMSG";
    /// <summary>Change the client's real name (GECOS) without reconnecting. Requires setname capability.</summary>
    public const string SETNAME = "SETNAME";
}
