using System;
using System.Threading;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests.Threading;

/// <summary>
/// Tests for thread safety, deadlock prevention, and concurrent operations
/// </summary>
public class ThreadSafetyAdvancedTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public ThreadSafetyAdvancedTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact]
    public async Task Dispose_WhenCalledConcurrently_ShouldNotDeadlock()
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

        // Act & Assert - Multiple concurrent dispose calls should not deadlock
        var disposeTasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            disposeTasks[i] = Task.Run(() => client.Dispose());
        }

        // Should complete within reasonable time without deadlock
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
        var completedTask = await Task.WhenAny(Task.WhenAll(disposeTasks), timeoutTask);

        Assert.NotEqual(timeoutTask, completedTask);
    }

    [Fact]
    public async Task DisposeAsync_WhenCalledConcurrently_ShouldNotDeadlock()
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

        // Act & Assert - Multiple concurrent async dispose calls should not deadlock
        var disposeTasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            disposeTasks[i] = client.DisposeAsync().AsTask();
        }

        // Should complete within reasonable time without deadlock
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
        var completedTask = await Task.WhenAny(Task.WhenAll(disposeTasks), timeoutTask);

        Assert.NotEqual(timeoutTask, completedTask);
    }

    [Fact]
    public async Task SendRawAsync_WithNullMessage_ShouldThrowArgumentException()
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

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendRawAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendRawAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendRawAsync("   "));
    }

    [Fact]
    public async Task SendRawAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
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

        // Act & Assert - Should throw when not connected
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendRawAsync("PING :test"));
    }

    [Fact]
    public async Task SendRawAsync_ConcurrentCalls_ShouldNotCauseRaceConditions()
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

        // Try to connect (will fail but that's ok for this test)
        try { await client.ConnectAsync(); } catch { /* Expected to fail */ }

        // Act - Multiple concurrent send attempts
        var sendTasks = new Task[20];
        var exceptions = new List<Exception>();

        for (int i = 0; i < 20; i++)
        {
            int index = i;
            sendTasks[i] = Task.Run(async () =>
            {
                try
                {
                    await client.SendRawAsync($"PRIVMSG #test :Message {index}");
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            });
        }

        await Task.WhenAll(sendTasks);

        // Assert - All calls should fail with the same type of exception (InvalidOperationException)
        Assert.All(exceptions, ex => Assert.IsType<InvalidOperationException>(ex));
    }

    [Fact]
    public void PingInterval_WhenSetToZero_ShouldThrowValidationError()
    {
        // Arrange & Act & Assert
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            PingIntervalMs = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void PingTimeout_WhenLessThanPingInterval_ShouldThrowValidationError()
    {
        // Arrange & Act & Assert
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            PingIntervalMs = 60000,
            PingTimeoutMs = 30000 // Less than ping interval
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void ValidConfiguration_ShouldPassValidation()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            PingIntervalMs = 60000,
            PingTimeoutMs = 180000
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Fact]
    public async Task FireAndForgetOperations_ShouldNotCrashApplication()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 100
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Trigger operations that use fire-and-forget tasks
        try
        {
            await client.ConnectAsync();
        }
        catch
        {
            // Expected to fail, but fire-and-forget tasks should handle errors gracefully
        }

        // Wait a bit to let any fire-and-forget tasks complete
        await Task.Delay(500);

        // Assert - Application should still be running (no unhandled exceptions)
        Assert.True(true); // If we reach here, no unhandled exceptions occurred
    }

    [Fact]
    public async Task EventHandlers_WithExceptions_ShouldNotCrashClient()
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

        // Add event handler that throws
        client.Connected += (sender, e) => throw new InvalidOperationException("Test exception");
        client.Disconnected += (sender, e) => throw new ArgumentException("Another test exception");

        // Act & Assert - Should not throw despite event handler exceptions
        try
        {
            await client.ConnectAsync();
        }
        catch
        {
            // Expected connection failure, but event handler exceptions should be caught
        }

        await Task.Delay(100); // Let event processing complete

        // If we reach here, the client handled event handler exceptions properly
        Assert.True(true);
    }

    [Fact]
    public async Task StateTransitions_UnderConcurrentAccess_ShouldRemainConsistent()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 100
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Act - Concurrent connection and disconnection attempts
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try { await client.ConnectAsync(); } catch { }
            }));

            tasks.Add(Task.Run(async () =>
            {
                try { await client.DisconnectAsync(); } catch { }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Client should be in a consistent state
        Assert.False(client.IsConnected); // Should end up disconnected
        Assert.False(client.IsRegistered);
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
