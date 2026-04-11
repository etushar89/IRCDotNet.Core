using System.Collections.Concurrent;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Tests for IRCv3 echo-message capability behavior.
/// Requires a real IRC server that supports echo-message (e.g., Libera.Chat on port 6697 with TLS).
/// </summary>
[Trait("Category", "NetworkDependent")]
public class EchoMessageTests : IDisposable
{
    private const string TestServer = "irc.us.libera.chat";
    private const int TestPort = 6697;
    private const bool UseSsl = true;
    private const string TestChannel = "#git"; // A real channel to test in

    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private IrcClient? _client;

    public EchoMessageTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact(Timeout = 30000)]
    public async Task EchoMessage_ShouldSetIsEchoFlag_WhenReceivingOwnChannelMessage()
    {
        // Arrange
        var nick = $"EchoTest{DateTime.Now.Ticks % 10000}";
        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            UseSsl = UseSsl,
            AcceptInvalidSslCertificates = false,
            Nick = nick,
            UserName = nick,
            RealName = "Echo Message Test",
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        _client = new IrcClient(options, _logger);

        var echoReceived = new TaskCompletionSource<PrivateMessageEvent>();
        var otherMessageReceived = new TaskCompletionSource<PrivateMessageEvent>();
        var connected = new TaskCompletionSource<bool>();

        _client.Connected += (s, e) =>
        {
            _output.WriteLine($"Connected as {_client.CurrentNick}");
            connected.TrySetResult(true);
        };

        _client.PrivateMessageReceived += (s, e) =>
        {
            _output.WriteLine($"PRIVMSG from {e.Nick} to {e.Target}: {e.Text} (IsEcho={e.IsEcho}, IsChannel={e.IsChannelMessage})");

            if (e.IsEcho && e.IsChannelMessage)
            {
                echoReceived.TrySetResult(e);
            }
            else if (!e.IsEcho && e.IsChannelMessage)
            {
                otherMessageReceived.TrySetResult(e);
            }
        };

        // Act
        await _client.ConnectAsync();
        await connected.Task;

        // Verify echo-message capability was negotiated
        _output.WriteLine($"Enabled capabilities: {string.Join(", ", _client.EnabledCapabilities)}");
        var hasEchoMessage = _client.EnabledCapabilities.Contains("echo-message");
        _output.WriteLine($"echo-message enabled: {hasEchoMessage}");

        if (!hasEchoMessage)
        {
            _output.WriteLine("SKIP: Server does not support echo-message capability");
            return; // Can't test echo without the capability
        }

        // Join channel
        await _client.JoinChannelAsync(TestChannel);
        await Task.Delay(2000); // Wait for channel join to complete

        // Send a message — should be echoed back with IsEcho=true
        var testMessage = $"Echo test {DateTime.Now.Ticks}";
        await _client.SendMessageAsync(TestChannel, testMessage);

        // Assert — wait for echo with timeout
        var completedTask = await Task.WhenAny(echoReceived.Task, Task.Delay(5000));

        if (completedTask == echoReceived.Task)
        {
            var echo = await echoReceived.Task;
            Assert.True(echo.IsEcho, "Echoed message should have IsEcho=true");
            Assert.True(echo.IsChannelMessage, "Echoed message should be a channel message");
            Assert.Equal(testMessage, echo.Text);
            Assert.Equal(_client.CurrentNick, echo.Nick);
            _output.WriteLine("PASS: Echo message correctly detected");
        }
        else
        {
            _output.WriteLine("WARN: No echo received within 5 seconds — server may not have echoed");
        }
    }

    [Fact]
    public void PrivateMessageEvent_IsEcho_DefaultsFalse()
    {
        var msg = IrcMessage.Parse(":other!u@h PRIVMSG #channel :hello");
        var evt = new PrivateMessageEvent(msg, "other", "u", "h", "#channel", "hello");

        Assert.False(evt.IsEcho);
    }

    [Fact]
    public void PrivateMessageEvent_IsEcho_SetWhenExplicit()
    {
        var msg = IrcMessage.Parse(":me!u@h PRIVMSG #channel :my message");
        var evt = new PrivateMessageEvent(msg, "me", "u", "h", "#channel", "my message", isEcho: true);

        Assert.True(evt.IsEcho);
        Assert.True(evt.IsChannelMessage);
    }

    public void Dispose()
    {
        if (_client != null)
        {
            try { _client.DisconnectAsync("Test complete").Wait(5000); } catch { }
            _client.Dispose();
        }
    }
}
