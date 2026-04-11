namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Enhanced error handling for IRC protocol operations.
/// </summary>
public static class IrcErrorHandler
{
    /// <summary>
    /// Inspects a numeric IRC reply and converts error codes into typed exceptions.
    /// </summary>
    /// <param name="message">The parsed IRC message to inspect.</param>
    /// <returns>A typed <see cref="IrcProtocolException"/> if the message is an error, or <c>null</c> for non-error messages.</returns>
    public static IrcProtocolException? HandleNumericError(IrcMessage message)
    {
        if (!IrcNumericReplies.IsErrorCode(message.Command))
            return null;

        var errorCode = message.Command;
        var description = IrcNumericReplies.GetDescription(errorCode);
        var parameters = string.Join(", ", message.Parameters);

        return errorCode switch
        {
            IrcNumericReplies.ERR_NOSUCHNICK => new IrcTargetNotFoundException(
                $"Target not found: {parameters}", errorCode),

            IrcNumericReplies.ERR_NOSUCHCHANNEL => new IrcChannelNotFoundException(
                $"Channel not found: {parameters}", errorCode),

            IrcNumericReplies.ERR_NICKNAMEINUSE => new IrcNicknameInUseException(
                $"Nickname already in use: {parameters}", errorCode),

            IrcNumericReplies.ERR_ERRONEUSNICKNAME => new IrcInvalidNicknameException(
                $"Invalid nickname: {parameters}", errorCode),

            IrcNumericReplies.ERR_NICKCOLLISION => new IrcNicknameCollisionException(
                $"Nickname collision KILL: {parameters}", errorCode),

            IrcNumericReplies.ERR_NEEDMOREPARAMS => new IrcInsufficientParametersException(
                $"Insufficient parameters: {parameters}", errorCode),

            IrcNumericReplies.ERR_ALREADYREGISTERED => new IrcAlreadyRegisteredException(
                $"Already registered: {parameters}", errorCode),

            IrcNumericReplies.ERR_PASSWDMISMATCH => new IrcAuthenticationException(
                $"Password mismatch: {parameters}", errorCode),

            IrcNumericReplies.ERR_CANNOTSENDTOCHAN => new IrcChannelPermissionException(
                $"Cannot send to channel: {parameters}", errorCode),

            IrcNumericReplies.ERR_NOTONCHANNEL => new IrcNotOnChannelException(
                $"Not on channel: {parameters}", errorCode),

            IrcNumericReplies.ERR_CHANOPRIVSNEEDED => new IrcChannelPermissionException(
                $"Channel operator privileges needed: {parameters}", errorCode),

            IrcNumericReplies.ERR_BANNEDFROMCHAN => new IrcChannelBannedException(
                $"Banned from channel: {parameters}", errorCode),

            IrcNumericReplies.ERR_INVITEONLYCHAN => new IrcChannelInviteOnlyException(
                $"Channel is invite-only: {parameters}", errorCode),

            IrcNumericReplies.ERR_CHANNELISFULL => new IrcChannelFullException(
                $"Channel is full: {parameters}", errorCode),

            IrcNumericReplies.ERR_BADCHANNELKEY => new IrcChannelKeyException(
                $"Bad channel key: {parameters}", errorCode),

            IrcNumericReplies.ERR_UNKNOWNCOMMAND => new IrcUnknownCommandException(
                $"Unknown command: {parameters}", errorCode),

            IrcNumericReplies.ERR_NOTREGISTERED => new IrcNotRegisteredException(
                $"Not registered: {parameters}", errorCode),

            IrcNumericReplies.ERR_SASLFAIL => new IrcAuthenticationException(
                $"SASL authentication failed: {parameters}", errorCode),

            _ => new IrcProtocolException($"{description}: {parameters}", errorCode)
        };
    }
}

/// <summary>
/// Base exception for IRC protocol errors.
/// </summary>
public class IrcProtocolException : Exception
{
    /// <summary>The IRC numeric error code (e.g. "433" for nickname in use), or <c>null</c> for non-numeric errors.</summary>
    public string? NumericCode { get; }

    /// <inheritdoc />
    public IrcProtocolException() { }

    /// <summary>
    /// Initializes a new <see cref="IrcProtocolException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcProtocolException(string message, string? numericCode = null)
        : base(message)
    {
        NumericCode = numericCode;
    }

    /// <summary>
    /// Initializes a new <see cref="IrcProtocolException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcProtocolException(string message, Exception innerException, string? numericCode = null)
        : base(message, innerException)
    {
        NumericCode = numericCode;
    }

    /// <inheritdoc />
    public IrcProtocolException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcProtocolException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an IRC message fails validation (invalid format, length, etc.).
/// </summary>
public class IrcValidationException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcValidationException() { }
    /// <inheritdoc />
    public IrcValidationException(string message) : base(message) { }
    /// <inheritdoc />
    public IrcValidationException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new <see cref="IrcValidationException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcValidationException(string message, string? numericCode = null) : base(message, numericCode)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="IrcValidationException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcValidationException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode)
    {
    }
}

