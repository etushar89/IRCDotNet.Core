using System.Text;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Moq;
using Xunit;

namespace IRCDotNet.Tests.Events;

/// <summary>
/// Tests for enhanced event response capabilities
/// </summary>
public class EnhancedEventResponseTests : IDisposable
{
    private readonly Mock<IrcClient> _mockClient;
    private readonly IrcClientOptions _options;

    public EnhancedEventResponseTests()
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
    public async Task EnhancedMessageEvent_RespondAsync_InChannel_ShouldRespondInChannel()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "#channel", "Hello world");

        // Setup mock to capture the response
        client.Setup(c => c.SendMessageWithCancellationAsync("#channel", "Response message", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondAsync("Response message");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedMessageEvent_RespondAsync_PrivateMessage_ShouldRespondToUser()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG testnick :Private message");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "testnick", "Private message");

        // Setup mock to capture the response
        client.Setup(c => c.SendMessageWithCancellationAsync("nick", "Response message", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondAsync("Response message");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedMessageEvent_RespondToUserAsync_InChannel_ShouldAddressUser()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "#channel", "Hello world");

        // Setup mock to capture the response
        client.Setup(c => c.SendMessageWithCancellationAsync("#channel", "nick: Response message", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondToUserAsync("Response message");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedMessageEvent_RespondToUserAsync_PrivateMessage_ShouldRespondNormally()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG testnick :Private message");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "testnick", "Private message");

        // Setup mock to capture the response
        client.Setup(c => c.SendMessageWithCancellationAsync("nick", "Response message", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondToUserAsync("Response message");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedMessageEvent_ReplyPrivatelyAsync_ShouldAlwaysReplyPrivately()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "#channel", "Hello world");

        // Setup mock to capture the response
        client.Setup(c => c.SendMessageWithCancellationAsync("nick", "Private response", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.ReplyPrivatelyAsync("Private response");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedUserJoinedChannelEvent_WelcomeUserAsync_ShouldSendWelcomeMessage()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, client.Object, "nick", "user", "host", "#channel");

        // Setup mock to capture the welcome message
        client.Setup(c => c.SendMessageWithCancellationAsync("#channel", "nick: Welcome to the channel!", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.WelcomeUserAsync("Welcome to the channel!");

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedConnectedEvent_JoinChannelsAsync_ShouldJoinMultipleChannels()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedConnectedEvent(message, client.Object, "TestNet", "nick", "server");

        var channels = new[] { "#channel1", "#channel2", "#channel3" };

        // Setup mock to capture channel joins
        foreach (var channel in channels)
        {
            client.Setup(c => c.JoinChannelAsync(channel, null))
                  .Returns(Task.CompletedTask)
                  .Verifiable();
        }

        // Act
        await eventArgs.JoinChannelsAsync(channels);

        // Assert
        client.Verify();
    }

    [Fact]
    public async Task EnhancedConnectedEvent_JoinChannelsAsync_WithEmptyList_ShouldNotJoinAny()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedConnectedEvent(message, client.Object, "TestNet", "nick", "server");

        var channels = Array.Empty<string>();

        // Act
        await eventArgs.JoinChannelsAsync(channels);

        // Assert
        client.Verify(c => c.JoinChannelAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("#channel")]
    [InlineData("&localchannel")]
    public async Task EnhancedMessageEvent_RespondAsync_ChannelMessages_ShouldDetectChannelCorrectly(string channel)
    {
        // Arrange
        var message = IrcMessage.Parse($":nick!user@host PRIVMSG {channel} :Hello");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", channel, "Hello");

        client.Setup(c => c.SendMessageWithCancellationAsync(channel, "Response", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondAsync("Response");

        // Assert
        client.Verify();
        eventArgs.IsChannelMessage.Should().BeTrue();
        eventArgs.IsPrivateMessage.Should().BeFalse();
    }

    [Theory]
    [InlineData("nick")]
    [InlineData("SomeUser")]
    public async Task EnhancedMessageEvent_RespondAsync_PrivateMessages_ShouldDetectPrivateCorrectly(string target)
    {
        // Arrange
        var message = IrcMessage.Parse($":nick!user@host PRIVMSG {target} :Hello");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", target, "Hello");

        client.Setup(c => c.SendMessageWithCancellationAsync("nick", "Response", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask)
              .Verifiable();

        // Act
        await eventArgs.RespondAsync("Response");

        // Assert
        client.Verify();
        eventArgs.IsChannelMessage.Should().BeFalse();
        eventArgs.IsPrivateMessage.Should().BeTrue();
    }

    [Fact]
    public async Task EnhancedMessageEvent_ResponseMethods_ShouldHandleCancellation()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "#channel", "Hello"); using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.Setup(c => c.SendMessageWithCancellationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await eventArgs.Invoking(e => e.RespondAsync("Response", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        await eventArgs.Invoking(e => e.RespondToUserAsync("Response", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        await eventArgs.Invoking(e => e.ReplyPrivatelyAsync("Response", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnhancedUserJoinedChannelEvent_WelcomeUserAsync_ShouldHandleCancellation()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, client.Object, "nick", "user", "host", "#channel"); using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.Setup(c => c.SendMessageWithCancellationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await eventArgs.Invoking(e => e.WelcomeUserAsync("Welcome!", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void EnhancedMessageEvent_Properties_ShouldMatchIMessageEventInterface()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedMessageEvent(message, client.Object, "nick", "user", "host", "#channel", "Hello world");

        // Act & Assert
        var messageEvent = eventArgs as IMessageEvent;
        messageEvent.Should().NotBeNull();
        messageEvent!.Message.Should().Be("Hello world");
        messageEvent.User.Should().Be("nick");
        messageEvent.Source.Should().Be("#channel");
        messageEvent.IsPrivateMessage.Should().BeFalse();
    }

    [Fact]
    public void EnhancedUserJoinedChannelEvent_Properties_ShouldMatchIChannelEventInterface()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        var client = new Mock<IrcClient>(_options);
        var eventArgs = new EnhancedUserJoinedChannelEvent(message, client.Object, "nick", "user", "host", "#channel");

        // Act & Assert
        var channelEvent = eventArgs as IChannelEvent;
        channelEvent.Should().NotBeNull();
        channelEvent!.Channel.Should().Be("#channel");
        channelEvent.User.Should().Be("nick");
    }

    public void Dispose()
    {
        _mockClient?.Object?.Dispose();
    }
}
