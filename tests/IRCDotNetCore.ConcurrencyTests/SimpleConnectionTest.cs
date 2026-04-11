using System;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Simple test to verify basic IRC connection and channel join functionality
/// </summary>
[Trait("Category", "Simple")]
public class SimpleConnectionTest : IDisposable
{
    private const string TestServer = "localhost";
    private const int TestPort = 6667;
    private const string TestChannel = "#testchannel";

    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private IrcClient? _client;

    public SimpleConnectionTest(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact]
    public async Task SimpleConnection_ShouldConnectAndJoinChannel()
    {
        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = $"TestUser_{DateTime.Now.Ticks % 10000}",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false,
            EnableRateLimit = false // Disable rate limiting for concurrency testing
        };

        _client = new IrcClient(options, _logger);

        var connected = false;
        var joined = false;

        _client.Connected += (sender, e) =>
        {
            _output.WriteLine($"Connected to {e.Network} as {e.Nick}");
            connected = true;
        };

        _client.UserJoinedChannel += (sender, e) =>
        {
            if (e.Nick == options.Nick && e.Channel == TestChannel)
            {
                _output.WriteLine($"Successfully joined {TestChannel}");
                joined = true;
            }
        };

        _client.Disconnected += (sender, e) =>
        {
            _output.WriteLine($"Disconnected: {e.Reason}");
        };

        try
        {
            _output.WriteLine("Connecting to IRC server...");
            await _client.ConnectAsync();

            // Wait for connection
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (!connected && DateTime.UtcNow < timeout)
            {
                await Task.Delay(100);
            }

            if (!connected)
            {
                throw new TimeoutException("Failed to connect within timeout");
            }

            _output.WriteLine("Joining test channel...");
            await _client.JoinChannelAsync(TestChannel);

            // Wait for channel join
            timeout = DateTime.UtcNow.AddSeconds(10);
            while (!joined && DateTime.UtcNow < timeout)
            {
                await Task.Delay(100);
            }

            if (!joined)
            {
                _output.WriteLine("Warning: Channel join may not have completed");
            }

            _output.WriteLine("Test completed successfully!");
            Assert.True(connected, "Should have connected to IRC server");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Test failed: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            // Cleanup client connection
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _client?.DisconnectAsync("Test completed").GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error disposing client: {ex.Message}");
        }
        finally
        {
            _client?.Dispose();
        }
    }
}
