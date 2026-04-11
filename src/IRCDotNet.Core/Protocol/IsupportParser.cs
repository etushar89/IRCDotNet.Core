using System.Text.RegularExpressions;

namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Parses ISUPPORT (RPL_ISUPPORT 005) parameters advertised by the server during registration.
/// </summary>
public class IsupportParser
{
    private readonly Dictionary<string, string?> _features = new();
    private readonly Dictionary<string, object> _parsedValues = new();

    /// <summary>
    /// All ISUPPORT features advertised by the server, keyed by feature name.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Features => _features;

    /// <summary>
    /// Server network name (e.g. <c>"Libera.Chat"</c>), or <c>null</c> if not advertised.
    /// </summary>
    public string? NetworkName => GetStringValue("NETWORK");

    /// <summary>
    /// Maximum allowed nickname length. Default: 9.
    /// </summary>
    public int MaxNicknameLength => GetIntValue("NICKLEN", 9);

    /// <summary>
    /// Maximum allowed channel name length. Default: 50.
    /// </summary>
    public int MaxChannelLength => GetIntValue("CHANNELLEN", 50);

    /// <summary>
    /// Maximum allowed topic length. Default: 390.
    /// </summary>
    public int MaxTopicLength => GetIntValue("TOPICLEN", 390);

    /// <summary>
    /// Maximum allowed kick message length. Default: 390.
    /// </summary>
    public int MaxKickLength => GetIntValue("KICKLEN", 390);

    /// <summary>
    /// Maximum allowed away message length. Default: 390.
    /// </summary>
    public int MaxAwayLength => GetIntValue("AWAYLEN", 390);

    /// <summary>
    /// Maximum number of targets for PRIVMSG/NOTICE commands. Default: 1.
    /// </summary>
    public int MaxTargets => GetIntValue("MAXTARGETS", 1);

    /// <summary>
    /// Maximum number of channels a user can join. Default: unlimited.
    /// </summary>
    public int MaxChannels => GetIntValue("CHANLIMIT", int.MaxValue);

    /// <summary>
    /// Valid channel prefix characters (e.g. <c>"#&amp;+!"</c>). Default: <c>"#&amp;+!"</c>.
    /// </summary>
    public string ChannelTypes => GetStringValue("CHANTYPES", "#&+!") ?? "#&+!";

    /// <summary>
    /// Raw CHANMODES value listing mode categories (list, always-param, set-param, flag).
    /// </summary>
    public string? ChannelModes => GetStringValue("CHANMODES");

    /// <summary>
    /// Parsed PREFIX value mapping mode characters to their display prefixes (e.g. <c>o</c> → <c>@</c>, <c>v</c> → <c>+</c>).
    /// </summary>
    public (string Modes, string Prefixes) ChannelModePrefix
    {
        get
        {
            var prefix = GetStringValue("PREFIX");
            if (string.IsNullOrEmpty(prefix))
                return ("ov", "@+"); // Default

            var match = Regex.Match(prefix, @"^\(([^)]+)\)(.+)$");
            if (match.Success)
                return (match.Groups[1].Value, match.Groups[2].Value);

            return ("ov", "@+");
        }
    }

    /// <summary>
    /// Server's case mapping rule for comparing nicknames and channel names.
    /// </summary>
    public CaseMappingType CaseMapping
    {
        get
        {
            var mapping = GetStringValue("CASEMAPPING", "rfc1459") ?? "rfc1459";
            return mapping.ToLowerInvariant() switch
            {
                "ascii" => CaseMappingType.Ascii,
                "rfc1459-strict" => CaseMappingType.Rfc1459Strict,
                "rfc1459" => CaseMappingType.Rfc1459,
                _ => CaseMappingType.Rfc1459
            };
        }
    }

    /// <summary>
    /// Capabilities advertised via ISUPPORT CAPAB parameter.
    /// </summary>
    public string[] SupportedCapabilities
    {
        get
        {
            var caps = GetStringValue("CAPAB");
            return string.IsNullOrEmpty(caps) ? Array.Empty<string>() : caps.Split(' ');
        }
    }

    /// <summary>
    /// Parses an RPL_ISUPPORT (005) message and stores its feature parameters.
    /// </summary>
    /// <param name="message">The message text.</param>
    /// <returns>The IRC message.</returns>
    public void ParseIsupport(IrcMessage message)
    {
        if (message.Command != "005" || message.Parameters.Count < 2)
            return;

        // Skip the nickname parameter (first parameter)
        for (int i = 1; i < message.Parameters.Count - 1; i++)
        {
            var param = message.Parameters[i];
            ParseFeature(param);
        }
    }

