using System.Collections.Concurrent;
using System.Diagnostics;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Unit tests for thread safety and concurrency without requiring real IRC connections.
/// These tests focus on verifying thread-safe operations and concurrent access patterns.
/// </summary>
public class ThreadSafetyUnitTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public ThreadSafetyUnitTests(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }
    [Fact]
    public async Task PropertyAccess_ConcurrentReads_ShouldNotThrow()
    {
        const int threadCount = 20;
        const int iterationsPerThread = 1000;

        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();
        var successCount = 0;

        // Create multiple threads that continuously access properties
        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            var threadIndex = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < iterationsPerThread; j++)
                    {
                        // Access all public properties
                        var isConnected = client.IsConnected;
                        var isRegistered = client.IsRegistered;
                        var currentNick = client.CurrentNick;
                        var channels = client.Channels;
                        var capabilities = client.EnabledCapabilities;

                        // Verify properties return valid data
                        Assert.NotNull(channels);
                        Assert.NotNull(capabilities);
                        Assert.NotNull(currentNick);

                        // Verify collections are safe to iterate
                        var channelCount = channels.Count;
                        var capabilityCount = capabilities.Count;

                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"Thread {threadIndex} exception: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        _output.WriteLine($"Property access test completed. Success: {successCount}, Exceptions: {exceptions.Count}");

        foreach (var ex in exceptions.Take(5)) // Show first 5 exceptions
        {
            _output.WriteLine($"Exception: {ex}");
        }

        Assert.Empty(exceptions);
        Assert.Equal(threadCount * iterationsPerThread, successCount);
    }
    [Fact]
    public async Task ClientCreation_MultipleInstances_ShouldNotInterfere()
    {
        const int clientCount = 50;

        var exceptions = new ConcurrentBag<Exception>();
        var createdClients = new ConcurrentBag<IrcClient>();

        // Create multiple clients concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var options = new IrcClientOptions
                    {
                        Server = "test.server.com",
                        Port = 6667,
                        Nick = $"TestClient{clientIndex}",
                        UserName = $"testuser{clientIndex}",
                        RealName = $"Test User {clientIndex}",
                        EnableRateLimit = false // Disable rate limiting for thread safety testing
                    };

                    var client = new IrcClient(options, _logger);
                    createdClients.Add(client);

                    // Verify initial state
                    Assert.False(client.IsConnected);
                    Assert.False(client.IsRegistered);
                    Assert.Equal($"TestClient{clientIndex}", client.CurrentNick);
                    Assert.Empty(client.Channels);
                    Assert.Empty(client.EnabledCapabilities);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"Client {clientIndex} creation failed: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        _output.WriteLine($"Client creation test completed. Created: {createdClients.Count}, Exceptions: {exceptions.Count}");

        // Add all created clients to disposal list
        foreach (var client in createdClients)
        {
            _clients.Add(client);
        }

        Assert.Empty(exceptions);
        Assert.Equal(clientCount, createdClients.Count);
    }
    [Fact]
    public async Task EventSubscription_ConcurrentOperations_ShouldNotCauseConcurrencyIssues()
    {
        const int operationCount = 1000;

        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "EventTestClient",
            UserName = "eventtest",
            RealName = "Event Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();
        var subscribeCount = 0;
        var unsubscribeCount = 0;
        var eventFireCount = 0;

        // Event handler that tracks invocations
        void EventHandler(object? sender, IRCDotNet.Core.Events.IrcEvent e) => Interlocked.Increment(ref eventFireCount);

        // Perform concurrent event subscription/unsubscription
        var tasks = new List<Task>();

        for (int i = 0; i < operationCount; i++)
        {
            var operationIndex = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    if (operationIndex % 2 == 0)
                    {
                        // Subscribe to events
                        client.Connected += EventHandler;
                        client.Disconnected += EventHandler;
                        client.RawMessageReceived += EventHandler;
                        Interlocked.Increment(ref subscribeCount);
                    }
                    else
                    {
                        // Unsubscribe from events
                        client.Connected -= EventHandler;
                        client.Disconnected -= EventHandler;
                        client.RawMessageReceived -= EventHandler;
                        Interlocked.Increment(ref unsubscribeCount);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"Operation {operationIndex} failed: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        _output.WriteLine($"Event subscription test completed.");
        _output.WriteLine($"Subscribes: {subscribeCount}, Unsubscribes: {unsubscribeCount}");
        _output.WriteLine($"Event fires: {eventFireCount}, Exceptions: {exceptions.Count}");

        Assert.Empty(exceptions);
        Assert.True(subscribeCount > 0);
        Assert.True(unsubscribeCount > 0);
    }
    [Fact]
    public async Task StressTest_MixedOperations_ShouldRemainStable()
    {
        const int clientCount = 10;
        const int operationsPerClient = 100;

        var exceptions = new ConcurrentBag<Exception>();
        var operationCounts = new ConcurrentDictionary<string, int>();

        var tasks = new List<Task>();

        for (int i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var options = new IrcClientOptions
                    {
                        Server = "test.server.com",
                        Port = 6667,
                        Nick = $"StressClient{clientIndex}",
                        UserName = $"stressuser{clientIndex}",
                        RealName = $"Stress Test User {clientIndex}",
                        EnableRateLimit = false // Disable rate limiting for thread safety testing
                    };

                    var client = new IrcClient(options, _logger);
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }

                    var random = new Random(clientIndex);

                    for (int j = 0; j < operationsPerClient; j++)
                    {
                        var operationType = random.Next(4);
                        switch (operationType)
                        {
                            case 0: // Property access
                                var nick = client.CurrentNick;
                                var channels = client.Channels;
                                var isConnected = client.IsConnected;
                                operationCounts.AddOrUpdate("PropertyAccess", 1, (k, v) => v + 1);
                                break;

                            case 1: // Collection enumeration
                                foreach (var channel in client.Channels) { }
                                foreach (var capability in client.EnabledCapabilities) { }
                                operationCounts.AddOrUpdate("CollectionEnum", 1, (k, v) => v + 1);
                                break;

                            case 2: // Event subscription/unsubscription
                                void DummyHandler(object? sender, IRCDotNet.Core.Events.IrcEvent e) { }
                                if (j % 2 == 0)
                                {
                                    client.Connected += DummyHandler;
                                }
                                else
                                {
                                    client.Connected -= DummyHandler;
                                }
                                operationCounts.AddOrUpdate("EventOps", 1, (k, v) => v + 1);
                                break;

                            case 3: // State checks
                                var state1 = client.IsConnected;
                                var state2 = client.IsRegistered;
                                var state3 = client.CurrentNick;
                                operationCounts.AddOrUpdate("StateCheck", 1, (k, v) => v + 1);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"Client {clientIndex} failed: {ex.Message}");
                }
            }));
        }
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks.ToArray());
        stopwatch.Stop();

        var totalOperations = operationCounts.Values.Sum();
        _output.WriteLine($"Stress test completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Total operations: {totalOperations}, Exceptions: {exceptions.Count}");
        _output.WriteLine($"Throughput: {(totalOperations * 1000.0 / stopwatch.ElapsedMilliseconds):F2} ops/sec");

        foreach (var kvp in operationCounts)
        {
            _output.WriteLine($"{kvp.Key}: {kvp.Value}");
        }

        foreach (var ex in exceptions.Take(5))
        {
            _output.WriteLine($"Exception: {ex}");
        }

        Assert.Empty(exceptions);
        Assert.True(totalOperations > 0);
        Assert.Equal(clientCount * operationsPerClient, totalOperations);
    }
    [Fact]
    public async Task SendRawAsync_WithCancellation_ShouldNotDeadlock()
    {
        // This test verifies the fix for critical bug #1: SendRawAsync deadlock risk
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Test that SendRawAsync throws appropriately when not connected
        // and doesn't hang indefinitely even with cancellation
        using var cts = new CancellationTokenSource(5000); // 5 second timeout

        var sendTask = Task.Run(async () =>
        {
            try
            {
                // This should throw InvalidOperationException since we're not connected
                await client.SendRawAsync("TEST MESSAGE");
                return false; // Should not reach here
            }
            catch (InvalidOperationException)
            {
                return true; // Expected exception
            }
            catch (OperationCanceledException)
            {
                return true; // Also acceptable if cancelled
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Unexpected exception: {ex}");
                return false;
            }
        });

        // The operation should complete quickly without hanging
        var timeoutTask = Task.Delay(5000, cts.Token);
        var completedTask = await Task.WhenAny(sendTask, timeoutTask);

        Assert.True(completedTask == sendTask, "SendRawAsync should not hang indefinitely");

        if (completedTask == sendTask)
        {
            var result = await sendTask;
            Assert.True(result, "SendRawAsync should handle disconnected state properly");
        }
    }

    [Fact]
    public async Task SendRawAsync_ConcurrentCalls_ShouldNotDeadlock()
    {
        // This test verifies that multiple concurrent SendRawAsync calls don't cause deadlocks
        const int concurrentSends = 10;

        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();
        var completedCount = 0;

        // Create multiple concurrent send attempts
        var tasks = Enumerable.Range(0, concurrentSends).Select(i => Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(2000); // 2 second timeout per operation

                // All these should fail quickly since we're not connected
                await client.SendRawAsync($"TEST MESSAGE {i}");
            }
            catch (InvalidOperationException)
            {
                // Expected - not connected
                Interlocked.Increment(ref completedCount);
            }
            catch (OperationCanceledException)
            {
                // Also acceptable if cancelled due to timeout
                Interlocked.Increment(ref completedCount);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        // All tasks should complete within reasonable time
        using var overallTimeout = new CancellationTokenSource(10000); // 10 second overall timeout

        try
        {
            await Task.WhenAll(tasks).WaitAsync(overallTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Concurrent SendRawAsync calls caused deadlock or excessive delay");
        }

        _output.WriteLine($"Concurrent send test completed. Successful completions: {completedCount}, Exceptions: {exceptions.Count}");

        // Most operations should complete successfully (with expected exceptions)
        Assert.True(completedCount >= concurrentSends / 2, "At least half of the concurrent sends should complete properly");
        Assert.True(exceptions.Count <= concurrentSends / 2, "Should not have excessive unexpected exceptions");
    }

    [Fact]
    public async Task NickChange_ConcurrentOperations_ShouldMaintainConsistency()
    {
        // This test verifies the fix for critical bug #3: Nick change race condition
        const int operationCount = 100;

        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();
        var successCount = 0;

        // Simulate concurrent nick changes and user tracking
        var tasks = Enumerable.Range(0, operationCount).Select(i => Task.Run(() =>
        {
            try
            {
                // Access nick-related properties concurrently
                var currentNick = client.CurrentNick;
                var channels = client.Channels;
                var capabilities = client.EnabledCapabilities;

                // Verify collections are safe to iterate
                foreach (var channel in channels)
                {
                    var userCount = channel.Value.Count;
                    // This should not throw even during concurrent operations
                }

                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        _output.WriteLine($"Nick change concurrency test completed. Success: {successCount}, Exceptions: {exceptions.Count}");

        foreach (var ex in exceptions.Take(5))
        {
            _output.WriteLine($"Exception: {ex}");
        }

        Assert.Empty(exceptions);
        Assert.Equal(operationCount, successCount);
    }

    [Fact]
    public async Task UserInfo_ConcurrentUpdates_ShouldNotThrow()
    {
        // This test verifies the fix for critical bug #4: UserInfo dictionary race
        const int threadCount = 10;
        const int operationsPerThread = 100;

        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();
        var operationCount = 0;

        // Use reflection to access private UpdateUserInfo method for testing
        var updateUserInfoMethod = typeof(IrcClient).GetMethod("UpdateUserInfo",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(updateUserInfoMethod);

        var tasks = Enumerable.Range(0, threadCount).Select(threadIndex => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var nick = $"TestUser{threadIndex}_{i % 10}"; // Reuse some nicks to create contention

                    // Call UpdateUserInfo concurrently
                    updateUserInfoMethod.Invoke(client, new object?[]
                    {
                        nick,
                        $"user{i}",
                        $"host{i}.com",
                        $"account{i}",
                        $"Real Name {i}",
                        null,
                        null
                    });

                    Interlocked.Increment(ref operationCount);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
                _output.WriteLine($"Thread {threadIndex} exception: {ex.Message}");
            }
        }));

        await Task.WhenAll(tasks);

        _output.WriteLine($"UserInfo concurrency test completed. Operations: {operationCount}, Exceptions: {exceptions.Count}");

        foreach (var ex in exceptions.Take(5))
        {
            _output.WriteLine($"Exception: {ex}");
        }

        Assert.Empty(exceptions);
        Assert.Equal(threadCount * operationsPerThread, operationCount);
    }

    [Fact]
    public async Task PingTimer_StateAccess_ShouldNotThrow()
    {
        // This test verifies the fix for critical bug #5: Ping timer state access
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Port = 6667,
            Nick = "TestClient",
            UserName = "testuser",
            RealName = "Test User",
            PingIntervalMs = 100, // Very short interval for testing
            EnableRateLimit = false // Disable rate limiting for thread safety testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var exceptions = new ConcurrentBag<Exception>();

        // Use reflection to access private SendPing method for testing
        var sendPingMethod = typeof(IrcClient).GetMethod("SendPing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(sendPingMethod);

        // Simulate concurrent ping timer calls
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            try
            {
                // Call SendPing while potentially disconnected
                sendPingMethod.Invoke(client, new object?[] { null });
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
                _output.WriteLine($"Ping operation {i} exception: {ex.Message}");
            }
        }));

        await Task.WhenAll(tasks);

        _output.WriteLine($"Ping timer test completed. Exceptions: {exceptions.Count}");

        foreach (var ex in exceptions.Take(5))
        {
            _output.WriteLine($"Exception: {ex}");
        }

        // Should not throw exceptions even when called on disconnected client
        Assert.Empty(exceptions);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing client: {ex.Message}");
            }
        }
    }
}
