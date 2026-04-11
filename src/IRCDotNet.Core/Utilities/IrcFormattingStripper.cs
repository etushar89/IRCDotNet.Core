using System.Text.RegularExpressions;

namespace IRCDotNet.Core.Utilities;

/// <summary>
/// Strips IRC formatting control codes from message text.
/// Currently strips all formatting for plain-text display.
/// TODO: Support rendering IRC colors/bold/underline visually in the UI
///       instead of stripping them (e.g., map mIRC colors to WPF brushes).
/// </summary>
public static partial class IrcFormattingStripper
{
    // IRC control characters:
    // \x02 = Bold
    // \x03 = Color (followed by optional fg[,bg] digits)
    // \x04 = Hex color (IRCv3, followed by RRGGBB[,RRGGBB])
    // \x0F = Reset (clear all formatting)
    // \x11 = Monospace
    // \x16 = Reverse/Italic (client-dependent)
    // \x1D = Italic
    // \x1E = Strikethrough
    // \x1F = Underline

    /// <summary>
    /// Regex matching \x03 followed by optional foreground color (1-2 digits)
    /// and optional comma + background color (1-2 digits).
    /// </summary>
    [GeneratedRegex(@"\x03(\d{1,2}(,\d{1,2})?)?")]
    private static partial Regex ColorCodeRegex();

    /// <summary>
    /// Regex matching \x04 followed by optional hex foreground (6 hex chars)
    /// and optional comma + hex background (6 hex chars).
    /// </summary>
    [GeneratedRegex(@"\x04([0-9A-Fa-f]{6}(,[0-9A-Fa-f]{6})?)?")]
    private static partial Regex HexColorCodeRegex();

    /// <summary>
    /// Strips all IRC formatting control codes from the given text,
    /// returning plain text suitable for display.
    /// </summary>
    /// <param name="text">The message text potentially containing IRC formatting codes.</param>
    /// <returns>The plain text with all formatting control codes removed.</returns>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Strip color codes first (they have trailing digits that must be removed together)
        text = ColorCodeRegex().Replace(text, string.Empty);
        text = HexColorCodeRegex().Replace(text, string.Empty);

        // Strip remaining single-character control codes
        return text
            .Replace("\x02", string.Empty)  // Bold
            .Replace("\x0F", string.Empty)  // Reset
            .Replace("\x11", string.Empty)  // Monospace
            .Replace("\x16", string.Empty)  // Reverse
            .Replace("\x1D", string.Empty)  // Italic
            .Replace("\x1E", string.Empty)  // Strikethrough
            .Replace("\x1F", string.Empty); // Underline
    }
}
