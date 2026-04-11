using FluentAssertions;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Events;

/// <summary>
/// Minimal tests for enhanced events to verify recent changes
/// </summary>
public class RecentEnhancedEventsTests
{
    [Fact]
    public void IrcEvent_ShouldHaveTimestamp()
    {
        // Arrange
        var message = IrcMessage.Parse("PING :server");
        var beforeTimestamp = DateTimeOffset.UtcNow;

        // Act
        var eventArgs = new RawMessageEvent(message);

        // Assert
        eventArgs.Timestamp.Should().BeOnOrAfter(beforeTimestamp);
        eventArgs.Timestamp.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void UserJoinedChannelEvent_ShouldHaveCorrectProperties()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");

        // Act
        var eventArgs = new UserJoinedChannelEvent(message, "nick", "user", "host", "#channel");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void PrivateMessageEvent_ShouldDetectChannelMessage()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");

        // Act
        var eventArgs = new PrivateMessageEvent(message, "nick", "user", "host", "#channel", "Hello world");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.Target.Should().Be("#channel");
        eventArgs.Text.Should().Be("Hello world");
        eventArgs.IsChannelMessage.Should().BeTrue();
    }

    [Fact]
    public void PrivateMessageEvent_ShouldDetectPrivateMessage()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG testnick :Private message");

        // Act
        var eventArgs = new PrivateMessageEvent(message, "nick", "user", "host", "testnick", "Private message");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.Target.Should().Be("testnick");
        eventArgs.Text.Should().Be("Private message");
        eventArgs.IsChannelMessage.Should().BeFalse();
    }

    [Fact]
    public void ExtendedUserJoinedChannelEvent_ShouldInheritFromUserJoinedChannelEvent()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");

        // Act
        var eventArgs = new ExtendedUserJoinedChannelEvent(message, "nick", "user", "host", "#channel", "account123", "Real Name");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Account.Should().Be("account123");
        eventArgs.RealName.Should().Be("Real Name");

        // Should be assignable to base type
        UserJoinedChannelEvent baseEvent = eventArgs;
        baseEvent.Should().NotBeNull();
    }

    [Fact]
    public void CapabilitiesNegotiatedEvent_ShouldStoreCapabilities()
    {
        // Arrange
        var message = IrcMessage.Parse("CAP * ACK :server-time message-tags");
        var enabled = new HashSet<string> { "server-time", "message-tags" };
        var supported = new HashSet<string> { "server-time", "message-tags", "away-notify" };

        // Act
        var eventArgs = new CapabilitiesNegotiatedEvent(message, enabled, supported);

        // Assert
        eventArgs.EnabledCapabilities.Should().BeEquivalentTo(enabled);
        eventArgs.SupportedCapabilities.Should().BeEquivalentTo(supported);
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void MessageTagsEvent_ShouldParseServerTime()
    {
        // Arrange
        var message = IrcMessage.Parse("@time=2023-01-01T12:00:00.000Z;msgid=123 :nick!user@host PRIVMSG #channel :Hello");

        // Act
        var eventArgs = new MessageTagsEvent(message);

        // Assert
        eventArgs.Tags.Should().ContainKey("time");
        eventArgs.Tags.Should().ContainKey("msgid");
        eventArgs.ServerTime.Should().HaveValue();
        eventArgs.MessageId.Should().Be("123");
    }

    [Fact]
    public void UserInfo_ShouldStoreExtendedInformation()
    {
        // Act
        var userInfo = new UserInfo
        {
            Nick = "testnick",
            User = "testuser",
            Host = "test.host.com",
            Account = "testaccount",
            RealName = "Test User",
            IsAway = true,
            AwayMessage = "Away from keyboard"
        };

        // Assert
        userInfo.Nick.Should().Be("testnick");
        userInfo.Account.Should().Be("testaccount");
        userInfo.IsAway.Should().BeTrue();
        userInfo.AwayMessage.Should().Be("Away from keyboard");
        userInfo.ExtendedInfo.Should().NotBeNull();
    }
}
