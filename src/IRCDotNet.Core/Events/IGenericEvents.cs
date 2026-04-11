namespace IRCDotNet.Core.Events;

/// <summary>
/// Interface for IRC events that support sending a reply back to the source.
/// </summary>
public interface IRespondableEvent
{
    /// <summary>
    /// Sends a reply to the source of this event (channel or private message).
    /// </summary>
    /// <param name="message">The response text to send.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    Task RespondAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for events representing an incoming IRC message (PRIVMSG).
/// </summary>
public interface IMessageEvent : IRespondableEvent
{
    /// <summary>
    /// The message text content.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Nickname of the user who sent the message.
    /// </summary>
    string User { get; }

    /// <summary>
    /// Where the message came from — channel name for public messages, or the client's nickname for private messages.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Whether this message was sent directly to the client (not to a channel).
    /// </summary>
    bool IsPrivateMessage { get; }
}

/// <summary>
/// Interface for events related to a specific IRC channel.
/// </summary>
public interface IChannelEvent
{
    /// <summary>
    /// The channel name (e.g. <c>"#general"</c>).
    /// </summary>
    string Channel { get; }

    /// <summary>
    /// Nickname of the user involved in this channel event.
    /// </summary>
    string User { get; }
}

/// <summary>
/// Interface for events related to a specific IRC user.
/// </summary>
public interface IUserEvent
{
    /// <summary>
    /// Nickname of the user involved in this event.
    /// </summary>
    string User { get; }

    /// <summary>
    /// The user's previous nickname, if this is a nick change event. Otherwise <c>null</c>.
    /// </summary>
    string? OldNick { get; }

    /// <summary>
    /// The user's new nickname, if this is a nick change event. Otherwise <c>null</c>.
    /// </summary>
    string? NewNick { get; }
}

/// <summary>
/// Interface for events related to the IRC server connection.
/// </summary>
public interface IServerEvent
{
    /// <summary>
    /// The server hostname the client is connected to.
    /// </summary>
    string Server { get; }

    /// <summary>
    /// Server-provided message — welcome text on connect, or disconnect reason on disconnect.
    /// </summary>
    string? Message { get; }
}

/// <summary>
/// Interface for outgoing events that can be cancelled before being sent.
/// </summary>
public interface ICancellableEvent
{
    /// <summary>
    /// When set to <c>true</c>, prevents this event's action from being executed.
    /// </summary>
    bool IsCancelled { get; set; }
}
