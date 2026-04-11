using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Configuration;

/// <summary>
/// Tests for timeout configuration in IrcClientOptions and IrcClientOptionsBuilder
/// </summary>
public class TimeoutConfigurationTests
{
    [Fact]
    public void IrcClientOptions_DefaultTimeouts_ShouldBeConservative()
    {
        // Arrange & Act
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User"
        };

        // Assert
        Assert.Equal(5000, options.SendTimeoutMs); // Conservative default
        Assert.Equal(1000, options.SendTimeoutCancelledMs); // Fast when cancelling
        Assert.Equal(30000, options.SendTimeoutWithCancellationMs); // Long for explicit cancellation
    }

    [Fact]
    public void IrcClientOptionsBuilder_WithSendTimeout_ShouldSetTimeoutCorrectly()
    {
        // Arrange & Act
        var options = new IrcClientOptionsBuilder()
            .AddServer("irc.example.com")
            .WithNick("TestBot")
            .WithUserName("testuser")
            .WithRealName("Test User")
            .WithSendTimeout(3000)
            .Build();

        // Assert
        Assert.Equal(3000, options.SendTimeoutMs);
        Assert.Equal(1000, options.SendTimeoutCancelledMs); // Should remain default
        Assert.Equal(30000, options.SendTimeoutWithCancellationMs); // Should remain default
    }

    [Fact]
    public void IrcClientOptionsBuilder_WithSendTimeouts_ShouldSetAllTimeoutsCorrectly()
    {
        // Arrange & Act
        var options = new IrcClientOptionsBuilder()
            .AddServer("irc.example.com")
            .WithNick("TestBot")
            .WithUserName("testuser")
            .WithRealName("Test User")
            .WithSendTimeouts(normalMs: 2000, cancelledMs: 500, withCancellationMs: 15000)
            .Build();

        // Assert
        Assert.Equal(2000, options.SendTimeoutMs);
        Assert.Equal(500, options.SendTimeoutCancelledMs);
        Assert.Equal(15000, options.SendTimeoutWithCancellationMs);
    }

    [Fact]
    public void IrcClientOptions_Clone_ShouldCopyTimeoutValues()
    {
        // Arrange
        var original = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User",
            SendTimeoutMs = 3000,
            SendTimeoutCancelledMs = 800,
            SendTimeoutWithCancellationMs = 25000
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal(original.SendTimeoutMs, clone.SendTimeoutMs);
        Assert.Equal(original.SendTimeoutCancelledMs, clone.SendTimeoutCancelledMs);
        Assert.Equal(original.SendTimeoutWithCancellationMs, clone.SendTimeoutWithCancellationMs);

        // Modify clone to ensure independence
        clone.SendTimeoutMs = 4000;
        Assert.NotEqual(original.SendTimeoutMs, clone.SendTimeoutMs);
    }

    [Fact]
    public void IrcClientOptionsBuilder_FromTemplate_ShouldCopyTimeoutValues()
    {
        // Arrange
        var template = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User",
            SendTimeoutMs = 4000,
            SendTimeoutCancelledMs = 750,
            SendTimeoutWithCancellationMs = 20000
        };

        // Act
        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .AddServer("irc.example.com") // Ensure server is set
            .Build();

        // Assert
        Assert.Equal(template.SendTimeoutMs, options.SendTimeoutMs);
        Assert.Equal(template.SendTimeoutCancelledMs, options.SendTimeoutCancelledMs);
        Assert.Equal(template.SendTimeoutWithCancellationMs, options.SendTimeoutWithCancellationMs);
    }

    [Theory]
    [InlineData(0)] // Zero timeout
    [InlineData(-1)] // Negative timeout
    [InlineData(-1000)] // Large negative timeout
    public void IrcClientOptions_Validate_WithInvalidSendTimeout_ShouldThrowArgumentOutOfRangeException(int invalidTimeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User",
            SendTimeoutMs = invalidTimeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Send timeout must be positive", ex.Message);
    }

    [Theory]
    [InlineData(0)] // Zero timeout
    [InlineData(-1)] // Negative timeout
    public void IrcClientOptions_Validate_WithInvalidSendTimeoutCancelled_ShouldThrowArgumentOutOfRangeException(int invalidTimeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User",
            SendTimeoutCancelledMs = invalidTimeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Send timeout (cancelled) must be positive", ex.Message);
    }

    [Theory]
    [InlineData(0)] // Zero timeout
    [InlineData(-1)] // Negative timeout
    public void IrcClientOptions_Validate_WithInvalidSendTimeoutWithCancellation_ShouldThrowArgumentOutOfRangeException(int invalidTimeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "TestBot",
            UserName = "testuser",
            RealName = "Test User",
            SendTimeoutWithCancellationMs = invalidTimeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Send timeout (with cancellation) must be positive", ex.Message);
    }

    [Fact]
    public void IrcClientOptionsBuilder_FastTestTimeouts_ShouldBuildCorrectly()
    {
        // Arrange & Act - Example of how tests can use fast timeouts
        var options = new IrcClientOptionsBuilder()
            .AddServer("localhost")
            .WithNick("TestBot")
            .WithUserName("testuser")
            .WithRealName("Test User")
            .WithSendTimeout(1000) // Fast timeout for tests
            .WithoutRateLimit() // Disable rate limiting for tests
            .Build();

        // Assert
        Assert.Equal(1000, options.SendTimeoutMs);
        Assert.False(options.EnableRateLimit);
    }

    [Fact]
    public void IrcClientOptionsBuilder_ProductionTimeouts_ShouldBuildCorrectly()
    {
        // Arrange & Act - Example of production configuration
        var options = new IrcClientOptionsBuilder()
            .AddServer("irc.example.com")
            .WithNick("ProductionBot")
            .WithUserName("botuser")
            .WithRealName("Production Bot")
            .WithSendTimeouts(normalMs: 8000, cancelledMs: 1500, withCancellationMs: 45000) // Conservative timeouts
            .WithRateLimit(true, IrcRateLimiter.ConservativeConfig) // Enable conservative rate limiting
            .Build();

        // Assert
        Assert.Equal(8000, options.SendTimeoutMs);
        Assert.Equal(1500, options.SendTimeoutCancelledMs);
        Assert.Equal(45000, options.SendTimeoutWithCancellationMs);
        Assert.True(options.EnableRateLimit);
        Assert.NotNull(options.RateLimitConfig);
    }
}