/// <summary>
/// Exception thrown when the target of a message (nickname or channel) does not exist (ERR_NOSUCHNICK 401).
/// </summary>
public class IrcTargetNotFoundException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcTargetNotFoundException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcTargetNotFoundException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcTargetNotFoundException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcTargetNotFoundException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcTargetNotFoundException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcTargetNotFoundException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcTargetNotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a channel does not exist (ERR_NOSUCHCHANNEL 403).
/// </summary>
public class IrcChannelNotFoundException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcChannelNotFoundException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelNotFoundException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelNotFoundException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelNotFoundException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelNotFoundException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelNotFoundException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelNotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base exception for nickname-related errors.
/// </summary>
public class IrcNicknameException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcNicknameException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcNicknameException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcNicknameException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a nickname is already in use on the server (ERR_NICKNAMEINUSE 433).
/// </summary>
public class IrcNicknameInUseException : IrcNicknameException
{
    /// <inheritdoc />
    public IrcNicknameInUseException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameInUseException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameInUseException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameInUseException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameInUseException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcNicknameInUseException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcNicknameInUseException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a nickname contains invalid characters (ERR_ERRONEUSNICKNAME 432).
/// </summary>
public class IrcInvalidNicknameException : IrcNicknameException
{
    /// <inheritdoc />
    public IrcInvalidNicknameException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcInvalidNicknameException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcInvalidNicknameException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcInvalidNicknameException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcInvalidNicknameException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcInvalidNicknameException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcInvalidNicknameException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a nickname collision is detected by the server (ERR_NICKCOLLISION 436).
/// </summary>
public class IrcNicknameCollisionException : IrcNicknameException
{
    /// <inheritdoc />
    public IrcNicknameCollisionException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameCollisionException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameCollisionException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcNicknameCollisionException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNicknameCollisionException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcNicknameCollisionException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcNicknameCollisionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a command is sent with too few parameters (ERR_NEEDMOREPARAMS 461).
/// </summary>
public class IrcInsufficientParametersException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcInsufficientParametersException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcInsufficientParametersException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcInsufficientParametersException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcInsufficientParametersException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcInsufficientParametersException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcInsufficientParametersException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcInsufficientParametersException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an already-registered client attempts to re-register (ERR_ALREADYREGISTERED 462).
/// </summary>
public class IrcAlreadyRegisteredException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcAlreadyRegisteredException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcAlreadyRegisteredException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcAlreadyRegisteredException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcAlreadyRegisteredException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcAlreadyRegisteredException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcAlreadyRegisteredException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcAlreadyRegisteredException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authentication fails (password mismatch, SASL failure, etc.).
/// </summary>
public class IrcAuthenticationException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcAuthenticationException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcAuthenticationException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcAuthenticationException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcAuthenticationException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcAuthenticationException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcAuthenticationException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcAuthenticationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base exception for channel-related errors.
/// </summary>
public class IrcChannelException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcChannelException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the client lacks permission to perform a channel operation (ERR_CHANOPRIVSNEEDED 482, ERR_CANNOTSENDTOCHAN 404).
/// </summary>
public class IrcChannelPermissionException : IrcChannelException
{
    /// <inheritdoc />
    public IrcChannelPermissionException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelPermissionException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelPermissionException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelPermissionException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelPermissionException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelPermissionException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelPermissionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the client is not on the specified channel (ERR_NOTONCHANNEL 442).
/// </summary>
public class IrcNotOnChannelException : IrcChannelException
{
    /// <inheritdoc />
    public IrcNotOnChannelException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcNotOnChannelException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNotOnChannelException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcNotOnChannelException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNotOnChannelException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcNotOnChannelException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcNotOnChannelException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the client is banned from a channel (ERR_BANNEDFROMCHAN 474).
/// </summary>
public class IrcChannelBannedException : IrcChannelException
{
    /// <inheritdoc />
    public IrcChannelBannedException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelBannedException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelBannedException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelBannedException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelBannedException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelBannedException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelBannedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a channel is invite-only and the client has not been invited (ERR_INVITEONLYCHAN 473).
/// </summary>
public class IrcChannelInviteOnlyException : IrcChannelException
{
    /// <inheritdoc />
    public IrcChannelInviteOnlyException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelInviteOnlyException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelInviteOnlyException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelInviteOnlyException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelInviteOnlyException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelInviteOnlyException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelInviteOnlyException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a channel has reached its user limit (ERR_CHANNELISFULL 471).
/// </summary>
public class IrcChannelFullException : IrcChannelException
{
    /// <inheritdoc />
    public IrcChannelFullException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelFullException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelFullException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelFullException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelFullException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelFullException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelFullException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the wrong channel key (password) is provided (ERR_BADCHANNELKEY 475).
/// </summary>
public class IrcChannelKeyException : IrcChannelException
{
    /// <inheritdoc />
    public IrcChannelKeyException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelKeyException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelKeyException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcChannelKeyException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcChannelKeyException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcChannelKeyException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcChannelKeyException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the server does not recognize a command (ERR_UNKNOWNCOMMAND 421).
/// </summary>
public class IrcUnknownCommandException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcUnknownCommandException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcUnknownCommandException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcUnknownCommandException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcUnknownCommandException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcUnknownCommandException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcUnknownCommandException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcUnknownCommandException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a command requires registration but the client has not registered yet (ERR_NOTREGISTERED 451).
/// </summary>
public class IrcNotRegisteredException : IrcProtocolException
{
    /// <inheritdoc />
    public IrcNotRegisteredException() { }
    /// <summary>
    /// Initializes a new <see cref="IrcNotRegisteredException"/> with a message and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNotRegisteredException(string message, string? numericCode = null) : base(message, numericCode) { }
    /// <summary>
    /// Initializes a new <see cref="IrcNotRegisteredException"/> with a message, inner exception, and optional numeric code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="numericCode">The IRC numeric error code.</param>
    public IrcNotRegisteredException(string message, Exception innerException, string? numericCode = null) : base(message, innerException, numericCode) { }

    /// <inheritdoc />
    public IrcNotRegisteredException(string? message) : base(message)
    {
    }

    /// <inheritdoc />
    public IrcNotRegisteredException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
