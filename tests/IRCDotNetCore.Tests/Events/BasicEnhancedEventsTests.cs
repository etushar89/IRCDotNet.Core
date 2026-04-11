using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Events;

/// <summary>
/// Basic tests for the enhanced event system
/// </summary>
public class BasicEnhancedEventsTests : IDisposable
{
    private readonly IrcClient _client;

    public BasicEnhancedEventsTests()
    {
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testnick",
            UserName = "testuser",
            RealName = "Test User"
        };
        _client = new IrcClient(options);
    }

    [Fact]
    public void EnhancedMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");

        // Act
        var eventArgs = new EnhancedMessageEvent(message, _client, "nick", "user", "host", "#channel", "Hello world");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Target.Should().Be("#channel");
        eventArgs.Text.Should().Be("Hello world");
        ((IrcEvent)eventArgs).Message.Should().Be(message);
        eventArgs.IsChannelMessage.Should().BeTrue();
        eventArgs.IsPrivateMessage.Should().BeFalse();
    }

    [Fact]
    public void EnhancedMessageEvent_PrivateMessage_ShouldDetectCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG testnick :Private message");

        // Act
        var eventArgs = new EnhancedMessageEvent(message, _client, "nick", "user", "host", "testnick", "Private message");

        // Assert
        eventArgs.IsChannelMessage.Should().BeFalse();
        eventArgs.IsPrivateMessage.Should().BeTrue();
    }

    [Fact]
    public void EnhancedUserJoinedChannelEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");

        // Act
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, _client, "nick", "user", "host", "#channel");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void EnhancedNickChangeEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":oldnick!user@host NICK newnick");

        // Act
        var eventArgs = new EnhancedNickChangeEvent(message, _client, "oldnick", "newnick", "user", "host");

        // Assert
        eventArgs.OldNick.Should().Be("oldnick");
        eventArgs.NewNick.Should().Be("newnick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void EnhancedConnectedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");

        // Act
        var eventArgs = new EnhancedConnectedEvent(message, _client, "TestNet", "nick", "server", "Welcome message");

        // Assert
        eventArgs.Network.Should().Be("TestNet");
        eventArgs.Nick.Should().Be("nick");
        eventArgs.Server.Should().Be("server");
        eventArgs.Message.Should().Be("Welcome message");
    }

    [Fact]
    public void PreSendMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse("PRIVMSG #channel :Hello");

        // Act
        var eventArgs = new PreSendMessageEvent(message, _client, "#channel", "Hello");

        // Assert
        eventArgs.Target.Should().Be("#channel");
        eventArgs.Message.Should().Be("Hello");
        eventArgs.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void PreSendMessageEvent_ShouldAllowCancellation()
    {
        // Arrange
        var message = IrcMessage.Parse("PRIVMSG #channel :Hello");
        var eventArgs = new PreSendMessageEvent(message, _client, "#channel", "Hello");

        // Act
        eventArgs.IsCancelled = true;

        // Assert
        eventArgs.IsCancelled.Should().BeTrue();
    }

    [Theory]
    [InlineData("#channel", true)]
    [InlineData("&channel", true)]
    [InlineData("nick", false)]
    [InlineData("Nick123", false)]
    public void EnhancedMessageEvent_ShouldDetectChannelVsPrivateMessage(string target, bool isChannel)
    {
        // Arrange
        var message = IrcMessage.Parse($":nick!user@host PRIVMSG {target} :Hello");

        // Act
        var eventArgs = new EnhancedMessageEvent(message, _client, "nick", "user", "host", target, "Hello");

        // Assert
        eventArgs.IsChannelMessage.Should().Be(isChannel);
        eventArgs.IsPrivateMessage.Should().Be(!isChannel);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
