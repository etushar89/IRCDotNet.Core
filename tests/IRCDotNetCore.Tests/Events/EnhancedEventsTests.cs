using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Moq;
using Xunit;

namespace IRCDotNet.Tests.Events;

/// <summary>
/// Tests for the enhanced event system with response capabilities
/// </summary>
public class EnhancedEventsTests : IDisposable
{
    private readonly Mock<IrcClient> _mockClient;
    private readonly IrcClientOptions _options;

    public EnhancedEventsTests()
    {
        _options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testnick",
            UserName = "testuser",
            RealName = "Test User"
        };
        _mockClient = new Mock<IrcClient>(_options);
    }

    [Fact]
    public void EnhancedMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedMessageEvent(message, client, "nick", "user", "host", "#channel", "Hello world");

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
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedMessageEvent(message, client, "nick", "user", "host", "testnick", "Private message");

        // Assert
        eventArgs.IsChannelMessage.Should().BeFalse();
        eventArgs.IsPrivateMessage.Should().BeTrue();
    }

    [Fact]
    public void EnhancedMessageEvent_ImplementsIMessageEvent()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedMessageEvent(message, client, "nick", "user", "host", "#channel", "Hello world");

        // Assert
        (eventArgs as IMessageEvent).Should().NotBeNull();
        (eventArgs as IMessageEvent)!.Message.Should().Be("Hello world");
        (eventArgs as IMessageEvent)!.User.Should().Be("nick");
        (eventArgs as IMessageEvent)!.Source.Should().Be("#channel");
        (eventArgs as IMessageEvent)!.IsPrivateMessage.Should().BeFalse();
    }

    [Fact]
    public void EnhancedUserJoinedChannelEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, client, "nick", "user", "host", "#channel");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void EnhancedUserJoinedChannelEvent_ImplementsIChannelEvent()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, client, "nick", "user", "host", "#channel");

        // Assert
        (eventArgs as IChannelEvent).Should().NotBeNull();
        (eventArgs as IChannelEvent)!.Channel.Should().Be("#channel");
        (eventArgs as IChannelEvent)!.User.Should().Be("nick");
    }

    [Fact]
    public void EnhancedNickChangeEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":oldnick!user@host NICK newnick");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedNickChangeEvent(message, client, "oldnick", "newnick", "user", "host");

        // Assert
        eventArgs.OldNick.Should().Be("oldnick");
        eventArgs.NewNick.Should().Be("newnick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void EnhancedNickChangeEvent_ImplementsIUserEvent()
    {
        // Arrange
        var message = IrcMessage.Parse(":oldnick!user@host NICK newnick");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedNickChangeEvent(message, client, "oldnick", "newnick", "user", "host");

        // Assert
        (eventArgs as IUserEvent).Should().NotBeNull();
        (eventArgs as IUserEvent)!.User.Should().Be("newnick");
        (eventArgs as IUserEvent)!.OldNick.Should().Be("oldnick");
        (eventArgs as IUserEvent)!.NewNick.Should().Be("newnick");
    }

    [Fact]
    public void EnhancedConnectedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedConnectedEvent(message, client, "TestNet", "nick", "server", "Welcome message");

        // Assert
        eventArgs.Network.Should().Be("TestNet");
        eventArgs.Nick.Should().Be("nick");
        eventArgs.Server.Should().Be("server");
        eventArgs.Message.Should().Be("Welcome message");
    }

    [Fact]
    public void EnhancedConnectedEvent_ImplementsIServerEvent()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedConnectedEvent(message, client, "TestNet", "nick", "server", "Welcome message");

        // Assert
        (eventArgs as IServerEvent).Should().NotBeNull();
        (eventArgs as IServerEvent)!.Server.Should().Be("server");
        (eventArgs as IServerEvent)!.Message.Should().Be("Welcome message");
    }

    [Fact]
    public void EnhancedDisconnectedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse("ERROR :Connection closed");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedDisconnectedEvent(message, client, "server", "Connection lost", false);

        // Assert
        eventArgs.Server.Should().Be("server");
        eventArgs.Reason.Should().Be("Connection lost");
        eventArgs.WasExpected.Should().BeFalse();
        (eventArgs as IServerEvent)!.Message.Should().Be("Connection lost");
    }

    [Fact]
    public void PreSendMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse("PRIVMSG #channel :Hello");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new PreSendMessageEvent(message, client, "#channel", "Hello");

        // Assert
        eventArgs.Target.Should().Be("#channel");
        eventArgs.Message.Should().Be("Hello");
        eventArgs.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void PreSendMessageEvent_ImplementsICancellableEvent()
    {
        // Arrange
        var message = IrcMessage.Parse("PRIVMSG #channel :Hello");
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new PreSendMessageEvent(message, client, "#channel", "Hello");

        // Assert
        (eventArgs as ICancellableEvent).Should().NotBeNull();

        // Test cancellation
        (eventArgs as ICancellableEvent)!.IsCancelled = true;
        eventArgs.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public void PreSendMessageEvent_ShouldAllowModification()
    {
        // Arrange
        var message = IrcMessage.Parse("PRIVMSG #channel :Hello");
        using var client = new IrcClient(_options);
        var eventArgs = new PreSendMessageEvent(message, client, "#channel", "Hello");

        // Act
        eventArgs.Target = "#newchannel";
        eventArgs.Message = "Modified message";

        // Assert
        eventArgs.Target.Should().Be("#newchannel");
        eventArgs.Message.Should().Be("Modified message");
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
        using var client = new IrcClient(_options);

        // Act
        var eventArgs = new EnhancedMessageEvent(message, client, "nick", "user", "host", target, "Hello");

        // Assert
        eventArgs.IsChannelMessage.Should().Be(isChannel);
        eventArgs.IsPrivateMessage.Should().Be(!isChannel);
    }

    public void Dispose()
    {
        _mockClient?.Object?.Dispose();
    }
}
