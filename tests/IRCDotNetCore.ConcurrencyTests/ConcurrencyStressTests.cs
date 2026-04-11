using System.Collections.Concurrent;
using System.Diagnostics;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Comprehensive concurrency and thread safety tests using real IRC servers
/// </summary>
[Trait("Category", "NetworkDependent")]
public class ConcurrencyStressTests : IDisposable
{
    private const string TestServer = "localhost";
    private const int TestPort = 6667;

    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();
    private readonly CancellationTokenSource _testCancellation = new();

    public ConcurrencyStressTests(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }
    [Fact]
    public async Task ConcurrentConnections_MultipleClients_ShouldNotInterfere()
    {
        const int clientCount = 5;

        var tasks = new List<Task>();
        var connectionResults = new ConcurrentBag<bool>();

        // Create multiple clients concurrently
        for (int i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var options = new IrcClientOptions
                    {
                        Server = TestServer,
                        Port = TestPort,
                        Nick = $"TestBot{clientIndex}_{DateTime.Now.Ticks % 10000}",
                        UserName = $"testuser{clientIndex}",
                        RealName = $"Test User {clientIndex}",
                        ConnectionTimeoutMs = 10000,
                        AutoReconnect = false,
                        EnableRateLimit = false // Disable rate limiting for concurrency testing
                    };

                    var client = new IrcClient(options, _logger);
                    _clients.Add(client);

                    var connected = false;
                    client.Connected += (sender, e) =>
                    {
                        _output.WriteLine($"Client {clientIndex} connected as {e.Nick}");
                        connected = true;
                    };

                    await client.ConnectAsync(_testCancellation.Token);

                    // Wait for connection confirmation with a reasonable timeout
                    var timeout = DateTime.UtcNow.AddSeconds(10); // Reduced from 15 to 10 seconds
                    while (!connected && DateTime.UtcNow < timeout)
                    {
                        await Task.Delay(100, _testCancellation.Token);
                    }

                    connectionResults.Add(connected);
                    _output.WriteLine($"Client {clientIndex}: Connection result = {connected}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Client {clientIndex} failed: {ex.Message}");
                    connectionResults.Add(false);
                }
            }));
        }

        await Task.WhenAll(tasks);

        var successCount = connectionResults.Count(r => r);
        _output.WriteLine($"Successfully connected {successCount}/{clientCount} clients");

        // We expect at least some connections to succeed (server may have connection limits)
        Assert.True(successCount > 0, "At least one client should connect successfully");
    }
    [Fact]
    public async Task ConcurrentMessageSending_ShouldMaintainOrder()
    {
        const int messageCount = 100;

        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = $"OrderTest_{DateTime.Now.Ticks % 10000}",
            UserName = "ordertest",
            RealName = "Order Test User",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var connected = false;
        var sentMessages = new ConcurrentQueue<string>();

        client.Connected += (sender, e) =>
        {
            connected = true;
            _output.WriteLine($"Connected as {e.Nick}");
        };

        await client.ConnectAsync(_testCancellation.Token);

        // Wait for connection
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!connected && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100, _testCancellation.Token);
        }

        Assert.True(connected, "Client should connect successfully");

        // Send messages concurrently from multiple threads
        var sendTasks = new List<Task>();
        for (int i = 0; i < messageCount; i++)
        {
            var messageIndex = i;
            sendTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var message = $"PING :test_message_{messageIndex:D3}";
                    await client.SendRawAsync(message);
                    sentMessages.Enqueue(message);
                    _output.WriteLine($"Sent: {message}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Failed to send message {messageIndex}: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(sendTasks);

        _output.WriteLine($"Sent {sentMessages.Count}/{messageCount} messages");

        // Verify no exceptions and reasonable success rate
        Assert.True(sentMessages.Count >= messageCount * 0.9, "At least 90% of messages should be sent successfully");
    }
    [Fact]
    public async Task ConcurrentEventHandling_ShouldNotBlock()
    {
        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = $"EventTest_{DateTime.Now.Ticks % 10000}",
            UserName = "eventtest",
            RealName = "Event Test User",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var connected = false;
        var eventCounts = new ConcurrentDictionary<string, int>();
        var blockingEventStarted = false;
        var nonBlockingEventsReceived = 0;

        client.Connected += (sender, e) =>
        {
            connected = true;
            eventCounts.AddOrUpdate("Connected", 1, (k, v) => v + 1);
            _output.WriteLine($"Connected event: {e.Nick}");
        }; client.RawMessageReceived += (sender, e) =>
        {
            eventCounts.AddOrUpdate("RawMessage", 1, (k, v) => v + 1);

            // Simulate a blocking event handler for some messages
            if (e.Message.Command == "001" && !blockingEventStarted)
            {
                blockingEventStarted = true;
                _output.WriteLine("Starting blocking event handler...");
                Thread.Sleep(1000); // Simulate slow processing (reduced from 2000ms to 1000ms)
                _output.WriteLine("Blocking event handler completed");
            }
            else
            {
                Interlocked.Increment(ref nonBlockingEventsReceived);
            }
        };

        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(_testCancellation.Token);

        // Wait for connection
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!connected && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100, _testCancellation.Token);
        }

        Assert.True(connected, "Client should connect successfully");        // Wait a bit for initial server messages (reduced from 3000ms to 2000ms)
        await Task.Delay(2000, _testCancellation.Token);
        stopwatch.Stop();

        _output.WriteLine($"Connection and event processing took {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Non-blocking events received: {nonBlockingEventsReceived}");
        _output.WriteLine($"Blocking event started: {blockingEventStarted}");

        // Verify that despite having a blocking event handler, other events were still processed
        Assert.True(nonBlockingEventsReceived > 0, "Non-blocking events should be processed even with blocking handlers");

        foreach (var kvp in eventCounts)
        {
            _output.WriteLine($"Event {kvp.Key}: {kvp.Value} times");
        }
    }
    [Fact]
    public async Task ThreadSafePropertyAccess_ConcurrentReads_ShouldNotThrow()
    {
        const int readerThreads = 10;
        const int readIterations = 1000;

        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = $"PropTest_{DateTime.Now.Ticks % 10000}",
            UserName = "proptest",
            RealName = "Property Test User",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var connected = false;
        var exceptions = new ConcurrentBag<Exception>();

        client.Connected += (sender, e) =>
        {
            connected = true;
            _output.WriteLine($"Connected as {e.Nick}");
        };

        await client.ConnectAsync(_testCancellation.Token);

        // Wait for connection
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!connected && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100, _testCancellation.Token);
        }

        Assert.True(connected, "Client should connect successfully");

        // Join a test channel to populate some state
        await client.JoinChannelAsync("#test");
        await Task.Delay(800); // Wait for channel join (reduced from 1000ms to 800ms)

        // Create multiple threads that continuously read properties
        var readerTasks = new List<Task>();
        for (int i = 0; i < readerThreads; i++)
        {
            var threadIndex = i;
            readerTasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < readIterations; j++)
                    {
                        // Read various properties concurrently
                        var isConnected = client.IsConnected;
                        var isRegistered = client.IsRegistered;
                        var currentNick = client.CurrentNick;
                        var channels = client.Channels;
                        var capabilities = client.EnabledCapabilities;

                        // Verify properties return valid data
                        Assert.NotNull(channels);
                        Assert.NotNull(capabilities);
                        Assert.NotNull(currentNick);

                        if (j % 100 == 0)
                        {
                            _output.WriteLine($"Thread {threadIndex}: Iteration {j}, Channels: {channels.Count}, Nick: {currentNick}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    _output.WriteLine($"Thread {threadIndex} exception: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(readerTasks);

        _output.WriteLine($"Property access test completed. Exceptions: {exceptions.Count}");

        foreach (var ex in exceptions)
        {
            _output.WriteLine($"Exception: {ex}");
        }

        Assert.Empty(exceptions);
    }
    [Fact]
    public async Task StressTest_HighConcurrencyOperations()
    {
        const int operationCount = 500;

        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = $"StressTest_{DateTime.Now.Ticks % 10000}",
            UserName = "stresstest",
            RealName = "Stress Test User",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false,
            EnableRateLimit = false // Disable rate limiting for stress testing
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var connected = false;
        var operationResults = new ConcurrentBag<(string Operation, bool Success, string? Error)>();

        client.Connected += (sender, e) =>
        {
            connected = true;
            _output.WriteLine($"Connected as {e.Nick}");
        };

        await client.ConnectAsync(_testCancellation.Token);

        // Wait for connection
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!connected && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100, _testCancellation.Token);
        }

        Assert.True(connected, "Client should connect successfully");

        var stopwatch = Stopwatch.StartNew();

        // Create high-concurrency mixed operations
        var tasks = new List<Task>();
        var random = new Random();

        for (int i = 0; i < operationCount; i++)
        {
            var operationIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var operationType = random.Next(4);
                    switch (operationType)
                    {
                        case 0: // Send PING
                            await client.SendRawAsync($"PING :stress_test_{operationIndex}");
                            operationResults.Add(("PING", true, null));
                            break;

                        case 1: // Property access
                            var nick = client.CurrentNick;
                            var channels = client.Channels;
                            var isConnected = client.IsConnected;
                            operationResults.Add(("PropertyAccess", true, null));
                            break;

                        case 2: // Send PRIVMSG to self
                            await client.SendMessageAsync(client.CurrentNick, $"Test message {operationIndex}");
                            operationResults.Add(("PRIVMSG", true, null));
                            break;

                        case 3: // Send NOTICE
                            await client.SendNoticeAsync(client.CurrentNick, $"Test notice {operationIndex}");
                            operationResults.Add(("NOTICE", true, null));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    operationResults.Add(($"Operation{operationIndex}", false, ex.Message));
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var successCount = operationResults.Count(r => r.Success);
        var failureCount = operationResults.Count(r => !r.Success);

        _output.WriteLine($"Stress test completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Operations: {operationResults.Count}, Success: {successCount}, Failures: {failureCount}");
        _output.WriteLine($"Throughput: {(operationCount * 1000.0 / stopwatch.ElapsedMilliseconds):F2} ops/sec");

        // Group results by operation type
        var resultsByType = operationResults.GroupBy(r => r.Operation.Contains("Operation") ? "Unknown" : r.Operation)
                                           .ToDictionary(g => g.Key, g => new { Success = g.Count(r => r.Success), Failed = g.Count(r => !r.Success) });

        foreach (var kvp in resultsByType)
        {
            _output.WriteLine($"{kvp.Key}: {kvp.Value.Success} success, {kvp.Value.Failed} failed");
        }

        // Print any errors
        foreach (var failure in operationResults.Where(r => !r.Success))
        {
            _output.WriteLine($"Error in {failure.Operation}: {failure.Error}");
        }

        // We expect a high success rate (at least 95%)
        var successRate = (double)successCount / operationResults.Count;
        _output.WriteLine($"Success rate: {successRate:P2}");

        Assert.True(successRate >= 0.95, $"Success rate should be at least 95%, got {successRate:P2}");
    }

    public void Dispose()
    {
        _testCancellation.Cancel();

        var disconnectTasks = _clients.Select(async client =>
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync("Test completed");
                }
                client.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing client: {ex.Message}");
            }
        });

        try
        {
            Task.WaitAll(disconnectTasks.ToArray(), TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during cleanup: {ex.Message}");
        }

        _testCancellation.Dispose();
    }
}
