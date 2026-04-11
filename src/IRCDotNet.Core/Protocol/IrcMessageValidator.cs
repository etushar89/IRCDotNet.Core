using System.Text;
using System.Text.RegularExpressions;

namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Validates IRC messages, nicknames, channel names, and hostnames against RFC 1459 and Modern IRC rules.
/// </summary>
public static class IrcMessageValidator
{
    /// <summary>
    /// Maximum IRC message length in bytes (510 = 512 minus trailing CRLF).
    /// </summary>
    public const int MaxMessageLength = 510; // 512 - 2 for CRLF

    /// <summary>
    /// Maximum length in bytes for a single command parameter.
    /// </summary>
    public const int MaxParameterLength = 512;

    /// <summary>
    /// Maximum number of parameters allowed in a single IRC message.
    /// </summary>
    public const int MaxParameterCount = 15;

    /// <summary>
    /// Regex pattern for valid IRC nicknames per RFC 1459.
    /// </summary>
    private static readonly Regex NicknamePattern = new Regex(
        @"^[a-zA-Z\[\]\\`_^{|}][a-zA-Z0-9\[\]\\`_^{|}-]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Regex pattern for valid IRC channel names (must start with <c>#</c>, <c>&amp;</c>, <c>+</c>, or <c>!</c>).
    /// </summary>
    private static readonly Regex ChannelPattern = new Regex(
        @"^[#&+!][^\x00\x07\x0A\x0D\x20\x2C]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Regex pattern for valid DNS hostnames.
    /// </summary>
    private static readonly Regex HostnamePattern = new Regex(
        @"^[a-zA-Z0-9.-]+$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validates the length of an IRC message.
    /// </summary>
    /// <param name="message">The message string to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the length is valid.</returns>
    public static ValidationResult ValidateMessageLength(string message)
    {
        if (string.IsNullOrEmpty(message))
            return ValidationResult.Invalid("Message cannot be null or empty");

        var bytes = Encoding.UTF8.GetByteCount(message);
        if (bytes > MaxMessageLength)
            return ValidationResult.Invalid($"Message length ({bytes} bytes) exceeds maximum ({MaxMessageLength} bytes)");

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates an IRC nickname according to RFC 1459.
    /// </summary>
    /// <param name="nickname">The nickname to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the nickname is valid.</returns>
    public static ValidationResult ValidateNickname(string nickname)
    {
        if (string.IsNullOrEmpty(nickname))
            return ValidationResult.Invalid("Nickname cannot be null or empty");

        if (nickname.Length > 30) // Modern IRC servers commonly support up to 30 characters
            return ValidationResult.Invalid($"Nickname length ({nickname.Length}) exceeds maximum (30 characters)");

        // First character must be a letter or special character
        if (!char.IsLetter(nickname[0]) && !"[]\\`_^{|}".Contains(nickname[0]))
            return ValidationResult.Invalid($"Nickname '{nickname}' must start with a letter or special character");

        if (!NicknamePattern.IsMatch(nickname))
            return ValidationResult.Invalid($"Nickname '{nickname}' contains invalid characters");

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates an IRC channel name.
    /// </summary>
    /// <param name="channel">The channel name to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the channel name is valid.</returns>
    public static ValidationResult ValidateChannelName(string channel)
    {
        if (string.IsNullOrEmpty(channel))
            return ValidationResult.Invalid("Channel name cannot be null or empty");

        if (channel.Length > 50) // Common server limit
            return ValidationResult.Invalid($"Channel name length ({channel.Length}) exceeds maximum (50 characters)");

        // Check for forbidden characters first
        if (channel.Contains('\x00') || channel.Contains('\x07') ||
            channel.Contains('\x0A') || channel.Contains('\x0D') ||
            channel.Contains(' ') || channel.Contains(','))
            return ValidationResult.Invalid($"Channel name '{channel}' contains forbidden characters");

        if (!ChannelPattern.IsMatch(channel))
            return ValidationResult.Invalid($"Channel name '{channel}' is invalid");

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates a hostname.
    /// </summary>
    /// <param name="hostname">The hostname to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the hostname is valid.</returns>
    public static ValidationResult ValidateHostname(string hostname)
    {
        if (string.IsNullOrEmpty(hostname))
            return ValidationResult.Invalid("Hostname cannot be null or empty");

        if (hostname.Length > 63) // DNS label limit
            return ValidationResult.Invalid($"Hostname length ({hostname.Length}) exceeds maximum (63 characters)");

        if (!HostnamePattern.IsMatch(hostname))
            return ValidationResult.Invalid($"Hostname '{hostname}' contains invalid characters");

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates IRC command parameters.
    /// </summary>
    /// <param name="parameters">The parameter list to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the parameters are valid.</returns>
    public static ValidationResult ValidateParameters(List<string> parameters)
    {
        if (parameters.Count > MaxParameterCount)
            return ValidationResult.Invalid($"Parameter count ({parameters.Count}) exceeds maximum ({MaxParameterCount})");

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.Length > MaxParameterLength)
                return ValidationResult.Invalid($"Parameter {i} length ({param.Length}) exceeds maximum ({MaxParameterLength})");

            // Middle parameters cannot contain spaces or start with ':'
            if (i < parameters.Count - 1)
            {
                if (param.Contains(' '))
                    return ValidationResult.Invalid($"Parameter {i} contains spaces (only last parameter can contain spaces)");

                if (param.StartsWith(':'))
                    return ValidationResult.Invalid($"Parameter {i} starts with ':' (only last parameter can start with ':')");
            }

            // Check for null bytes
            if (param.Contains('\0'))
                return ValidationResult.Invalid($"Parameter {i} contains null bytes");
        }

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates IRC message tags.
    /// </summary>
    /// <param name="tags">The tags dictionary to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the tags are valid.</returns>
    public static ValidationResult ValidateTags(Dictionary<string, string?> tags)
    {
        if (tags.Count > 50) // Reasonable limit to prevent abuse
            return ValidationResult.Invalid($"Tag count ({tags.Count}) exceeds maximum (50)");

        foreach (var tag in tags)
        {
            // Validate tag key
            if (string.IsNullOrEmpty(tag.Key))
                return ValidationResult.Invalid("Tag key cannot be null or empty");

            if (tag.Key.Length > 50)
                return ValidationResult.Invalid($"Tag key '{tag.Key}' length exceeds maximum (50 characters)");

            // Tag keys should not contain spaces, semicolons, or equals signs
            if (tag.Key.Contains(' ') || tag.Key.Contains(';') || tag.Key.Contains('='))
                return ValidationResult.Invalid($"Tag key '{tag.Key}' contains invalid characters");

            // Validate tag value if present
            if (tag.Value != null)
            {
                if (tag.Value.Length > 512)
                    return ValidationResult.Invalid($"Tag value for '{tag.Key}' length exceeds maximum (512 characters)");

                // Check for unescaped characters that should be escaped
                if (tag.Value.Contains(';') && !tag.Value.Contains("\\:"))
                    return ValidationResult.Invalid($"Tag value for '{tag.Key}' contains unescaped semicolon");
            }
        }

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validates a complete IRC message.
    /// </summary>
    /// <param name="message">The IRC message to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the message is valid.</returns>
    public static ValidationResult ValidateMessage(IrcMessage message)
    {
        // Validate command
        if (string.IsNullOrEmpty(message.Command))
            return ValidationResult.Invalid("Command cannot be null or empty");

        if (message.Command.Length > 20)
            return ValidationResult.Invalid($"Command '{message.Command}' length exceeds maximum (20 characters)");

        // Validate tags
        var tagResult = ValidateTags(message.Tags);
        if (!tagResult.IsValid)
            return tagResult;

        // Validate parameters
        var paramResult = ValidateParameters(message.Parameters);
        if (!paramResult.IsValid)
            return paramResult;

        // Validate source if present
        if (!string.IsNullOrEmpty(message.Source))
        {
            if (message.Source.Length > 512)
                return ValidationResult.Invalid($"Source '{message.Source}' length exceeds maximum (512 characters)");
        }

        // Validate serialized message length
        var serialized = message.Serialize();
        var lengthResult = ValidateMessageLength(serialized);
        if (!lengthResult.IsValid)
            return lengthResult;

        return ValidationResult.Valid();
    }
}

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public class ValidationResult
{
    /// <summary>Whether the validation passed.</summary>
    public bool IsValid { get; private set; }
    /// <summary>Human-readable error message if validation failed, or <c>null</c> if valid.</summary>
    public string? ErrorMessage { get; private set; }

    private ValidationResult(bool isValid, string? errorMessage = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>Creates a successful validation result.</summary>
    /// <returns>A valid <see cref="ValidationResult"/>.</returns>
    public static ValidationResult Valid() => new ValidationResult(true);
    /// <summary>Creates a failed validation result with an error message.</summary>
    /// <param name="errorMessage">Description of the validation failure.</param>
    /// <returns>An invalid <see cref="ValidationResult"/>.</returns>
    public static ValidationResult Invalid(string errorMessage) => new ValidationResult(false, errorMessage);

    /// <inheritdoc />
    public override string ToString()
    {
        return IsValid ? "Valid" : $"Invalid: {ErrorMessage}";
    }
}
