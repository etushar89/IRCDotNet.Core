using System.Text;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcEncodingTests
{
    [Fact]
    public void EncodeMessage_ValidString_ReturnsBytes()
    {
        // Arrange
        var message = "Hello World";

        // Act
        var result = IrcEncoding.EncodeMessage(message);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes(message), result);
    }

    [Fact]
    public void DecodeMessage_ValidBytes_ReturnsString()
    {
        // Arrange
        var message = "Hello World";
        var bytes = Encoding.UTF8.GetBytes(message);

        // Act
        var result = IrcEncoding.DecodeMessage(bytes);

        // Assert
        Assert.Equal(message, result);
    }

    [Fact]
    public void DecodeMessageSafe_ValidUTF8_ReturnsUTF8String()
    {
        // Arrange
        var message = "Hello 世界";
        var bytes = Encoding.UTF8.GetBytes(message);

        // Act
        var result = IrcEncoding.DecodeMessageSafe(bytes);

        // Assert
        Assert.Equal(message, result);
    }

    [Fact]
    public void SanitizeForIrc_WithControlCharacters_RemovesInvalidChars()
    {
        // Arrange
        var input = "Hello\x00World\x01Test";

        // Act
        var result = IrcEncoding.SanitizeForIrc(input);

        // Assert
        Assert.Equal("HelloWorldTest", result);
    }

    [Fact]
    public void SanitizeForIrc_WithFormattingChars_KeepsFormatting()
    {
        // Arrange
        var input = "Hello\x02World\x03Test"; // Bold and color

        // Act
        var result = IrcEncoding.SanitizeForIrc(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripIrcFormatting_WithFormatting_RemovesFormatting()
    {
        // Arrange
        var input = "Hello\x02World\x0F\x0312Red Text\x0F";

        // Act
        var result = IrcEncoding.StripIrcFormatting(input);

        // Assert
        Assert.Equal("HelloWorldRed Text", result);
    }

    [Fact]
    public void StripIrcFormatting_WithColorCodes_RemovesColorCodes()
    {
        // Arrange
        var input = "\x0312,4Red on Blue\x0F";

        // Act
        var result = IrcEncoding.StripIrcFormatting(input);

        // Assert
        Assert.Equal("Red on Blue", result);
    }

    [Fact]
    public void TruncateForIrc_ShortString_ReturnsOriginal()
    {
        // Arrange
        var input = "Hello World";

        // Act
        var result = IrcEncoding.TruncateForIrc(input, 100);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void TruncateForIrc_LongString_ReturnsTruncated()
    {
        // Arrange
        var input = new string('a', 600);

        // Act
        var result = IrcEncoding.TruncateForIrc(input, 100);

        // Assert
        Assert.True(result.Length < input.Length);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 100);
    }

    [Fact]
    public void ToIrcLowercase_WithSpecialChars_ConvertsCorrectly()
    {
        // Arrange
        var input = "Test[User]";

        // Act
        var result = IrcEncoding.ToIrcLowercase(input);

        // Assert
        Assert.Equal("test{user}", result);
    }

    [Fact]
    public void EscapeIrcMessage_WithSpecialChars_EscapesCorrectly()
    {
        // Arrange
        var input = "Hello\r\nWorld\0";

        // Act
        var result = IrcEncoding.EscapeIrcMessage(input);

        // Assert
        Assert.Equal("Hello\\r\\nWorld\\0", result);
    }

    [Fact]
    public void UnescapeIrcMessage_WithEscapedChars_UnescapesCorrectly()
    {
        // Arrange
        var input = "Hello\\r\\nWorld\\0";

        // Act
        var result = IrcEncoding.UnescapeIrcMessage(input);

        // Assert
        Assert.Equal("Hello\r\nWorld\0", result);
    }

    [Fact]
    public void IsValidIrcText_ValidText_ReturnsTrue()
    {
        // Arrange
        var input = "Hello World 123";

        // Act
        var result = IrcEncoding.IsValidIrcText(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidIrcText_WithForbiddenChars_ReturnsFalse()
    {
        // Arrange
        var input = "Hello\0World";

        // Act
        var result = IrcEncoding.IsValidIrcText(input);

        // Assert
        Assert.False(result);
    }
}
