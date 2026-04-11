using FluentAssertions;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Events;

public class IrcEventsTests
{
    [Fact]
    public void UserJoinedChannelEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        var eventArgs = new UserJoinedChannelEvent(message, "nick", "user", "host", "#channel");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Message.Should().Be(message);
        eventArgs.Timestamp.Should().BeCloseTo(timestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void UserLeftChannelEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PART #channel :Leaving");

        // Act
        var eventArgs = new UserLeftChannelEvent(message, "nick", "user", "host", "#channel", "Leaving");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Reason.Should().Be("Leaving");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void UserQuitEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host QUIT :Connection reset");

        // Act
        var eventArgs = new UserQuitEvent(message, "nick", "user", "host", "Connection reset");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Reason.Should().Be("Connection reset");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void PrivateMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG target :Hello world");

        // Act
        var eventArgs = new PrivateMessageEvent(message, "nick", "user", "host", "target", "Hello world");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Target.Should().Be("target");
        eventArgs.Text.Should().Be("Hello world");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void PrivateMessageEvent_IsEcho_ShouldDefaultToFalse()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :test");
        var eventArgs = new PrivateMessageEvent(message, "nick", "user", "host", "#channel", "test");

        eventArgs.IsEcho.Should().BeFalse();
    }

    [Fact]
    public void PrivateMessageEvent_IsEcho_ShouldBeSetWhenProvided()
    {
        var message = IrcMessage.Parse(":myNick!user@host PRIVMSG #channel :my own message");
        var eventArgs = new PrivateMessageEvent(message, "myNick", "user", "host", "#channel", "my own message", isEcho: true);

        eventArgs.IsEcho.Should().BeTrue();
        eventArgs.IsChannelMessage.Should().BeTrue();
    }

    [Fact]
    public void PrivateMessageEvent_IsChannelMessage_ShouldDetectChannelTargets()
    {
        var msg1 = IrcMessage.Parse(":nick!u@h PRIVMSG #channel :hi");
        new PrivateMessageEvent(msg1, "nick", "u", "h", "#channel", "hi").IsChannelMessage.Should().BeTrue();

        var msg2 = IrcMessage.Parse(":nick!u@h PRIVMSG &local :hi");
        new PrivateMessageEvent(msg2, "nick", "u", "h", "&local", "hi").IsChannelMessage.Should().BeTrue();

        var msg3 = IrcMessage.Parse(":nick!u@h PRIVMSG someone :hi");
        new PrivateMessageEvent(msg3, "nick", "u", "h", "someone", "hi").IsChannelMessage.Should().BeFalse();
    }

    [Fact]
    public void NoticeEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host NOTICE target :Important notice");

        // Act
        var eventArgs = new NoticeEvent(message, "nick", "user", "host", "target", "Important notice");

        // Assert
        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Target.Should().Be("target");
        eventArgs.Text.Should().Be("Important notice");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void TopicChangedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host TOPIC #channel :New topic");

        // Act
        var eventArgs = new TopicChangedEvent(message, "#channel", "New topic", "nick!user@host");

        // Assert
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Topic.Should().Be("New topic");
        eventArgs.SetBy.Should().Be("nick!user@host");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void UserKickedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":kicker!user@host KICK #channel victim :You're out");

        // Act
        var eventArgs = new UserKickedEvent(message, "#channel", "victim", "kicker", "You're out");

        // Assert
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.KickedNick.Should().Be("victim");
        eventArgs.KickedByNick.Should().Be("kicker");
        eventArgs.Reason.Should().Be("You're out");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void NickChangedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":oldnick!user@host NICK newnick");        // Act
        var eventArgs = new NickChangedEvent(message, "oldnick", "newnick", "user", "host");

        // Assert
        eventArgs.OldNick.Should().Be("oldnick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.NewNick.Should().Be("newnick");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void ChannelUsersEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 353 nick = #channel :@op +voice regular");
        var users = new List<ChannelUser>
        {
            new() { Nick = "op", Prefixes = ['@'] },
            new() { Nick = "voice", Prefixes = ['+'] },
            new() { Nick = "regular", Prefixes = [] }
        };

        // Act
        var eventArgs = new ChannelUsersEvent(message, "#channel", users);

        // Assert
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.Users.Should().HaveCount(3);
        eventArgs.Users[0].Nick.Should().Be("op");
        eventArgs.Users[0].IsOperator.Should().BeTrue();
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void RawMessageEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse("PING :server");

        // Act
        var eventArgs = new RawMessageEvent(message);

        // Assert
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void ConnectedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 001 nick :Welcome");
        var network = "ExampleNet";
        var nick = "nick";

        // Act
        var eventArgs = new ConnectedEvent(message, network, nick);

        // Assert
        eventArgs.Network.Should().Be(network);
        eventArgs.Nick.Should().Be(nick);
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void DisconnectedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse("ERROR :Connection closed");
        var reason = "Connection lost";

        // Act
        var eventArgs = new DisconnectedEvent(message, reason);

        // Assert
        eventArgs.Reason.Should().Be(reason);
        eventArgs.Message.Should().Be(message);
    }
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
    }

    [Fact]
    public void IrcEvent_ShouldStoreOriginalMessage()
    {
        // Arrange
        var message = IrcMessage.Parse(":nick!user@host JOIN #channel");

        // Act
        var eventArgs = new UserJoinedChannelEvent(message, "nick", "user", "host", "#channel");

        // Assert
        eventArgs.Message.Should().BeSameAs(message);
        eventArgs.Message.Command.Should().Be("JOIN");
        eventArgs.Message.Source.Should().Be("nick!user@host");
    }

    [Fact]
    public void ChannelListReceivedEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 322 nick #channel 42 :A channel topic");

        // Act
        var eventArgs = new ChannelListReceivedEvent(message, "#channel", 42, "A channel topic");

        // Assert
        eventArgs.Channel.Should().Be("#channel");
        eventArgs.UserCount.Should().Be(42);
        eventArgs.Topic.Should().Be("A channel topic");
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void ChannelListEndEvent_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = IrcMessage.Parse(":server 323 nick :End of /LIST");

        // Act
        var eventArgs = new ChannelListEndEvent(message);

        // Assert
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public void ChannelListEntry_ShouldInitializeCorrectly()
    {
        // Act
        var entry = new ChannelListEntry("#general", 150, "Welcome to general chat");

        // Assert
        entry.Channel.Should().Be("#general");
        entry.UserCount.Should().Be(150);
        entry.Topic.Should().Be("Welcome to general chat");
    }

    [Fact]
    public void ChannelListEntry_WithEmptyTopic_ShouldWork()
    {
        // Act
        var entry = new ChannelListEntry("#quiet", 3, "");

        // Assert
        entry.Channel.Should().Be("#quiet");
        entry.UserCount.Should().Be(3);
        entry.Topic.Should().BeEmpty();
    }
}
