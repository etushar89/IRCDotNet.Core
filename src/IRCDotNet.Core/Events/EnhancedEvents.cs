using IRCDotNet.Core.Protocol;

namespace IRCDotNet.Core.Events;

/// <summary>
/// Enhanced base class for IRC events with a client reference for sending responses.
/// </summary>
public abstract class EnhancedIrcEvent : IrcEvent
{
    /// <summary>
    /// Reference to the IRC client for sending responses.
    /// </summary>
    protected IrcClient Client { get; }

    /// <summary>
    /// Initializes the enhanced event with the raw IRC message and client reference.
    /// </summary>
    /// <param name="message">The raw IRC message that triggered this event.</param>
    /// <param name="client">The IRC client instance for sending responses.</param>
    protected EnhancedIrcEvent(IrcMessage message, IrcClient client) : base(message)
    {
        Client = client;
    }
}

/// <summary>
/// Enhanced message event with response capabilities.
/// </summary>
public class EnhancedMessageEvent : EnhancedIrcEvent, IMessageEvent
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
    /// <summary>The message text (alias for <see cref="Text"/>).</summary>
    public new string Message => Text;
    string IMessageEvent.User => Nick;
    /// <summary>The source target of the message (channel or nickname).</summary>
    public string Source => Target;
    /// <summary>Whether this is a private (non-channel) message.</summary>
    public bool IsPrivateMessage => !IsChannelMessage;

    /// <summary>
    /// Initializes a new <see cref="EnhancedMessageEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="nick">Nickname of the sender.</param>
    /// <param name="user">Username (ident) of the sender.</param>
    /// <param name="host">Hostname of the sender.</param>
    /// <param name="target">Target of the message (channel or nickname).</param>
    /// <param name="text">The message text.</param>
    public EnhancedMessageEvent(IrcMessage message, IrcClient client, string nick, string user, string host, string target, string text)
        : base(message, client)
    {
        Nick = nick;
        User = user;
        Host = host;
        Target = target;
        Text = text;
    }

    /// <summary>
    /// Respond to this message intelligently.
    /// If it's a channel message, responds in the channel.
    /// If it's a private message, responds to the user directly.
    /// </summary>
    /// <param name="message">The response text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RespondAsync(string message, CancellationToken cancellationToken = default)
    {
        if (IsChannelMessage)
        {
            // Respond in the channel, optionally addressing the user
            await Client.SendMessageWithCancellationAsync(Target, message, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Respond via private message
            await Client.SendMessageWithCancellationAsync(Nick, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Respond to this message addressing the user by name (e.g. "Nick: response").
    /// For private messages, sends directly without the prefix.
    /// </summary>
    /// <param name="message">The response text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RespondToUserAsync(string message, CancellationToken cancellationToken = default)
    {
        if (IsChannelMessage)
        {
            await Client.SendMessageWithCancellationAsync(Target, $"{Nick}: {message}", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Client.SendMessageWithCancellationAsync(Nick, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reply via private message regardless of where the original message came from.
    /// </summary>
    /// <param name="message">The response text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ReplyPrivatelyAsync(string message, CancellationToken cancellationToken = default)
    {
        await Client.SendMessageWithCancellationAsync(Nick, message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Enhanced channel join event with response capabilities.
/// </summary>
public class EnhancedUserJoinedChannelEvent : EnhancedIrcEvent, IChannelEvent
{
    /// <summary>Nickname of the user who joined.</summary>
    public string Nick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }
    /// <summary>Channel that was joined.</summary>
    public string Channel { get; }

    // IChannelEvent implementation
    string IChannelEvent.User => Nick;

    /// <summary>
    /// Initializes a new <see cref="EnhancedUserJoinedChannelEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="nick">Nickname of the user who joined.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="channel">Channel that was joined.</param>
    public EnhancedUserJoinedChannelEvent(IrcMessage message, IrcClient client, string nick, string user, string host, string channel)
        : base(message, client)
    {
        Nick = nick;
        User = user;
        Host = host;
        Channel = channel;
    }

    /// <summary>
    /// Sends a welcome message to the user who joined, addressing them by name.
    /// </summary>
    /// <param name="message">The welcome message text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task WelcomeUserAsync(string message, CancellationToken cancellationToken = default)
    {
        await Client.SendMessageWithCancellationAsync(Channel, $"{Nick}: {message}", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Enhanced channel part event.
/// </summary>
public class EnhancedUserLeftChannelEvent : EnhancedIrcEvent, IChannelEvent
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

    // IChannelEvent implementation
    string IChannelEvent.User => Nick;

    /// <summary>
    /// Initializes a new <see cref="EnhancedUserLeftChannelEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="nick">Nickname of the user who left.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    /// <param name="channel">Channel that was left.</param>
    /// <param name="reason">Optional part message.</param>
    public EnhancedUserLeftChannelEvent(IrcMessage message, IrcClient client, string nick, string user, string host, string channel, string? reason = null)
        : base(message, client)
    {
        Nick = nick;
        User = user;
        Host = host;
        Channel = channel;
        Reason = reason;
    }
}

/// <summary>
/// Enhanced nick change event.
/// </summary>
public class EnhancedNickChangeEvent : EnhancedIrcEvent, IUserEvent
{
    /// <summary>The user's previous nickname.</summary>
    public string OldNick { get; }
    /// <summary>The user's new nickname.</summary>
    public string NewNick { get; }
    /// <summary>Username (ident) of the user.</summary>
    public string User { get; }
    /// <summary>Hostname of the user.</summary>
    public string Host { get; }

    // IUserEvent implementation
    string IUserEvent.User => NewNick;
    string? IUserEvent.OldNick => OldNick;
    string? IUserEvent.NewNick => NewNick;

    /// <summary>
    /// Initializes a new <see cref="EnhancedNickChangeEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="oldNick">The user's previous nickname.</param>
    /// <param name="newNick">The user's new nickname.</param>
    /// <param name="user">Username (ident) of the user.</param>
    /// <param name="host">Hostname of the user.</param>
    public EnhancedNickChangeEvent(IrcMessage message, IrcClient client, string oldNick, string newNick, string user, string host)
        : base(message, client)
    {
        OldNick = oldNick;
        NewNick = newNick;
        User = user;
        Host = host;
    }
}

/// <summary>
/// Enhanced server connect event with post-connect capabilities.
/// </summary>
public class EnhancedConnectedEvent : EnhancedIrcEvent, IServerEvent
{
    /// <summary>Network name reported by the server.</summary>
    public string Network { get; }
    /// <summary>The nickname assigned to the client after registration.</summary>
    public string Nick { get; }
    /// <summary>Server hostname the client connected to.</summary>
    public string Server { get; }
    /// <summary>Welcome message from the server, if any.</summary>
    public new string? Message { get; }

    /// <summary>
    /// Initializes a new <see cref="EnhancedConnectedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="network">Network name reported by the server.</param>
    /// <param name="nick">The nickname assigned to the client.</param>
    /// <param name="server">Server hostname.</param>
    /// <param name="welcomeMessage">Optional welcome message from the server.</param>
    public EnhancedConnectedEvent(IrcMessage message, IrcClient client, string network, string nick, string server, string? welcomeMessage = null)
        : base(message, client)
    {
        Network = network;
        Nick = nick;
        Server = server;
        Message = welcomeMessage;
    }

    /// <summary>
    /// Joins a list of channels after connecting.
    /// </summary>
    /// <param name="channels">Channel names to join.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task JoinChannelsAsync(IEnumerable<string> channels, CancellationToken cancellationToken = default)
    {
        foreach (var channel in channels)
        {
            await Client.JoinChannelAsync(channel).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Enhanced disconnect event.
/// </summary>
public class EnhancedDisconnectedEvent : EnhancedIrcEvent, IServerEvent
{
    /// <summary>The reason for the disconnection, if available.</summary>
    public string? Reason { get; }
    /// <summary>Whether the disconnection was initiated by the client (expected) or by the server/network (unexpected).</summary>
    public bool WasExpected { get; }
    /// <summary>Server hostname the client was connected to.</summary>
    public string Server { get; }
    /// <summary>The disconnection reason (alias for <see cref="Reason"/>).</summary>
    public new string? Message => Reason;

    /// <summary>
    /// Initializes a new <see cref="EnhancedDisconnectedEvent"/>.
    /// </summary>
    /// <param name="message">The raw IRC message.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="server">Server hostname.</param>
    /// <param name="reason">Optional disconnection reason.</param>
    /// <param name="wasExpected">Whether the disconnect was client-initiated.</param>
    public EnhancedDisconnectedEvent(IrcMessage message, IrcClient client, string server, string? reason = null, bool wasExpected = false)
        : base(message, client)
    {
        Reason = reason;
        WasExpected = wasExpected;
        Server = server;
    }
}

/// <summary>
/// Pre-send message event that allows inspection and cancellation of outgoing messages.
/// </summary>
public class PreSendMessageEvent : EnhancedIrcEvent, ICancellableEvent
{
    /// <summary>Target recipient (channel or nickname). Can be modified before sending.</summary>
    public string Target { get; set; }
    /// <summary>The outgoing message text. Can be modified before sending.</summary>
    public new string Message { get; set; }
    /// <summary>Set to <c>true</c> to prevent this message from being sent.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Initializes a new <see cref="PreSendMessageEvent"/>.
    /// </summary>
    /// <param name="originalMessage">The raw IRC message being constructed.</param>
    /// <param name="client">The IRC client instance.</param>
    /// <param name="target">Target recipient (channel or nickname).</param>
    /// <param name="message">The outgoing message text.</param>
    public PreSendMessageEvent(IrcMessage originalMessage, IrcClient client, string target, string message)
        : base(originalMessage, client)
    {
        Target = target;
        Message = message;
    }
}
