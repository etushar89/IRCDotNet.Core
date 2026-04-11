using System.Text;

namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Represents an IRC message as defined in the Modern IRC Client Protocol specification.
/// Format: [@tags] [:source] &lt;command&gt; [parameters] [crlf]
/// </summary>
public class IrcMessage
{
    /// <summary>
    /// IRCv3 message tags — optional key-value metadata prefixed with <c>@</c> in the raw line.
    /// </summary>
    public Dictionary<string, string?> Tags { get; set; } = new();

    /// <summary>
    /// The message source (nick!user@host or server name), parsed from the <c>:</c> prefix.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The IRC command or numeric reply code (e.g. <c>"PRIVMSG"</c>, <c>"001"</c>). Always uppercase.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Command parameters. The last parameter may contain spaces (trailing parameter prefixed with <c>:</c> in the raw line).
    /// </summary>
    public List<string> Parameters { get; set; } = new();

    /// <summary>
    /// Parses an IRC message from a raw line.
    /// </summary>
    /// <param name="line">The raw IRC line to parse.</param>
    /// <returns>The parsed <see cref="IrcMessage"/>.</returns>
    public static IrcMessage Parse(string line)
    {
        var message = new IrcMessage();
        var pos = 0;

        // Remove trailing CRLF
        line = line.TrimEnd('\r', '\n');

        // Validate that the line is not empty or whitespace-only
        if (string.IsNullOrWhiteSpace(line))
            throw new ArgumentException("Invalid message format: empty or whitespace-only message");

        // Parse tags (optional)
        if (line.StartsWith('@'))
        {
            var tagEnd = line.IndexOf(' ');
            if (tagEnd == -1) throw new ArgumentException("Invalid message format: tags without command");

            var tagString = line.Substring(1, tagEnd - 1);
            message.Tags = ParseTags(tagString);
            pos = tagEnd + 1;
        }

        // Skip whitespace
        while (pos < line.Length && line[pos] == ' ') pos++;

        // Parse source (optional)
        if (pos < line.Length && line[pos] == ':')
        {
            var sourceEnd = line.IndexOf(' ', pos);
            if (sourceEnd == -1) throw new ArgumentException("Invalid message format: source without command");

            message.Source = line.Substring(pos + 1, sourceEnd - pos - 1);
            pos = sourceEnd + 1;
        }

        // Skip whitespace
        while (pos < line.Length && line[pos] == ' ') pos++;

        // Parse command (required)
        var commandEnd = line.IndexOf(' ', pos);
        if (commandEnd == -1) commandEnd = line.Length;

        if (pos >= line.Length) throw new ArgumentException("Invalid message format: missing command");

        message.Command = line.Substring(pos, commandEnd - pos).ToUpperInvariant();
        pos = commandEnd;

        // Parse parameters
        message.Parameters = ParseParameters(line, pos);

        return message;
    }

    /// <summary>
    /// Serializes the IRC message to a raw line.
    /// </summary>
    /// <returns>The serialized IRC protocol string.</returns>
    public string Serialize()
    {
        var sb = new StringBuilder();

        // Add tags
        if (Tags.Count > 0)
        {
            sb.Append('@');
            sb.Append(SerializeTags(Tags));
            sb.Append(' ');
        }

        // Add source
        if (!string.IsNullOrEmpty(Source))
        {
            sb.Append(':');
            sb.Append(Source);
            sb.Append(' ');
        }

        // Add command
        sb.Append(Command);

        // Add parameters
        for (int i = 0; i < Parameters.Count; i++)
        {
            sb.Append(' ');

            // Last parameter may need trailing prefix if it contains spaces or starts with ':'
            if (i == Parameters.Count - 1 &&
                (Parameters[i].Contains(' ') || Parameters[i].StartsWith(':') || string.IsNullOrEmpty(Parameters[i])))
            {
                sb.Append(':');
            }

            sb.Append(Parameters[i]);
        }

        return sb.ToString();
    }

    private static Dictionary<string, string?> ParseTags(string tagString)
    {
        var tags = new Dictionary<string, string?>();
        var pairs = tagString.Split(';');

        foreach (var pair in pairs)
        {
            var equalPos = pair.IndexOf('=');
            if (equalPos == -1)
            {
                tags[pair] = null;
            }
            else
            {
                var key = pair.Substring(0, equalPos);
                var value = pair.Substring(equalPos + 1);
                tags[key] = UnescapeTagValue(value);
            }
        }

        return tags;
    }

