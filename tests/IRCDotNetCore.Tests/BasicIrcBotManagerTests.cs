using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Xunit;

namespace IRCDotNet.Tests;

/// <summary>
/// Basic tests for the IrcBotManager functionality
/// </summary>
public class BasicIrcBotManagerTests : IDisposable
{
    private readonly IrcBotManager _manager;

    public BasicIrcBotManagerTests()
    {
        _manager = new IrcBotManager();
    }

    [Fact]
    public void IrcBotManager_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _manager.IsRunning.Should().BeFalse();
        _manager.Bots.Should().BeEmpty();
    }

    [Fact]
    public async Task AddBotAsync_WithValidConfiguration_ShouldAddBot()
    {
        // Arrange
        var config = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testbot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act
        var bot = await _manager.AddBotAsync("testbot", config);

        // Assert
        bot.Should().NotBeNull();
        _manager.Bots.Should().HaveCount(1);
        _manager.Bots.Should().ContainKey("testbot");
        _manager.Bots["testbot"].Should().BeSameAs(bot);
    }

    [Fact]
    public async Task AddBotAsync_WithNullName_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testbot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act & Assert
        await _manager.Invoking(m => m.AddBotAsync(null!, config))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Bot name cannot be null or whitespace*");
    }

    [Fact]
    public async Task AddBotAsync_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await _manager.Invoking(m => m.AddBotAsync("testbot", (IrcClientOptions)null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public async Task AddBotAsync_WithBuilder_ShouldCreateBot()
    {
        // Act
        var bot = await _manager.AddBotAsync("testbot", builder => builder
            .WithNick("testbot")
            .WithUserName("testbot")
            .WithRealName("Test Bot")
            .AddServer("test.server.com", 6667));

        // Assert
        bot.Should().NotBeNull();
        _manager.Bots.Should().HaveCount(1);
        _manager.Bots.Should().ContainKey("testbot");
    }

    [Fact]
    public async Task RemoveBotAsync_WithExistingBot_ShouldRemoveBot()
    {
        // Arrange
        var config = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testbot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        await _manager.AddBotAsync("testbot", config);

        // Act
        var result = await _manager.RemoveBotAsync("testbot");

        // Assert
        result.Should().BeTrue();
        _manager.Bots.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveBotAsync_WithNonExistentBot_ShouldReturnFalse()
    {
        // Act
        var result = await _manager.RemoveBotAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetBotAsync_WithExistingBot_ShouldReturnBot()
    {
        // Arrange
        var config = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "testbot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        var addedBot = await _manager.AddBotAsync("testbot", config);

        // Act
        var retrievedBot = _manager.GetBot("testbot");

        // Assert
        retrievedBot.Should().BeSameAs(addedBot);
    }

    [Fact]
    public void GetBotAsync_WithNonExistentBot_ShouldReturnNull()
    {
        // Act
        var bot = _manager.GetBot("nonexistent");

        // Assert
        bot.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_ShouldSetIsRunningToTrue()
    {
        // Act
        await _manager.StartAsync(CancellationToken.None);

        // Assert
        _manager.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ShouldSetIsRunningToFalse()
    {
        // Arrange
        await _manager.StartAsync(CancellationToken.None);

        // Act
        await _manager.StopAsync(CancellationToken.None);

        // Assert
        _manager.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Act & Assert - Should not throw
        _manager.Dispose();
    }

    public void Dispose()
    {
        _manager?.Dispose();
    }
}
