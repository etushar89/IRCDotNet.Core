using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests;

/// <summary>
/// Tests for recent improvements including parameter validation, caching, and disposal improvements
/// </summary>
public class RecentImprovementsTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public RecentImprovementsTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    #region Parameter Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task SendMessageAsync_WithInvalidTarget_ShouldThrowArgumentException(string invalidTarget)
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendMessageAsync(invalidTarget, "test message"));
    }

    [Fact]
    public async Task SendMessageAsync_WithNullTarget_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendMessageAsync(null!, "test message"));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task SendMessageAsync_WithInvalidMessage_ShouldThrowArgumentException(string invalidMessage)
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendMessageAsync("#test", invalidMessage));
    }

    [Fact]
    public async Task SendMessageAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendMessageAsync("#test", null!));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task SendNoticeAsync_WithInvalidTarget_ShouldThrowArgumentException(string invalidTarget)
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendNoticeAsync(invalidTarget, "test notice"));
    }

    [Fact]
    public async Task SendNoticeAsync_WithNullTarget_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendNoticeAsync(null!, "test notice"));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task SendNoticeAsync_WithInvalidMessage_ShouldThrowArgumentException(string invalidMessage)
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendNoticeAsync("#test", invalidMessage));
    }

    [Fact]
    public async Task SendNoticeAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendNoticeAsync("#test", null!));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task JoinChannelAsync_WithInvalidChannel_ShouldThrowArgumentException(string invalidChannel)
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.JoinChannelAsync(invalidChannel));
    }

    [Fact]
    public async Task JoinChannelAsync_WithNullChannel_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.JoinChannelAsync(null!));
    }

    [Fact]
    public async Task JoinChannelAsync_WithValidChannelAndKey_ShouldNotThrow()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert - Should throw InvalidOperationException (not connected) but not ArgumentException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.JoinChannelAsync("#test", "secretkey"));
    }

    #endregion

    #region Caching System Tests

    [Fact]
    public void Channels_PropertyAccess_ShouldUseCaching()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Access the property multiple times
        var channels1 = client.Channels;
        var channels2 = client.Channels;
        var channels3 = client.Channels;

        // Assert - Should return the same cached instance
        Assert.Same(channels1, channels2);
        Assert.Same(channels2, channels3);
        Assert.NotNull(channels1);
        Assert.Empty(channels1);
    }

    [Fact]
    public void EnabledCapabilities_PropertyAccess_ShouldUseCaching()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Access the property multiple times
        var caps1 = client.EnabledCapabilities;
        var caps2 = client.EnabledCapabilities;
        var caps3 = client.EnabledCapabilities;

        // Assert - Should return the same cached instance
        Assert.Same(caps1, caps2);
        Assert.Same(caps2, caps3);
        Assert.NotNull(caps1);
        Assert.Empty(caps1);
    }

    [Fact]
    public void Channels_AfterMultipleAccess_ShouldReturnImmutableCopy()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act
        var channels = client.Channels;

        // Assert - Should be read-only and immutable from caller perspective
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, IReadOnlySet<string>>>(channels);

        // The returned collections should be immutable snapshots
        Assert.NotNull(channels);
        Assert.Empty(channels);
    }

    [Fact]
    public void EnabledCapabilities_AfterMultipleAccess_ShouldReturnImmutableCopy()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act
        var capabilities = client.EnabledCapabilities;

        // Assert - Should be read-only and immutable from caller perspective
        Assert.IsAssignableFrom<IReadOnlySet<string>>(capabilities);
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    #endregion

    #region Disposal Pattern Tests

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Call dispose multiple times
        client.Dispose();
        client.Dispose();
        client.Dispose();

        // Assert - Should not throw
        Assert.True(true); // Test passes if no exception is thrown
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Call async dispose multiple times
        await client.DisposeAsync();
        await client.DisposeAsync();
        await client.DisposeAsync();

        // Assert - Should not throw
        Assert.True(true); // Test passes if no exception is thrown
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_ShouldBeIdempotent()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Call both dispose methods
        client.Dispose();
        await client.DisposeAsync();

        // Assert - Should not throw
        Assert.True(true); // Test passes if no exception is thrown
    }

    [Fact]
    public async Task Dispose_AfterDisposeAsync_ShouldBeIdempotent()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Call both dispose methods in reverse order
        await client.DisposeAsync();
        client.Dispose();

        // Assert - Should not throw
        Assert.True(true); // Test passes if no exception is thrown
    }

    #endregion

    #region API Contract Tests

    [Fact]
    public async Task SendRawAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendRawAsync("PING :test"));
    }
    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // We need to simulate the connected state by setting the internal flag
        // Since _isConnected is private, we'll use reflection to set it
        var isConnectedField = client.GetType().GetField("_isConnected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        isConnectedField?.SetValue(client, true);

        // Act & Assert - Second connection attempt should throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    #endregion

    #region Performance and Memory Tests

    [Fact]
    public void Properties_AccessedFrequently_ShouldNotCreateExcessiveObjects()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Access properties many times to test caching efficiency
        var channels = new List<IReadOnlyDictionary<string, IReadOnlySet<string>>>();
        var capabilities = new List<IReadOnlySet<string>>();

        for (int i = 0; i < 100; i++)
        {
            channels.Add(client.Channels);
            capabilities.Add(client.EnabledCapabilities);
        }

        // Assert - Due to caching, all references should be the same
        var firstChannels = channels[0];
        var firstCapabilities = capabilities[0];

        Assert.All(channels, c => Assert.Same(firstChannels, c));
        Assert.All(capabilities, c => Assert.Same(firstCapabilities, c));
    }

    #endregion

    #region Regex Performance Tests

    [Fact]
    public void ParseNickUserHost_RegexCompilation_ShouldNotThrow()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // We can't directly test the private ParseNickUserHost method,
        // but we can verify that the regex compilation works by ensuring
        // the client initializes without issues and properties work

        // Act & Assert - If regex compilation failed, this would throw
        var nick = client.CurrentNick;
        var channels = client.Channels;
        var capabilities = client.EnabledCapabilities;

        Assert.NotNull(nick);
        Assert.NotNull(channels);
        Assert.NotNull(capabilities);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new IrcClient(null!, _logger));
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitializePropertiesCorrectly()
    {
        // Arrange
        var options = CreateTestOptions();

        // Act
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Assert
        Assert.False(client.IsConnected);
        Assert.False(client.IsRegistered);
        Assert.Equal(options.Nick, client.CurrentNick);
        Assert.NotNull(client.Channels);
        Assert.NotNull(client.EnabledCapabilities);
        Assert.Empty(client.Channels);
        Assert.Empty(client.EnabledCapabilities);
    }

    #endregion

    #region Helper Methods

    private static IrcClientOptions CreateTestOptions()
    {
        return new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 1000
        };
    }

    #endregion

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            try
            {
                client?.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
    }
}