    /// <summary>
    /// Parses a single feature parameter
    /// </summary>
    private void ParseFeature(string feature)
    {
        if (string.IsNullOrEmpty(feature))
            return;

        // Handle negation
        if (feature.StartsWith('-'))
        {
            var key = feature.Substring(1);
            _features.Remove(key);
            _parsedValues.Remove(key);
            return;
        }

        // Parse key=value or key format
        var equalPos = feature.IndexOf('=');
        if (equalPos == -1)
        {
            // Boolean feature
            _features[feature] = null;
            _parsedValues[feature] = true;
        }
        else
        {
            var key = feature.Substring(0, equalPos);
            var value = feature.Substring(equalPos + 1);
            _features[key] = value;
            ParseSpecialValues(key, value);
        }
    }

    /// <summary>
    /// Parses special value formats for known features
    /// </summary>
    private void ParseSpecialValues(string key, string value)
    {
        switch (key.ToUpperInvariant())
        {
            case "CHANLIMIT":
                ParseChannelLimits(value);
                break;
            case "CHANMODES":
                ParseChannelModes(value);
                break;
            case "MODES":
                if (int.TryParse(value, out var modes))
                    _parsedValues["MODES"] = modes;
                break;
            case "NICKLEN":
            case "CHANNELLEN":
            case "TOPICLEN":
            case "KICKLEN":
            case "AWAYLEN":
            case "MAXTARGETS":
                if (int.TryParse(value, out var intValue))
                    _parsedValues[key] = intValue;
                break;
        }
    }

    /// <summary>
    /// Parses channel limits (e.g., "#:120,&amp;:50")
    /// </summary>
    private void ParseChannelLimits(string value)
    {
        var limits = new Dictionary<char, int>();
        var parts = value.Split(',');

        foreach (var part in parts)
        {
            var colonPos = part.IndexOf(':');
            if (colonPos == -1) continue;

            var prefixes = part.Substring(0, colonPos);
            if (int.TryParse(part.Substring(colonPos + 1), out var limit))
            {
                foreach (char prefix in prefixes)
                {
                    limits[prefix] = limit;
                }
            }
        }

        _parsedValues["CHANLIMIT_PARSED"] = limits;
    }

    /// <summary>
    /// Parses channel modes (e.g., "beI,kfL,lj,psmntirRcOAQKVCuzNSMTG")
    /// </summary>
    private void ParseChannelModes(string value)
    {
        var parts = value.Split(',');
        if (parts.Length >= 4)
        {
            var modes = new ChannelModeInfo
            {
                ListModes = parts[0],           // Modes that add/remove items from lists (e.g., ban)
                AlwaysParameterModes = parts[1], // Modes that always take a parameter
                SetParameterModes = parts[2],    // Modes that take a parameter when set
                SimpleFlags = parts[3]           // Simple flags without parameters
            };
            _parsedValues["CHANMODES_PARSED"] = modes;
        }
    }