    private static string SerializeTags(Dictionary<string, string?> tags)
    {
        var pairs = new List<string>();

        foreach (var tag in tags)
        {
            if (tag.Value == null)
            {
                pairs.Add(tag.Key);
            }
            else
            {
                pairs.Add($"{tag.Key}={EscapeTagValue(tag.Value)}");
            }
        }

        return string.Join(";", pairs);
    }

    private static List<string> ParseParameters(string line, int startPos)
    {
        var parameters = new List<string>();
        var pos = startPos;

        while (pos < line.Length)
        {
            // Skip whitespace
            while (pos < line.Length && line[pos] == ' ') pos++;
            if (pos >= line.Length) break;

            // Check for trailing parameter
            if (line[pos] == ':')
            {
                parameters.Add(line.Substring(pos + 1));
                break;
            }

            // Parse regular parameter
            var paramEnd = line.IndexOf(' ', pos);
            if (paramEnd == -1) paramEnd = line.Length;

            parameters.Add(line.Substring(pos, paramEnd - pos));
            pos = paramEnd;
        }

        return parameters;
    }

    private static string UnescapeTagValue(string value)
    {
        return value.Replace("\\\\", "\\")
                   .Replace("\\:", ";")
                   .Replace("\\s", " ")
                   .Replace("\\r", "\r")
                   .Replace("\\n", "\n");
    }

    private static string EscapeTagValue(string value)
    {
        return value.Replace("\\", "\\\\")
                   .Replace(";", "\\:")
                   .Replace(" ", "\\s")
                   .Replace("\r", "\\r")
                   .Replace("\n", "\\n");
    }

    /// <summary>
    /// Validates the IRC message according to RFC specifications.
    /// </summary>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the message is valid.</returns>
    public ValidationResult Validate()
    {
        return IrcMessageValidator.ValidateMessage(this);
    }

    /// <summary>
    /// Parses an IRC message from a raw line with validation.
    /// </summary>
    /// <param name="line">The raw IRC line to parse.</param>
    /// <returns>The parsed and validated <see cref="IrcMessage"/>.</returns>
    /// <exception cref="IrcValidationException">The parsed message fails validation.</exception>
    public static IrcMessage ParseAndValidate(string line)
    {
        var message = Parse(line);
        var validation = message.Validate();

        if (!validation.IsValid)
        {
            throw new IrcValidationException($"Invalid IRC message: {validation.ErrorMessage}");
        }

        return message;
    }

    /// <summary>
    /// Creates a safe IRC message with automatic validation and truncation.
    /// </summary>
    /// <param name="command">The IRC command.</param>
    /// <param name="parameters">Command parameters.</param>
    /// <returns>A validated <see cref="IrcMessage"/>.</returns>
    /// <exception cref="IrcValidationException">Unable to create a valid message.</exception>
    public static IrcMessage CreateSafe(string command, params string[] parameters)
    {
        var message = new IrcMessage
        {
            Command = command.ToUpperInvariant()
        };

        // Add parameters with validation
        foreach (var param in parameters)
        {
            if (!string.IsNullOrEmpty(param))
            {
                var sanitized = IrcEncoding.SanitizeForIrc(param);
                message.Parameters.Add(sanitized);
            }
        }

        // Validate and truncate if necessary
        var validation = message.Validate();
        if (!validation.IsValid)
        {
            // Try to fix by truncating the last parameter
            if (message.Parameters.Count > 0)
            {
                var lastParam = message.Parameters[message.Parameters.Count - 1];
                var truncated = IrcEncoding.TruncateForIrc(lastParam, 400); // Leave room for other parts
                message.Parameters[message.Parameters.Count - 1] = truncated;

                validation = message.Validate();
            }
        }

        if (!validation.IsValid)
        {
            throw new IrcValidationException($"Unable to create valid IRC message: {validation.ErrorMessage}");
        }

        return message;
    }

    /// <inheritdoc />
    public override string ToString() => Serialize();
}
