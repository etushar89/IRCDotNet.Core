using System;
using System.Threading;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests.Resilience;

/// <summary>
/// Tests for error handling, timeouts, and resilience scenarios
/// </summary>
public class ResilienceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public ResilienceTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact]
    public async Task ConnectAsync_WithVeryShortTimeout_ShouldTimeoutGracefully()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "192.0.2.1", // RFC5737 test IP that should not respond
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 100 // Very short timeout
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task SendRawAsync_AfterDisconnect_ShouldFailGracefully()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User"
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Disconnect without connecting
        await client.DisconnectAsync();

        // Assert - Send should fail gracefully
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendRawAsync("PRIVMSG #test :Hello"));
    }

    [Fact]
    public async Task MultipleDisconnectCalls_ShouldBeIdempotent()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User"
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Multiple disconnect calls
        await client.DisconnectAsync("First disconnect");
        await client.DisconnectAsync("Second disconnect");
        await client.DisconnectAsync("Third disconnect");

        // Assert - Should complete without errors
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConcurrentConnectAndDisconnect_ShouldHandleGracefully()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 1000
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Concurrent connect and disconnect operations
        var tasks = new Task[20];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try { await client.ConnectAsync(); }
                catch { /* Expected failures */ }
            });
        }

        for (int i = 10; i < 20; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try { await client.DisconnectAsync(); }
                catch { /* Expected failures */ }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Should end in consistent state
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SendLock_WithCancellation_ShouldReleaseProperlyOnTimeout()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User"
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Try to connect to create initial state
        try { await client.ConnectAsync(); } catch { }

        // Act - Multiple concurrent sends that should timeout
        var sendTasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            sendTasks[i] = Task.Run(async () =>
            {
                try
                {
                    await client.SendRawAsync($"PRIVMSG #test :Message {i}");
                }
                catch
                {
                    // Expected to fail
                }
            });
        }

        // Wait for all to complete with timeout
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(35)); // Longer than send timeout
        var completedTask = await Task.WhenAny(Task.WhenAll(sendTasks), timeoutTask);

        // Assert - Should complete within timeout (not hang indefinitely)
        Assert.NotEqual(timeoutTask, completedTask);
    }

    [Fact]
    public void SaslOptions_WithInvalidConfiguration_ShouldFailValidation()
    {
        // Arrange
        var saslOptions = new SaslOptions
        {
            Mechanism = "PLAIN",
            Username = "", // Invalid - empty username
            Password = "password"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => saslOptions.Validate());
    }

    [Fact]
    public void SaslOptions_WithEmptyMechanism_ShouldFailValidation()
    {
        // Arrange
        var saslOptions = new SaslOptions
        {
            Mechanism = "", // Invalid
            Username = "user",
            Password = "password"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => saslOptions.Validate());
    }

    [Fact]
    public void SaslOptions_Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = new SaslOptions
        {
            Mechanism = "PLAIN",
            Username = "user",
            Password = "password",
            TimeoutMs = 30000,
            Required = true
        };
        original.AdditionalParameters["custom"] = "value";

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal(original.Mechanism, clone.Mechanism);
        Assert.Equal(original.Username, clone.Username);
        Assert.Equal(original.Password, clone.Password);
        Assert.Equal(original.TimeoutMs, clone.TimeoutMs);
        Assert.Equal(original.Required, clone.Required);
        Assert.Equal(original.AdditionalParameters["custom"], clone.AdditionalParameters["custom"]);

        // Modify clone to ensure independence
        clone.Username = "different";
        clone.AdditionalParameters["custom"] = "different";

        Assert.NotEqual(original.Username, clone.Username);
        Assert.NotEqual(original.AdditionalParameters["custom"], clone.AdditionalParameters["custom"]);
    }

    [Fact]
    public void IrcClientOptions_Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6697,
            UseSsl = true,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            PingIntervalMs = 90000,
            PingTimeoutMs = 270000,
            Sasl = new SaslOptions { Username = "user", Password = "pass" }
        };
        original.AlternativeNicks.Add("TestUser2");
        original.RequestedCapabilities.Add("custom-cap");

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal(original.Server, clone.Server);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.UseSsl, clone.UseSsl);
        Assert.Equal(original.Nick, clone.Nick);
        Assert.Equal(original.PingIntervalMs, clone.PingIntervalMs);
        Assert.Equal(original.AlternativeNicks.Count, clone.AlternativeNicks.Count);
        Assert.Equal(original.RequestedCapabilities.Count, clone.RequestedCapabilities.Count);

        // Ensure SASL options are cloned
        Assert.NotNull(clone.Sasl);
        Assert.Equal(original.Sasl.Username, clone.Sasl.Username);

        // Modify clone to ensure independence
        clone.Server = "different.com";
        clone.AlternativeNicks.Add("TestUser3");
        clone.Sasl!.Username = "different";

        Assert.NotEqual(original.Server, clone.Server);
        Assert.NotEqual(original.AlternativeNicks.Count, clone.AlternativeNicks.Count);
        Assert.NotEqual(original.Sasl.Username, clone.Sasl.Username);
    }

    [Fact]
    public async Task EventHandler_Exceptions_ShouldNotStopOtherEventHandlers()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User"
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        // Add multiple event handlers, with one that throws
        client.Disconnected += (sender, e) => { handler1Called = true; };
        client.Disconnected += (sender, e) => { throw new InvalidOperationException("Test exception"); };
        client.Disconnected += (sender, e) => { handler2Called = true; };
        client.Disconnected += (sender, e) => { handler3Called = true; };

        // Act - Trigger disconnect event
        await client.DisconnectAsync("Test");

        // Wait for event processing
        await Task.Delay(100);

        // Assert - All handlers except the throwing one should have been called
        Assert.True(handler1Called);
        Assert.True(handler2Called);
        Assert.True(handler3Called);
    }

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