    /// <summary>
    /// Looks up a string-valued ISUPPORT feature.
    /// </summary>
    /// <param name="key">The ISUPPORT feature name (e.g. <c>"NETWORK"</c>, <c>"CHANTYPES"</c>).</param>
    /// <param name="defaultValue">Value returned if the feature is not advertised.</param>
    /// <returns>The feature value, or <paramref name="defaultValue"/> if not present.</returns>
    public string? GetStringValue(string key, string? defaultValue = null)
    {
        return _features.TryGetValue(key.ToUpperInvariant(), out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Looks up an integer-valued ISUPPORT feature.
    /// </summary>
    /// <param name="key">The ISUPPORT feature name (e.g. <c>"NICKLEN"</c>, <c>"TOPICLEN"</c>).</param>
    /// <param name="defaultValue">Value returned if the feature is not advertised.</param>
    /// <returns>The parsed integer, or <paramref name="defaultValue"/> if not present.</returns>
    public int GetIntValue(string key, int defaultValue = 0)
    {
        var upperKey = key.ToUpperInvariant();
        if (_parsedValues.TryGetValue(upperKey, out var value) && value is int intValue)
            return intValue;

        if (_features.TryGetValue(upperKey, out var stringValue) &&
            int.TryParse(stringValue, out var parsedValue))
            return parsedValue;

        return defaultValue;
    }

    /// <summary>
    /// Checks whether a boolean-valued ISUPPORT feature is present.
    /// </summary>
    /// <param name="key">The ISUPPORT feature name.</param>
    /// <returns><c>true</c> if the feature was advertised by the server.</returns>
    public bool GetBoolValue(string key)
    {
        var upperKey = key.ToUpperInvariant();
        return _features.ContainsKey(upperKey);
    }

    /// <summary>
    /// Gets the maximum number of channels a user can join for a specific channel type prefix.
    /// </summary>
    /// <param name="channelType">The channel prefix character (e.g. <c>'#'</c>).</param>
    /// <returns>The channel limit, or <see cref="int.MaxValue"/> if no limit is set.</returns>
    public int GetChannelLimit(char channelType)
    {
        if (_parsedValues.TryGetValue("CHANLIMIT_PARSED", out var value) &&
            value is Dictionary<char, int> limits)
        {
            return limits.TryGetValue(channelType, out var limit) ? limit : int.MaxValue;
        }

        return MaxChannels;
    }

    /// <summary>
    /// Gets the parsed CHANMODES information, or <c>null</c> if the server did not advertise CHANMODES.
    /// </summary>
    /// <returns>Channel mode information, or <c>null</c> if not available.</returns>
    public ChannelModeInfo? GetChannelModeInfo()
    {
        return _parsedValues.TryGetValue("CHANMODES_PARSED", out var value) &&
               value is ChannelModeInfo info ? info : null;
    }

    /// <summary>
    /// Checks whether a specific ISUPPORT feature was advertised by the server.
    /// </summary>
    /// <param name="feature">The ISUPPORT feature name (e.g. <c>"WHOX"</c>, <c>"MONITOR"</c>).</param>
    /// <returns><c>true</c> if the feature is present in the server's ISUPPORT list.</returns>
    public bool IsFeatureSupported(string feature)
    {
        return _features.ContainsKey(feature.ToUpperInvariant());
    }

    /// <summary>
    /// Clears all parsed features and resets to defaults.
    /// </summary>
    public void Clear()
    {
        _features.Clear();
        _parsedValues.Clear();
    }
}

/// <summary>
/// Case mapping types for IRC nickname and channel name comparison.
/// </summary>
public enum CaseMappingType
{
    /// <summary>
    /// ASCII case mapping: only A–Z are mapped to a–z.
    /// </summary>
    Ascii,

    /// <summary>
    /// RFC 1459 strict: A–Z plus <c>[</c> → <c>{</c>, <c>]</c> → <c>}</c>, <c>\</c> → <c>|</c> (but NOT <c>^</c> → <c>~</c>).
    /// </summary>
    Rfc1459Strict,

    /// <summary>
    /// RFC 1459: A–Z plus <c>[</c> → <c>{</c>, <c>]</c> → <c>}</c>, <c>\</c> → <c>|</c>, <c>^</c> → <c>~</c>. Most common default.
    /// </summary>
    Rfc1459
}

/// <summary>
/// Parsed CHANMODES information describing the four mode categories supported by the server.
/// </summary>
public class ChannelModeInfo
{
    /// <summary>
    /// Type A: modes that manage lists (e.g. <c>b</c> for bans, <c>e</c> for exceptions, <c>I</c> for invex).
    /// </summary>
    public string ListModes { get; set; } = string.Empty;

    /// <summary>
    /// Type B: modes that always require a parameter when set or unset (e.g. <c>k</c> for channel key).
    /// </summary>
    public string AlwaysParameterModes { get; set; } = string.Empty;

    /// <summary>
    /// Type C: modes that require a parameter only when being set (e.g. <c>l</c> for user limit).
    /// </summary>
    public string SetParameterModes { get; set; } = string.Empty;

    /// <summary>
    /// Type D: simple flag modes with no parameter (e.g. <c>n</c> for no external messages, <c>t</c> for topic lock).
    /// </summary>
    public string SimpleFlags { get; set; } = string.Empty;

    /// <summary>
    /// Checks whether the given mode character is a list mode (Type A).
    /// </summary>
    /// <param name="mode">The mode character to check.</param>
    /// <returns><c>true</c> if the mode manages a list.</returns>
    public bool IsListMode(char mode) => ListModes.Contains(mode);

    /// <summary>
    /// Checks whether the given mode character always requires a parameter (Type B).
    /// </summary>
    /// <param name="mode">The mode character to check.</param>
    /// <returns><c>true</c> if the mode always takes a parameter.</returns>
    public bool AlwaysRequiresParameter(char mode) => AlwaysParameterModes.Contains(mode);

    /// <summary>
    /// Checks whether the given mode character requires a parameter when being set but not when unset (Type C).
    /// </summary>
    /// <param name="mode">The mode character to check.</param>
    /// <returns><c>true</c> if the mode needs a parameter when set.</returns>
    public bool RequiresParameterWhenSet(char mode) => SetParameterModes.Contains(mode);

    /// <summary>
    /// Checks whether the given mode character is a simple flag with no parameter (Type D).
    /// </summary>
    /// <param name="mode">The mode character to check.</param>
    /// <returns><c>true</c> if the mode is a parameterless flag.</returns>
    public bool IsSimpleFlag(char mode) => SimpleFlags.Contains(mode);
}
