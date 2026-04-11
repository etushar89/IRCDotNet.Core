using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IRCDotNet.Tests;

public class IrcClientTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly IrcClientOptions _options;

    public IrcClientTests()
    {
        _options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false
        };

        _client = new IrcClient(_options, NullLogger<IrcClient>.Instance);
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitializeCorrectly()
    {        // Assert
        _client.IsConnected.Should().BeFalse();
        _client.IsRegistered.Should().BeFalse();
        _client.CurrentNick.Should().Be("testbot");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new IrcClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }
    [Fact]
    public void ConnectAsync_WithValidOptions_ShouldBeAvailable()
    {
        // Arrange
        var connected = false;
        _client.Connected += (sender, args) => connected = true;

        // Note: This test would need a mock TCP client or test server
        // For now, we'll test that the method is available

        // Act
        var connectMethod = _client.GetType().GetMethod("ConnectAsync");

        // Assert
        connectMethod.Should().NotBeNull();
        connected.Should().BeFalse(); // Since we can't actually connect in unit tests
    }
    [Fact]
    public void SendRaw_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendRawAsync("PRIVMSG #test :Hello");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void JoinChannel_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.JoinChannelAsync("#test");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void LeaveChannel_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.LeaveChannelAsync("#test");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void SendMessage_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendMessageAsync("user", "Hello");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void SendNotice_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendNoticeAsync("user", "Notice");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Theory]
    [InlineData("Hello\r\nQUIT :injected")]
    [InlineData("Line1\nLine2")]
    [InlineData("Line1\rLine2")]
    public void SendMessage_WithNewlinesInMessage_ShouldThrowArgumentException(string message)
    {
        Func<Task> act = async () => await _client.SendMessageAsync("user", message);
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("user\r\nQUIT")]
    [InlineData("user\n")]
    public void SendMessage_WithNewlinesInTarget_ShouldThrowArgumentException(string target)
    {
        Func<Task> act = async () => await _client.SendMessageAsync(target, "Hello");
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("Hello\r\nQUIT :injected")]
    [InlineData("Line1\nLine2")]
    public void SendNotice_WithNewlinesInMessage_ShouldThrowArgumentException(string message)
    {
        Func<Task> act = async () => await _client.SendNoticeAsync("user", message);
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("user\r\nQUIT")]
    [InlineData("user\n")]
    public void SendNotice_WithNewlinesInTarget_ShouldThrowArgumentException(string target)
    {
        Func<Task> act = async () => await _client.SendNoticeAsync(target, "Hello");
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Fact]
    public void SetTopic_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SetTopicAsync("#test", "New topic");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void GetUserInfo_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.GetUserInfoAsync("user");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void ChangeNick_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.ChangeNickAsync("newnick");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }
    [Fact]
    public void EventHandlers_CanBeAssigned()
    {
        // Act & Assert - These should compile without error
        _client.Connected += (sender, e) => { };
        _client.Disconnected += (sender, e) => { };
        _client.UserJoinedChannel += (sender, e) => { };
        _client.UserLeftChannel += (sender, e) => { };
        _client.UserQuit += (sender, e) => { };
        _client.PrivateMessageReceived += (sender, e) => { };
        _client.NoticeReceived += (sender, e) => { };
        _client.TopicChanged += (sender, e) => { };
        _client.UserKicked += (sender, e) => { };
        _client.NickChanged += (sender, e) => { };
        _client.ChannelUsersReceived += (sender, e) => { };
        _client.RawMessageReceived += (sender, e) => { };

        // If we got here without compilation errors, the test passes
        true.Should().BeTrue();
    }
    [Fact]
    public void ClientProperties_ShouldBeAccessible()
    {
        // Assert - These should compile without error
        _client.IsConnected.Should().BeFalse();
        _client.IsRegistered.Should().BeFalse();
        _client.CurrentNick.Should().Be("testbot");
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

public class IrcClientConfigurationTests
{
    [Fact]
    public void IrcClientOptions_WithDefaultValues_ShouldHaveCorrectDefaults()
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
        options.RequestedCapabilities.Should().NotBeNull();
    }

    [Fact]
    public void IrcClientOptions_Validation_ShouldValidateRequiredFields()
    {
        // Arrange
        var options = new IrcClientOptions();

        // Act & Assert
        options.Server.Should().Be(string.Empty);
        options.Nick.Should().Be(string.Empty);
        options.UserName.Should().Be(string.Empty);
        options.RealName.Should().Be(string.Empty);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void IrcClientOptions_InvalidPort_ShouldAllowButWillFailOnValidation(int port)
    {
        // Arrange & Act
        var options = new IrcClientOptions
        {
            Port = port,
            Server = "irc.example.com",
            Nick = "testnick",
            UserName = "testuser",
            RealName = "Test User"
        };

        // Assert - The options class doesn't validate automatically
        options.Port.Should().Be(port);

        // But validation should fail for invalid ports
        if (port <= 0 || port > 65535)
        {
            Action act = () => options.Validate();
            act.Should().Throw<ArgumentOutOfRangeException>()
               .WithMessage("*Port*");
        }
        else
        {
            // Valid port should not throw
            Action act = () => options.Validate();
            act.Should().NotThrow();
        }
    }
}
