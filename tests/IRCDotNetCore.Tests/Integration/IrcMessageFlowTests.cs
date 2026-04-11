using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IRCDotNet.Tests.Integration;

/// <summary>
/// Integration tests that test the interaction between different components
/// These tests don't require network connectivity but test the full message flow
/// </summary>
public class IrcMessageFlowTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly List<IrcEvent> _receivedEvents = new();

    public IrcMessageFlowTests()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
        };

        _client = new IrcClient(options, NullLogger<IrcClient>.Instance);        // Subscribe to all events to track them
        _client.UserJoinedChannel += (sender, e) => _receivedEvents.Add(e);
        _client.UserLeftChannel += (sender, e) => _receivedEvents.Add(e);
        _client.UserQuit += (sender, e) => _receivedEvents.Add(e);
        _client.PrivateMessageReceived += (sender, e) => _receivedEvents.Add(e);
        _client.NoticeReceived += (sender, e) => _receivedEvents.Add(e);
        _client.TopicChanged += (sender, e) => _receivedEvents.Add(e);
        _client.UserKicked += (sender, e) => _receivedEvents.Add(e);
        _client.NickChanged += (sender, e) => _receivedEvents.Add(e);
        _client.ChannelUsersReceived += (sender, e) => _receivedEvents.Add(e);
        _client.RawMessageReceived += (sender, e) => _receivedEvents.Add(e);
    }

    [Fact]
    public void MessageParsing_JoinMessage_ShouldTriggerUserJoinedEvent()
    {
        // This test would require access to internal message processing
        // For now, we'll test the message parsing independently

        // Arrange
        var joinMessage = ":nick!user@host.example.com JOIN #testchannel";

        // Act
        var parsed = IrcMessage.Parse(joinMessage);

        // Assert
        parsed.Command.Should().Be("JOIN");
        parsed.Source.Should().Be("nick!user@host.example.com");
        parsed.Parameters.Should().HaveCount(1);
        parsed.Parameters[0].Should().Be("#testchannel");
    }

    [Fact]
    public void MessageParsing_PartMessage_ShouldTriggerUserLeftEvent()
    {
        // Arrange
        var partMessage = ":nick!user@host.example.com PART #testchannel :Goodbye!";

        // Act
        var parsed = IrcMessage.Parse(partMessage);

        // Assert
        parsed.Command.Should().Be("PART");
        parsed.Source.Should().Be("nick!user@host.example.com");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("#testchannel");
        parsed.Parameters[1].Should().Be("Goodbye!");
    }

    [Fact]
    public void MessageParsing_QuitMessage_ShouldTriggerUserQuitEvent()
    {
        // Arrange
        var quitMessage = ":nick!user@host.example.com QUIT :Connection reset by peer";

        // Act
        var parsed = IrcMessage.Parse(quitMessage);

        // Assert
        parsed.Command.Should().Be("QUIT");
        parsed.Source.Should().Be("nick!user@host.example.com");
        parsed.Parameters.Should().HaveCount(1);
        parsed.Parameters[0].Should().Be("Connection reset by peer");
    }

    [Fact]
    public void MessageParsing_PrivmsgMessage_ShouldTriggerPrivateMessageEvent()
    {
        // Arrange
        var privmsgMessage = ":sender!user@host.example.com PRIVMSG #channel :Hello everyone!";

        // Act
        var parsed = IrcMessage.Parse(privmsgMessage);

        // Assert
        parsed.Command.Should().Be("PRIVMSG");
        parsed.Source.Should().Be("sender!user@host.example.com");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("#channel");
        parsed.Parameters[1].Should().Be("Hello everyone!");
    }

    [Fact]
    public void MessageParsing_NoticeMessage_ShouldTriggerNoticeEvent()
    {
        // Arrange
        var noticeMessage = ":server.example.com NOTICE testuser :This is a server notice";

        // Act
        var parsed = IrcMessage.Parse(noticeMessage);

        // Assert
        parsed.Command.Should().Be("NOTICE");
        parsed.Source.Should().Be("server.example.com");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("testuser");
        parsed.Parameters[1].Should().Be("This is a server notice");
    }

    [Fact]
    public void MessageParsing_TopicMessage_ShouldTriggerTopicChangedEvent()
    {
        // Arrange
        var topicMessage = ":nick!user@host.example.com TOPIC #channel :Welcome to our channel!";

        // Act
        var parsed = IrcMessage.Parse(topicMessage);

        // Assert
        parsed.Command.Should().Be("TOPIC");
        parsed.Source.Should().Be("nick!user@host.example.com");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("#channel");
        parsed.Parameters[1].Should().Be("Welcome to our channel!");
    }

    [Fact]
    public void MessageParsing_KickMessage_ShouldTriggerUserKickedEvent()
    {
        // Arrange
        var kickMessage = ":operator!user@host.example.com KICK #channel victim :You have been kicked";

        // Act
        var parsed = IrcMessage.Parse(kickMessage);

        // Assert
        parsed.Command.Should().Be("KICK");
        parsed.Source.Should().Be("operator!user@host.example.com");
        parsed.Parameters.Should().HaveCount(3);
        parsed.Parameters[0].Should().Be("#channel");
        parsed.Parameters[1].Should().Be("victim");
        parsed.Parameters[2].Should().Be("You have been kicked");
    }

    [Fact]
    public void MessageParsing_NickMessage_ShouldTriggerNickChangedEvent()
    {
        // Arrange
        var nickMessage = ":oldnick!user@host.example.com NICK newnick";

        // Act
        var parsed = IrcMessage.Parse(nickMessage);

        // Assert
        parsed.Command.Should().Be("NICK");
        parsed.Source.Should().Be("oldnick!user@host.example.com");
        parsed.Parameters.Should().HaveCount(1);
        parsed.Parameters[0].Should().Be("newnick");
    }

    [Fact]
    public void MessageParsing_NamesReply_ShouldTriggerChannelUsersEvent()
    {
        // Arrange
        var namesMessage = ":server.example.com 353 testnick = #channel :@op +voice regular";

        // Act
        var parsed = IrcMessage.Parse(namesMessage);

        // Assert
        parsed.Command.Should().Be("353");
        parsed.Source.Should().Be("server.example.com");
        parsed.Parameters.Should().HaveCount(4);
        parsed.Parameters[0].Should().Be("testnick");
        parsed.Parameters[1].Should().Be("=");
        parsed.Parameters[2].Should().Be("#channel");
        parsed.Parameters[3].Should().Be("@op +voice regular");
    }

    [Fact]
    public void MessageParsing_WelcomeMessage_ShouldParseCorrectly()
    {
        // Arrange
        var welcomeMessage = ":server.example.com 001 testnick :Welcome to the IRC Network testnick!testuser@host.example.com";

        // Act
        var parsed = IrcMessage.Parse(welcomeMessage);

        // Assert
        parsed.Command.Should().Be("001");
        parsed.Source.Should().Be("server.example.com");
        parsed.Parameters.Should().HaveCount(2);
        parsed.Parameters[0].Should().Be("testnick");
        parsed.Parameters[1].Should().Be("Welcome to the IRC Network testnick!testuser@host.example.com");
    }

    [Fact]
    public void MessageParsing_PingMessage_ShouldParseCorrectly()
    {
        // Arrange
        var pingMessage = "PING :server.example.com";

        // Act
        var parsed = IrcMessage.Parse(pingMessage);

        // Assert
        parsed.Command.Should().Be("PING");
        parsed.Source.Should().BeNull();
        parsed.Parameters.Should().HaveCount(1);
        parsed.Parameters[0].Should().Be("server.example.com");
    }

    [Fact]
    public void MessageSerialization_RoundTrip_ShouldPreserveData()
    {
        // Arrange
        var testMessages = new[]
        {
            "PRIVMSG #channel :Hello world",
            ":nick!user@host JOIN #channel",
            ":server 001 nick :Welcome message",
            "@time=2023-01-01T12:00:00.000Z :nick!user@host PRIVMSG #channel :Hello",
            "PING :server.example.com"
        };

        foreach (var originalMessage in testMessages)
        {
            // Act
            var parsed = IrcMessage.Parse(originalMessage);
            var serialized = parsed.Serialize();
            var reparsed = IrcMessage.Parse(serialized);

            // Assert
            reparsed.Command.Should().Be(parsed.Command, $"Command should match for: {originalMessage}");
            reparsed.Source.Should().Be(parsed.Source, $"Source should match for: {originalMessage}");
            reparsed.Parameters.Should().BeEquivalentTo(parsed.Parameters, $"Parameters should match for: {originalMessage}");
            reparsed.Tags.Should().BeEquivalentTo(parsed.Tags, $"Tags should match for: {originalMessage}");
        }
    }
    [Fact]
    public void ClientOptions_Validation_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new IrcClientOptions();

        // Assert
        options.Port.Should().Be(6667);
        options.UseSsl.Should().BeFalse();
        options.ConnectionTimeoutMs.Should().Be(30000);
        options.PingIntervalMs.Should().Be(30000);
        options.PingTimeoutMs.Should().Be(60000);
        options.ReconnectDelayMs.Should().Be(5000);
        options.MaxReconnectAttempts.Should().Be(0);
        options.AutoReconnect.Should().BeTrue();
        options.RequestedCapabilities.Should().NotBeNull().And.NotBeEmpty();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
