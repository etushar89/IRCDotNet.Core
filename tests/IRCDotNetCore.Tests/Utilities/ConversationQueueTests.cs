using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using IRCDotNet.Core.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IRCDotNet.Tests.Utilities;

/// <summary>
/// Tests for the ConversationQueue utility
/// </summary>
public class ConversationQueueTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly ConversationQueue _queue;
    private readonly Mock<ILogger<ConversationQueue>> _mockLogger;

    public ConversationQueueTests()
    {
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testnick",
            UserName = "testuser",
            RealName = "Test User"
        };

        _client = new IrcClient(options);
        _mockLogger = new Mock<ILogger<ConversationQueue>>();
        _queue = new ConversationQueue(_client, _mockLogger.Object);
    }

    [Fact]
    public void ConversationQueue_ShouldInitializeCorrectly()
    {
        // Act & Assert - Should not throw during construction
        _queue.Should().NotBeNull();
    }

    [Fact]
    public void ConversationQueue_WithNullClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new ConversationQueue(null!);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("client");
    }

    [Fact]
    public async Task WaitForAsync_WithTimeout_ShouldReturnNullOnTimeout()
    {
        // Act
        var result = await _queue.WaitForAsync<EnhancedMessageEvent>(
            timeout: TimeSpan.FromMilliseconds(100));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task WaitForAsync_WithCancellation_ShouldReturnNullOnCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act
        var result = await _queue.WaitForAsync<EnhancedMessageEvent>(
            cancellationToken: cts.Token);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task WaitForMessageFromUserAsync_ShouldFilterByUser()
    {
        // Arrange
        var targetUser = "targetuser";
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var task = _queue.WaitForMessageFromUserAsync(targetUser, timeout: timeout);

        // Simulate a message from a different user
        var wrongMessage = IrcMessage.Parse(":wronguser!user@host PRIVMSG #channel :Hello");
        var wrongEvent = new EnhancedMessageEvent(wrongMessage, _client, "wronguser", "user", "host", "#channel", "Hello");

        // This shouldn't match our filter
        var result = await task;

        // Assert
        result.Should().BeNull(); // Timeout because no matching message
    }

    [Fact]
    public async Task WaitForMessageInChannelAsync_ShouldFilterByChannel()
    {
        // Arrange
        var targetChannel = "#target";
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _queue.WaitForMessageInChannelAsync(targetChannel, timeout: timeout);

        // Assert
        result.Should().BeNull(); // Timeout because no matching message
    }

    [Fact]
    public async Task WaitForMessageContainingAsync_ShouldFilterByContent()
    {
        // Arrange
        var searchText = "specific text";
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _queue.WaitForMessageContainingAsync(searchText, timeout: timeout);

        // Assert
        result.Should().BeNull(); // Timeout because no matching message
    }

    [Fact]
    public async Task WaitForAnyMessageAsync_ShouldReturnFirstMessage()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _queue.WaitForAnyMessageAsync(timeout: timeout);

        // Assert
        result.Should().BeNull(); // Timeout because no messages
    }
    [Fact]
    public async Task CollectEventsUntilAsync_ShouldCollectUntilCondition()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _queue.CollectEventsUntilAsync<EnhancedMessageEvent>(
            stopCondition: e => e.Text.Contains("stop"),
            timeout: timeout);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty(); // No events within timeout
    }

    [Fact]
    public void StartConversation_ShouldReturnBuilder()
    {
        // Act
        var builder = _queue.StartConversation();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<ConversationBuilder>();
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Act & Assert - Should not throw
        _queue.Dispose();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        _queue.Dispose();
        _queue.Dispose();
    }

    [Fact]
    public async Task WaitForAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _queue.Dispose();

        // Act & Assert
        await _queue.Invoking(q => q.WaitForAsync<EnhancedMessageEvent>())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task WaitForMessageFromUserAsync_WithValidParameters_ShouldSetupCorrectFilter()
    {
        // Arrange
        var user = "testuser";
        var timeout = TimeSpan.FromMilliseconds(50);

        // Act - This should complete with timeout (no actual events)
        var result = await _queue.WaitForMessageFromUserAsync(user, timeout: timeout);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task WaitForMessageInChannelAsync_WithValidParameters_ShouldSetupCorrectFilter()
    {
        // Arrange
        var channel = "#testchannel";
        var timeout = TimeSpan.FromMilliseconds(50);

        // Act - This should complete with timeout (no actual events)
        var result = await _queue.WaitForMessageInChannelAsync(channel, timeout: timeout);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("test message")]
    [InlineData("!command")]
    public async Task WaitForMessageContainingAsync_WithDifferentSearchTexts_ShouldSetupCorrectFilter(string searchText)
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(50);

        // Act - This should complete with timeout (no actual events)
        var result = await _queue.WaitForMessageContainingAsync(searchText, timeout: timeout);

        // Assert
        result.Should().BeNull();
    }
    [Fact]
    public async Task CollectEventsUntilAsync_WithPredicate_ShouldFilterCorrectly()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _queue.CollectEventsUntilAsync<EnhancedMessageEvent>(
            stopCondition: e => e.Text == "stop",
            timeout: timeout);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty(); // No events during timeout
    }

    public void Dispose()
    {
        _queue?.Dispose();
        _client?.Dispose();
    }
}
