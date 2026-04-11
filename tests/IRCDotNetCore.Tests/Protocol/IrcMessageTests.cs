using FluentAssertions;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcMessageTests
{
    [Fact]
    public void Parse_SimpleCommand_ShouldParseCorrectly()
    {
        // Arrange
        var line = "PRIVMSG #channel :Hello world";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel");
        message.Parameters[1].Should().Be("Hello world");
        message.Source.Should().BeNull();
        message.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MessageWithSource_ShouldParseCorrectly()
    {
        // Arrange
        var line = ":nick!user@host PRIVMSG #channel :Hello world";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("PRIVMSG");
        message.Source.Should().Be("nick!user@host");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel");
        message.Parameters[1].Should().Be("Hello world");
        message.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MessageWithTags_ShouldParseCorrectly()
    {
        // Arrange
        var line = "@time=2023-01-01T12:00:00.000Z;msgid=test123 :nick!user@host PRIVMSG #channel :Hello world";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("PRIVMSG");
        message.Source.Should().Be("nick!user@host");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel");
        message.Parameters[1].Should().Be("Hello world");
        message.Tags.Should().HaveCount(2);
        message.Tags["time"].Should().Be("2023-01-01T12:00:00.000Z");
        message.Tags["msgid"].Should().Be("test123");
    }

    [Fact]
    public void Parse_NumericReply_ShouldParseCorrectly()
    {
        // Arrange
        var line = ":irc.server.com 001 testnick :Welcome to the IRC Network";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("001");
        message.Source.Should().Be("irc.server.com");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("Welcome to the IRC Network");
    }

    [Fact]
    public void Parse_JOIN_ShouldParseCorrectly()
    {
        // Arrange
        var line = ":nick!user@host JOIN #channel";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("JOIN");
        message.Source.Should().Be("nick!user@host");
        message.Parameters.Should().HaveCount(1);
        message.Parameters[0].Should().Be("#channel");
    }

    [Fact]
    public void Parse_QUIT_WithMessage_ShouldParseCorrectly()
    {
        // Arrange
        var line = ":nick!user@host QUIT :Leaving";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("QUIT");
        message.Source.Should().Be("nick!user@host");
        message.Parameters.Should().HaveCount(1);
        message.Parameters[0].Should().Be("Leaving");
    }

    [Fact]
    public void Parse_PING_ShouldParseCorrectly()
    {
        // Arrange
        var line = "PING :irc.server.com";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("PING");
        message.Source.Should().BeNull();
        message.Parameters.Should().HaveCount(1);
        message.Parameters[0].Should().Be("irc.server.com");
    }

    [Fact]
    public void Parse_EmptyTags_ShouldParseCorrectly()
    {
        // Arrange
        var line = "@tag1;tag2= :nick!user@host PRIVMSG #channel :Hello";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Tags.Should().HaveCount(2);
        message.Tags["tag1"].Should().BeNull();
        message.Tags["tag2"].Should().Be("");
    }

    [Fact]
    public void Parse_MultipleSpaces_ShouldParseCorrectly()
    {
        // Arrange
        var line = "  PRIVMSG   #channel   :Hello world  ";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel");
        message.Parameters[1].Should().Be("Hello world  ");
    }
    [Fact]
    public void Parse_InvalidFormat_NoCommand_ShouldThrowException()
    {
        // Arrange
        var line = "@tag=value :source";

        // Act & Assert
        Action act = () => IrcMessage.Parse(line);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Invalid message format: source without command*");
    }

    [Fact]
    public void Parse_InvalidFormat_TagsWithoutCommand_ShouldThrowException()
    {
        // Arrange
        var line = "@tag=value";

        // Act & Assert
        Action act = () => IrcMessage.Parse(line);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*tags without command*");
    }

    [Fact]
    public void Serialize_SimpleMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "PRIVMSG",
            Parameters = ["#channel", "Hello world"]
        };

        // Act
        var result = message.Serialize();

        // Assert
        result.Should().Be("PRIVMSG #channel :Hello world");
    }

    [Fact]
    public void Serialize_MessageWithSource_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Source = "nick!user@host",
            Command = "PRIVMSG",
            Parameters = ["#channel", "Hello world"]
        };

        // Act
        var result = message.Serialize();

        // Assert
        result.Should().Be(":nick!user@host PRIVMSG #channel :Hello world");
    }

    [Fact]
    public void Serialize_MessageWithTags_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Tags = new Dictionary<string, string?> { ["time"] = "2023-01-01T12:00:00.000Z", ["msgid"] = "test123" },
            Source = "nick!user@host",
            Command = "PRIVMSG",
            Parameters = ["#channel", "Hello world"]
        };

        // Act
        var result = message.Serialize();

        // Assert
        result.Should().Contain("@");
        result.Should().Contain("time=2023-01-01T12:00:00.000Z");
        result.Should().Contain("msgid=test123");
        result.Should().Contain(":nick!user@host PRIVMSG #channel :Hello world");
    }

    [Fact]
    public void Serialize_MessageWithEmptyTags_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IrcMessage
        {
            Tags = new Dictionary<string, string?> { ["tag1"] = null, ["tag2"] = "" },
            Command = "PRIVMSG",
            Parameters = ["#channel", "Hello"]
        };

        // Act
        var result = message.Serialize();        // Assert
        result.Should().Contain("@tag1;tag2=");
        result.Should().Contain("PRIVMSG #channel Hello");
    }

    [Fact]
    public void ParseAndSerialize_RoundTrip_ShouldPreserveData()
    {
        // Arrange
        var originalLine = "@time=2023-01-01T12:00:00.000Z;msgid=test123 :nick!user@host PRIVMSG #channel :Hello world";

        // Act
        var message = IrcMessage.Parse(originalLine);
        var serialized = message.Serialize();
        var reparsed = IrcMessage.Parse(serialized);

        // Assert
        reparsed.Command.Should().Be(message.Command);
        reparsed.Source.Should().Be(message.Source);
        reparsed.Parameters.Should().BeEquivalentTo(message.Parameters);
        reparsed.Tags.Should().BeEquivalentTo(message.Tags);
    }

    [Theory]
    [InlineData("PRIVMSG", "privmsg")]
    [InlineData("JOIN", "join")]
    [InlineData("001", "001")]
    public void Parse_Command_ShouldBeCaseInsensitive(string expectedCommand, string inputCommand)
    {
        // Arrange
        var line = $"{inputCommand} #channel";

        // Act
        var message = IrcMessage.Parse(line);

        // Assert
        message.Command.Should().Be(expectedCommand);
    }
}
