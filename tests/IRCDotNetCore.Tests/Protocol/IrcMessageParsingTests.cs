using System;
using System.Collections.Generic;
using System.Linq;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

/// <summary>
/// Comprehensive tests for IRC message parsing and serialization
/// </summary>
public class IrcMessageParsingTests
{
    [Fact]
    public void ParseMessage_WithValidBasicMessage_ShouldParseCorrectly()
    {
        // Arrange
        var input = "PRIVMSG #test :Hello world";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal(2, message.Parameters.Count);
        Assert.Equal("#test", message.Parameters[0]);
        Assert.Equal("Hello world", message.Parameters[1]);
        Assert.Null(message.Source);
        Assert.Empty(message.Tags);
    }

    [Fact]
    public void ParseMessage_WithSource_ShouldParseCorrectly()
    {
        // Arrange
        var input = ":nick!user@host PRIVMSG #test :Hello world";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("nick!user@host", message.Source);
        Assert.Equal(2, message.Parameters.Count);
        Assert.Equal("#test", message.Parameters[0]);
        Assert.Equal("Hello world", message.Parameters[1]);
    }

    [Fact]
    public void ParseMessage_WithTags_ShouldParseCorrectly()
    {
        // Arrange
        var input = "@time=2023-01-01T12:00:00.000Z;id=123 :nick!user@host PRIVMSG #test :Hello world";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("nick!user@host", message.Source);
        Assert.Equal(2, message.Tags.Count);
        Assert.Equal("2023-01-01T12:00:00.000Z", message.Tags["time"]);
        Assert.Equal("123", message.Tags["id"]);
        Assert.Equal("#test", message.Parameters[0]);
        Assert.Equal("Hello world", message.Parameters[1]);
    }

    [Fact]
    public void ParseMessage_WithTagsNoValue_ShouldParseCorrectly()
    {
        // Arrange
        var input = "@tag1;tag2=value2 COMMAND param1";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("COMMAND", message.Command);
        Assert.Equal(2, message.Tags.Count);
        Assert.Null(message.Tags["tag1"]);
        Assert.Equal("value2", message.Tags["tag2"]);
        Assert.Single(message.Parameters);
        Assert.Equal("param1", message.Parameters[0]);
    }

    [Fact]
    public void ParseMessage_WithEscapedTagValues_ShouldUnescapeCorrectly()
    {
        // Arrange
        var input = @"@tag1=hello\\world;tag2=foo\\:bar\\sbaz COMMAND";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("COMMAND", message.Command);
        Assert.Equal(2, message.Tags.Count);
        Assert.Equal("hello\\world", message.Tags["tag1"]);
        Assert.Equal("foo;bar baz", message.Tags["tag2"]);
    }

    [Fact]
    public void ParseMessage_WithMultipleSpaces_ShouldHandleCorrectly()
    {
        // Arrange
        var input = "  COMMAND     param1     param2     :trailing with   spaces  ";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("COMMAND", message.Command);
        Assert.Equal(3, message.Parameters.Count);
        Assert.Equal("param1", message.Parameters[0]);
        Assert.Equal("param2", message.Parameters[1]);
        Assert.Equal("trailing with   spaces  ", message.Parameters[2]);
    }

    [Fact]
    public void ParseMessage_WithEmptyTrailingParameter_ShouldParseCorrectly()
    {
        // Arrange
        var input = "COMMAND param1 :";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("COMMAND", message.Command);
        Assert.Equal(2, message.Parameters.Count);
        Assert.Equal("param1", message.Parameters[0]);
        Assert.Equal(string.Empty, message.Parameters[1]);
    }

    [Fact]
    public void ParseMessage_WithNoParameters_ShouldParseCorrectly()
    {
        // Arrange
        var input = "QUIT";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("QUIT", message.Command);
        Assert.Empty(message.Parameters);
        Assert.Null(message.Source);
        Assert.Empty(message.Tags);
    }

