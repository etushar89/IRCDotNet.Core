using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IRCDotNet.Tests;

/// <summary>
/// Tests for the IrcBotManager functionality
/// </summary>
public class IrcBotManagerTests : IDisposable
{
    private readonly IrcBotManager _manager;
    private readonly Mock<ILogger<IrcBotManager>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;

    public IrcBotManagerTests()
    {
        _mockLogger = new Mock<ILogger<IrcBotManager>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _manager = new IrcBotManager(_mockLogger.Object, _mockLoggerFactory.Object);
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
    public async Task AddBotAsync_WithEmptyName_ShouldThrowArgumentException()
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
        await _manager.Invoking(m => m.AddBotAsync("", config))
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
    public async Task AddBotAsync_WithDuplicateName_ShouldThrowInvalidOperationException()
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

        // Act & Assert
        await _manager.Invoking(m => m.AddBotAsync("testbot", config))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bot with name 'testbot' already exists*");
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
    public async Task AddBotsFromTemplateAsync_ShouldCreateMultipleBots()
    {
        // Arrange
        var template = new IrcClientOptions
        {
            UserName = "templatebot",
            RealName = "Template Bot"
        };

        var servers = new[]
        {
            ("bot1", "server1.com", 6667, false),
            ("bot2", "server2.com", 6697, true)
        };

        // Act
        await _manager.AddBotsFromTemplateAsync(template, servers);

        // Assert
        _manager.Bots.Should().HaveCount(2);
        _manager.Bots.Should().ContainKey("bot1");
        _manager.Bots.Should().ContainKey("bot2");
    }

    [Fact]
    public void GetAllBotStatus_WithNoBots_ShouldReturnEmptyDictionary()
    {
        // Act
        var statuses = _manager.GetAllBotStatus();

        // Assert
        statuses.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllBotStatus_WithBots_ShouldReturnStatuses()
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
        var statuses = _manager.GetAllBotStatus();

        // Assert
        statuses.Should().HaveCount(1);
        statuses.Should().ContainKey("testbot");
        statuses["testbot"].Should().NotBeNull();
    }

    [Fact]
    public async Task StartAllBotsAsync_WithBots_ShouldAttemptToStartAll()
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
        await _manager.StartAllBotsAsync();

        // Assert
        _manager.Bots.Should().HaveCount(1);
        _manager.Bots.Should().ContainKey("testbot");
        // Note: The result may be false since we're not actually connecting to a server
    }

    [Fact]
    public async Task StopAllBotsAsync_WithBots_ShouldStopAll()
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
        await _manager.StopAllBotsAsync("Test shutdown");

        // Assert - Should not throw and should complete successfully
        _manager.Bots.Should().HaveCount(1); // Bots should still exist but be disconnected
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

    [Fact]
    public void DisposeAsync_ShouldCleanupResources()
    {
        // Act & Assert - Should not throw
        _manager.Dispose();
    }

    public void Dispose()
    {
        _manager?.Dispose();
    }
}
