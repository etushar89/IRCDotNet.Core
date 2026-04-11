using System.Text;

namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Provides case-insensitive comparison and hashing for IRC nicknames and channel names,
/// respecting the server's CASEMAPPING setting from ISUPPORT (005).
/// </summary>
public static class IrcCaseMapping
{
    /// <summary>
    /// Converts a string to lowercase using ASCII case mapping (A–Z → a–z only).
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>The lowercased string.</returns>
    public static string ToLowerAscii(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c >= 'A' && c <= 'Z')
                sb.Append((char)(c + 32));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a string to lowercase using RFC 1459 case mapping.
    /// In addition to A–Z, maps <c>[</c> → <c>{</c>, <c>]</c> → <c>}</c>, <c>\</c> → <c>|</c>, <c>^</c> → <c>~</c>.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>The lowercased string.</returns>
    public static string ToLowerRfc1459(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            switch (c)
            {
                case 'A':
                case 'B':
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                case 'G':
                case 'H':
                case 'I':
                case 'J':
                case 'K':
                case 'L':
                case 'M':
                case 'N':
                case 'O':
                case 'P':
                case 'Q':
                case 'R':
                case 'S':
                case 'T':
                case 'U':
                case 'V':
                case 'W':
                case 'X':
                case 'Y':
                case 'Z':
                    sb.Append((char)(c + 32));
                    break;
                case '[': sb.Append('{'); break;
                case ']': sb.Append('}'); break;
                case '\\': sb.Append('|'); break;
                case '^': sb.Append('~'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a string to lowercase using RFC 1459 strict case mapping.
    /// Same as RFC 1459 but does NOT map <c>^</c> → <c>~</c>.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>The lowercased string.</returns>
    public static string ToLowerRfc1459Strict(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            switch (c)
            {
                case 'A':
                case 'B':
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                case 'G':
                case 'H':
                case 'I':
                case 'J':
                case 'K':
                case 'L':
                case 'M':
                case 'N':
                case 'O':
                case 'P':
                case 'Q':
                case 'R':
                case 'S':
                case 'T':
                case 'U':
                case 'V':
                case 'W':
                case 'X':
                case 'Y':
                case 'Z':
                    sb.Append((char)(c + 32));
                    break;
                case '[': sb.Append('{'); break;
                case ']': sb.Append('}'); break;
                case '\\': sb.Append('|'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a string to lowercase using the specified case mapping type.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <param name="mappingType">The case mapping to use.</param>
    /// <returns>The lowercased string.</returns>
    public static string ToLower(string input, CaseMappingType mappingType)
    {
        return mappingType switch
        {
            CaseMappingType.Ascii => ToLowerAscii(input),
            CaseMappingType.Rfc1459Strict => ToLowerRfc1459Strict(input),
            CaseMappingType.Rfc1459 => ToLowerRfc1459(input),
            _ => ToLowerRfc1459(input)
        };
    }

    /// <summary>
    /// Compares two strings using IRC case mapping.
    /// </summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <param name="mappingType">The case mapping to use.</param>
    /// <returns><c>true</c> if the strings are equivalent.</returns>
    public static bool Equals(string? a, string? b, CaseMappingType mappingType)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        return string.Equals(ToLower(a, mappingType), ToLower(b, mappingType), StringComparison.Ordinal);
    }

    /// <summary>
    /// Compares two strings using RFC1459 case mapping (default).
    /// </summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns><c>true</c> if the strings are equivalent.</returns>
    public static bool Equals(string? a, string? b)
    {
        return Equals(a, b, CaseMappingType.Rfc1459);
    }

    /// <summary>
    /// Gets a hash code for a string using IRC case mapping.
    /// </summary>
    /// <param name="input">The string to hash.</param>
    /// <param name="mappingType">The case mapping to use.</param>
    /// <returns>The computed hash code.</returns>
    public static int GetHashCode(string input, CaseMappingType mappingType)
    {
        if (string.IsNullOrEmpty(input))
            return 0;

        return ToLower(input, mappingType).GetHashCode();
    }

    /// <summary>
    /// Gets a hash code for a string using RFC1459 case mapping (default).
    /// </summary>
    /// <param name="input">The string to hash.</param>
    /// <returns>The computed hash code.</returns>
    public static int GetHashCode(string input)
    {
        return GetHashCode(input, CaseMappingType.Rfc1459);
    }

    /// <summary>
    /// Creates a string comparer for IRC case mapping.
    /// </summary>
    /// <param name="mappingType">The case mapping to use.</param>
    /// <returns>An <see cref="IEqualityComparer{T}"/> that compares strings using IRC case rules.</returns>
    public static IEqualityComparer<string> CreateComparer(CaseMappingType mappingType)
    {
        return new IrcCaseComparer(mappingType);
    }

    /// <summary>
    /// String comparer that uses IRC case mapping
    /// </summary>
    private class IrcCaseComparer : IEqualityComparer<string>
    {
        private readonly CaseMappingType _mappingType;

        public IrcCaseComparer(CaseMappingType mappingType)
        {
            _mappingType = mappingType;
        }

        public bool Equals(string? x, string? y)
        {
            return IrcCaseMapping.Equals(x, y, _mappingType);
        }

        public int GetHashCode(string obj)
        {
            return IrcCaseMapping.GetHashCode(obj, _mappingType);
        }
    }
}

/// <summary>
/// Dictionary that uses IRC case mapping for string keys.
/// </summary>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
public class IrcCaseDictionary<TValue> : Dictionary<string, TValue>
{
    /// <summary>
    /// Initializes a new empty <see cref="IrcCaseDictionary{TValue}"/>.
    /// </summary>
    /// <param name="mappingType">The IRC case mapping to use for key comparison.</param>
    public IrcCaseDictionary(CaseMappingType mappingType = CaseMappingType.Rfc1459)
        : base(IrcCaseMapping.CreateComparer(mappingType))
    {
    }

    /// <summary>
    /// Initializes a new <see cref="IrcCaseDictionary{TValue}"/> from an existing dictionary.
    /// </summary>
    /// <param name="dictionary">The source dictionary to copy entries from.</param>
    /// <param name="mappingType">The IRC case mapping to use for key comparison.</param>
    public IrcCaseDictionary(IDictionary<string, TValue> dictionary, CaseMappingType mappingType = CaseMappingType.Rfc1459)
        : base(dictionary, IrcCaseMapping.CreateComparer(mappingType))
    {
    }
}

/// <summary>
/// HashSet that uses IRC case mapping for string comparison.
/// </summary>
public class IrcCaseHashSet : HashSet<string>
{
    /// <summary>
    /// Initializes a new empty <see cref="IrcCaseHashSet"/>.
    /// </summary>
    /// <param name="mappingType">The IRC case mapping to use.</param>
    public IrcCaseHashSet(CaseMappingType mappingType = CaseMappingType.Rfc1459)
        : base(IrcCaseMapping.CreateComparer(mappingType))
    {
    }

    /// <summary>
    /// Initializes a new <see cref="IrcCaseHashSet"/> from an existing collection.
    /// </summary>
    /// <param name="collection">The source collection to copy elements from.</param>
    /// <param name="mappingType">The IRC case mapping to use.</param>
    public IrcCaseHashSet(IEnumerable<string> collection, CaseMappingType mappingType = CaseMappingType.Rfc1459)
        : base(collection, IrcCaseMapping.CreateComparer(mappingType))
    {
    }
}