    [Fact]
    public void ParseMessage_WithNumericCommand_ShouldParseCorrectly()
    {
        // Arrange
        var input = ":server 001 nick :Welcome to the network";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("001", message.Command);
        Assert.Equal("server", message.Source);
        Assert.Equal(2, message.Parameters.Count);
        Assert.Equal("nick", message.Parameters[0]);
        Assert.Equal("Welcome to the network", message.Parameters[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void ParseMessage_WithEmptyOrWhitespaceInput_ShouldThrowException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IrcMessage.Parse(input));
    }

    [Theory]
    [InlineData("@tag1=value1")]
    [InlineData(":source")]
    [InlineData("@tag1=value1 :source")]
    public void ParseMessage_WithoutCommand_ShouldThrowException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IrcMessage.Parse(input));
    }

    [Fact]
    public void SerializeMessage_BasicMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "PRIVMSG",
            Parameters = { "#test", "Hello world" }
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.Equal("PRIVMSG #test :Hello world", result);
    }

    [Fact]
    public void SerializeMessage_WithSource_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Source = "nick!user@host",
            Command = "PRIVMSG",
            Parameters = { "#test", "Hello world" }
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.Equal(":nick!user@host PRIVMSG #test :Hello world", result);
    }

    [Fact]
    public void SerializeMessage_WithTags_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Tags = { ["time"] = "2023-01-01T12:00:00.000Z", ["id"] = "123" },
            Source = "nick!user@host",
            Command = "PRIVMSG",
            Parameters = { "#test", "Hello world" }
        };

        // Act
        var result = message.Serialize();

        // Assert - Note: Dictionary order may vary, so check both possibilities
        Assert.True(
            result == "@time=2023-01-01T12:00:00.000Z;id=123 :nick!user@host PRIVMSG #test :Hello world" ||
            result == "@id=123;time=2023-01-01T12:00:00.000Z :nick!user@host PRIVMSG #test :Hello world");
    }

    [Fact]
    public void SerializeMessage_WithEscapedTagValues_ShouldEscapeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Tags = { ["tag1"] = "hello\\world", ["tag2"] = "foo;bar baz" },
            Command = "COMMAND"
        };

        // Act
        var result = message.Serialize();

        // Assert - Check that values are properly escaped
        Assert.Contains("tag1=hello\\\\world", result);
        Assert.Contains("tag2=foo\\:bar\\sbaz", result);
    }

    [Fact]
    public void SerializeMessage_WithNullTagValue_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Tags = { ["tag1"] = null, ["tag2"] = "value" },
            Command = "COMMAND"
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.True(
            result.StartsWith("@tag1;tag2=value COMMAND") ||
            result.StartsWith("@tag2=value;tag1 COMMAND"));
    }

    [Fact]
    public void SerializeMessage_ParameterWithSpaces_ShouldAddTrailingPrefix()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "PRIVMSG",
            Parameters = { "#test", "Hello world with spaces" }
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.Equal("PRIVMSG #test :Hello world with spaces", result);
    }

    [Fact]
    public void SerializeMessage_ParameterStartingWithColon_ShouldAddTrailingPrefix()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "PRIVMSG",
            Parameters = { "#test", ":test message" }
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.Equal("PRIVMSG #test ::test message", result);
    }

    [Fact]
    public void SerializeMessage_EmptyLastParameter_ShouldAddTrailingPrefix()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "TOPIC",
            Parameters = { "#test", "" }
        };

        // Act
        var result = message.Serialize();

        // Assert
        Assert.Equal("TOPIC #test :", result);
    }

    [Fact]
    public void ParseAndSerialize_RoundTrip_ShouldBeConsistent()
    {
        // Arrange
        var originalInputs = new[]
        {
            "PRIVMSG #test :Hello world",
            ":nick!user@host PRIVMSG #test :Hello world",
            "@time=2023-01-01T12:00:00.000Z PRIVMSG #test :Hello world",
            "001 nick :Welcome",
            "QUIT :Goodbye",
            "MODE #test +o nick"
        };

        foreach (var input in originalInputs)
        {
            // Act
            var parsed = IrcMessage.Parse(input);
            var serialized = parsed.Serialize();
            var reparsed = IrcMessage.Parse(serialized);

            // Assert
            Assert.Equal(parsed.Command, reparsed.Command);
            Assert.Equal(parsed.Source, reparsed.Source);
            Assert.Equal(parsed.Parameters.Count, reparsed.Parameters.Count);
            for (int i = 0; i < parsed.Parameters.Count; i++)
            {
                Assert.Equal(parsed.Parameters[i], reparsed.Parameters[i]);
            }
            Assert.Equal(parsed.Tags.Count, reparsed.Tags.Count);
            foreach (var tag in parsed.Tags)
            {
                Assert.True(reparsed.Tags.ContainsKey(tag.Key));
                Assert.Equal(tag.Value, reparsed.Tags[tag.Key]);
            }
        }
    }

    [Fact]
    public void ParseMessage_WithCRLFEnding_ShouldRemoveCorrectly()
    {
        // Arrange
        var input = "PRIVMSG #test :Hello world\r\n";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal(2, message.Parameters.Count);
        Assert.Equal("#test", message.Parameters[0]);
        Assert.Equal("Hello world", message.Parameters[1]);
    }

    [Fact]
    public void ParseMessage_WithUnicodeCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var input = "PRIVMSG #test :Hello 世界 🌍";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("Hello 世界 🌍", message.Parameters[1]);
    }

    [Theory]
    [InlineData("PRIVMSG")]
    [InlineData("privmsg")]
    [InlineData("PrIvMsG")]
    public void ParseMessage_CommandCaseInsensitive_ShouldNormalizeToUppercase(string command)
    {
        // Arrange
        var input = $"{command} #test :Hello";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("PRIVMSG", message.Command);
    }

    [Fact]
    public void ParseMessage_WithMaximumParameters_ShouldHandleCorrectly()
    {
        // Arrange - IRC typically allows up to 15 parameters
        var parameters = Enumerable.Range(1, 14).Select(i => $"param{i}").ToList();
        parameters.Add("trailing parameter");
        var input = $"COMMAND {string.Join(" ", parameters.Take(14))} :{parameters.Last()}";

        // Act
        var message = IrcMessage.Parse(input);

        // Assert
        Assert.Equal("COMMAND", message.Command);
        Assert.Equal(15, message.Parameters.Count);
        for (int i = 0; i < 14; i++)
        {
            Assert.Equal($"param{i + 1}", message.Parameters[i]);
        }
        Assert.Equal("trailing parameter", message.Parameters[14]);
    }
}
