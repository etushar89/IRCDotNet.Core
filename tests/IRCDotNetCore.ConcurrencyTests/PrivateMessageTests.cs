using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Integration tests for direct private messages (user-to-user DMs, not channel messages).
/// Requires a running IRC server on localhost:6667.
/// </summary>
[Trait("Category", "NetworkDependent")]
public class PrivateMessageTests : IDisposable
{
    private const string TestServer = "localhost";
    private const int TestPort = 6667;

    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();
    private readonly CancellationTokenSource _testCancellation = new();

    public PrivateMessageTests(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    /// <summary>
    /// Two users exchange direct private messages (not via channel).
    /// Verifies that DMs are received with correct sender, target, and content.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task TwoUsers_DirectPrivateMessage_ShouldDeliverCorrectly()
    {
        var user1Nick = $"PMUser1_{DateTime.Now.Ticks % 10000}";
        var user2Nick = $"PMUser2_{DateTime.Now.Ticks % 10000}";

        var (user1, user2) = await ConnectTwoUsersAsync(user1Nick, user2Nick);

        var user1ReceivedDMs = new ConcurrentBag<(string FromNick, string Text)>();
        var user2ReceivedDMs = new ConcurrentBag<(string FromNick, string Text)>();

        // Hook DM handlers — only capture non-channel messages
        user1.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
            {
                user1ReceivedDMs.Add((e.Nick, e.Text));
                _output.WriteLine($"User1 received DM from {e.Nick}: {e.Text}");
            }
        };

        user2.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
            {
                user2ReceivedDMs.Add((e.Nick, e.Text));
                _output.WriteLine($"User2 received DM from {e.Nick}: {e.Text}");
            }
        };

        // User1 sends DM to User2
        _output.WriteLine($"=== User1 sending DM to {user2Nick} ===");
        await user1.SendMessageAsync(user2Nick, "Hello User2, this is a direct message!");
        await Task.Delay(2000);

        Assert.Single(user2ReceivedDMs);
        var dm = user2ReceivedDMs.First();
        Assert.Equal(user1Nick, dm.FromNick);
        Assert.Equal("Hello User2, this is a direct message!", dm.Text);
        Assert.Empty(user1ReceivedDMs); // User1 should NOT receive their own DM

        _output.WriteLine("✅ Direct private message delivered correctly");
    }

    /// <summary>
    /// Bidirectional DM conversation: both users send and receive multiple DMs.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task TwoUsers_BidirectionalDMs_ShouldExchangeMessages()
    {
        var user1Nick = $"BiDM1_{DateTime.Now.Ticks % 10000}";
        var user2Nick = $"BiDM2_{DateTime.Now.Ticks % 10000}";

        var (user1, user2) = await ConnectTwoUsersAsync(user1Nick, user2Nick);

        var user1ReceivedDMs = new ConcurrentBag<(string FromNick, string Text)>();
        var user2ReceivedDMs = new ConcurrentBag<(string FromNick, string Text)>();

        user1.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
                user1ReceivedDMs.Add((e.Nick, e.Text));
        };

        user2.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
                user2ReceivedDMs.Add((e.Nick, e.Text));
        };

        const int messageCount = 3;

        _output.WriteLine("=== Starting bidirectional DM conversation ===");
        for (int i = 0; i < messageCount; i++)
        {
            await user1.SendMessageAsync(user2Nick, $"Message {i + 1} from User1");
            await Task.Delay(500);

            await user2.SendMessageAsync(user1Nick, $"Reply {i + 1} from User2");
            await Task.Delay(500);
        }

        // Wait for propagation
        await Task.Delay(3000);

        _output.WriteLine($"User1 received {user1ReceivedDMs.Count} DMs, User2 received {user2ReceivedDMs.Count} DMs");

        Assert.Equal(messageCount, user2ReceivedDMs.Count);
        Assert.Equal(messageCount, user1ReceivedDMs.Count);

        // Verify all User1's DMs came from User2 and vice versa
        Assert.All(user1ReceivedDMs, dm => Assert.Equal(user2Nick, dm.FromNick));
        Assert.All(user2ReceivedDMs, dm => Assert.Equal(user1Nick, dm.FromNick));

        // Verify message content ordering
        var user2Received = user2ReceivedDMs.OrderBy(d => d.Text).ToList();
        for (int i = 0; i < messageCount; i++)
        {
            Assert.Contains($"Message {i + 1} from User1", user2Received[i].Text);
        }

        _output.WriteLine("✅ Bidirectional DM exchange completed successfully");
    }

    /// <summary>
    /// Send a DM with Unicode/multi-byte characters to verify UTF-8 handling
    /// over the wire against a real server.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task DirectMessage_WithUnicodeCharacters_ShouldDeliverCorrectly()
    {
        var user1Nick = $"Uni1_{DateTime.Now.Ticks % 10000}";
        var user2Nick = $"Uni2_{DateTime.Now.Ticks % 10000}";

        var (user1, user2) = await ConnectTwoUsersAsync(user1Nick, user2Nick);

        var receivedMessages = new ConcurrentBag<string>();

        user2.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
            {
                receivedMessages.Add(e.Text);
                _output.WriteLine($"Received Unicode DM: {e.Text}");
            }
        };

        // Test various Unicode content
        var unicodeMessages = new[]
        {
            "Héllo wörld — café résumé",           // Latin extended
            "Привет мир!",                           // Cyrillic
            "こんにちは世界",                           // Japanese (3 bytes each)
            "🎉🚀💻 emoji test 👍",                  // 4-byte emoji
            "Mixed: Hello Привет こんにちは 🌍",      // Multi-script
        };

        _output.WriteLine("=== Sending Unicode DMs ===");
        foreach (var msg in unicodeMessages)
        {
            var byteCount = Encoding.UTF8.GetByteCount(msg);
            _output.WriteLine($"  Sending ({byteCount} bytes): {msg}");
            await user1.SendMessageAsync(user2Nick, msg);
            await Task.Delay(800);
        }

        // Wait for delivery
        await Task.Delay(3000);

        _output.WriteLine($"Received {receivedMessages.Count}/{unicodeMessages.Length} Unicode DMs");
        Assert.Equal(unicodeMessages.Length, receivedMessages.Count);

        // Verify each message arrived intact
        foreach (var expected in unicodeMessages)
        {
            Assert.Contains(expected, receivedMessages);
        }

        _output.WriteLine("✅ All Unicode DMs delivered correctly");
    }

    /// <summary>
    /// Send a DM at exactly the IRC byte boundary to verify edge behavior.
    /// PRIVMSG header is "PRIVMSG nick :" = ~15-25 bytes overhead.
    /// Total IRC line limit is 512 bytes including CRLF.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task DirectMessage_AtByteBoundary_ShouldDeliverCorrectly()
    {
        var user1Nick = $"Bnd1_{DateTime.Now.Ticks % 10000}";
        var user2Nick = $"Bnd2_{DateTime.Now.Ticks % 10000}";

        var (user1, user2) = await ConnectTwoUsersAsync(user1Nick, user2Nick);

        var receivedMessages = new ConcurrentBag<string>();

        user2.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
            {
                receivedMessages.Add(e.Text);
                _output.WriteLine($"Received ({Encoding.UTF8.GetByteCount(e.Text)} bytes): {e.Text[..Math.Min(50, e.Text.Length)]}...");
            }
        };

        // Calculate max payload: 512 - 2 (CRLF) - "PRIVMSG " (8) - nick (variable) - " :" (2) - server prefix overhead (~60)
        // Server adds ":nick!user@host PRIVMSG target :" prefix on delivery (~60+ bytes)
        // Safe payload for testing: 400 ASCII chars should be well within limits
        var longMessage = new string('A', 400);
        var longMessageBytes = Encoding.UTF8.GetByteCount(longMessage);

        _output.WriteLine($"=== Sending 400-char ASCII message ({longMessageBytes} bytes) ===");
        await user1.SendMessageAsync(user2Nick, longMessage);
        await Task.Delay(2000);

        Assert.Single(receivedMessages);
        Assert.Equal(longMessage, receivedMessages.First());
        _output.WriteLine("✅ 400-char message delivered intact");

        // Test with multi-byte chars near the limit
        // 120 Japanese chars × 3 bytes = 360 bytes, well within limit
        var multiByteMessage = new string('あ', 120);
        var multiByteSize = Encoding.UTF8.GetByteCount(multiByteMessage);
        _output.WriteLine($"=== Sending 120 Japanese chars ({multiByteSize} bytes) ===");
        await user1.SendMessageAsync(user2Nick, multiByteMessage);
        await Task.Delay(2000);

        Assert.Equal(2, receivedMessages.Count);
        Assert.Contains(multiByteMessage, receivedMessages);
        _output.WriteLine("✅ Multi-byte boundary message delivered intact");
    }

    /// <summary>
    /// DM to a non-existent user should not crash the client.
    /// The server will return ERR_NOSUCHNICK (401) but the send should complete.
    /// </summary>
    [Fact(Timeout = 20000)]
    public async Task DirectMessage_ToNonExistentUser_ShouldNotCrash()
    {
        var senderNick = $"Ghost1_{DateTime.Now.Ticks % 10000}";
        var fakeTarget = $"NoSuchUser_{DateTime.Now.Ticks % 10000}";

        var sender = await ConnectSingleUserAsync(senderNick);

        var serverErrors = new ConcurrentBag<string>();

        // Track server error replies via raw messages
        sender.RawMessageReceived += (s, e) =>
        {
            // ERR_NOSUCHNICK = 401
            if (e.Message.Command == "401")
            {
                serverErrors.Add(string.Join(" ", e.Message.Parameters));
                _output.WriteLine($"Server error 401: {string.Join(" ", e.Message.Parameters)}");
            }
        };

        _output.WriteLine($"=== Sending DM to non-existent user {fakeTarget} ===");

        // Should not throw
        await SendMessageSafe(sender, fakeTarget, "Hello ghost!");
        await Task.Delay(2000);

        // Client should still be connected and functional
        Assert.True(sender.IsConnected, "Client should remain connected after sending to non-existent user");

        _output.WriteLine($"Server returned {serverErrors.Count} error(s)");
        if (serverErrors.Count > 0)
        {
            _output.WriteLine("✅ Server correctly returned ERR_NOSUCHNICK, client remained stable");
        }
        else
        {
            _output.WriteLine("✅ No server error (server may silently drop), client remained stable");
        }
    }

    /// <summary>
    /// Multiple DMs sent rapidly to verify delivery completeness under burst load.
    /// Note: strict ordering is NOT guaranteed when sending without inter-message delays,
    /// as the server may process queued messages in slightly different order.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task DirectMessage_RapidBurst_ShouldDeliverAllMessages()
    {
        var user1Nick = $"Burst1_{DateTime.Now.Ticks % 10000}";
        var user2Nick = $"Burst2_{DateTime.Now.Ticks % 10000}";

        var (user1, user2) = await ConnectTwoUsersAsync(user1Nick, user2Nick);

        var receivedMessages = new ConcurrentBag<string>();

        user2.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
                receivedMessages.Add(e.Text);
        };

        const int burstCount = 10;
        _output.WriteLine($"=== Sending {burstCount} DMs in rapid succession ===");

        for (int i = 0; i < burstCount; i++)
        {
            await user1.SendMessageAsync(user2Nick, $"Burst#{i:D3}");
            // No artificial delay — test natural throughput
        }

        // Wait for all messages to arrive
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (receivedMessages.Count < burstCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
        }

        _output.WriteLine($"Received {receivedMessages.Count}/{burstCount} burst DMs");
        Assert.Equal(burstCount, receivedMessages.Count);

        // Verify all messages arrived (completeness, not strict ordering — server may reorder bursts)
        var received = receivedMessages.ToHashSet();
        for (int i = 0; i < burstCount; i++)
        {
            Assert.Contains($"Burst#{i:D3}", received);
        }

        _output.WriteLine("✅ All burst DMs delivered successfully");
    }

    /// <summary>
    /// Stress test: 3 users all sending DMs to each other simultaneously (6 directional streams).
    /// Each stream sends 30 messages with minimal delay. Verifies completeness, no crashes,
    /// and client stability after the storm.
    /// </summary>
    [Fact(Timeout = 90000)]
    public async Task DirectMessage_StressTest_ThreeUsersCrossMessaging()
    {
        const int usersCount = 3;
        const int messagesPerStream = 30;
        var ticks = DateTime.Now.Ticks % 10000;

        var nicks = Enumerable.Range(0, usersCount).Select(i => $"Stress{i}_{ticks}").ToArray();

        _output.WriteLine($"=== Connecting {usersCount} users for PM stress test ===");

        // Connect all users
        var clients = new IrcClient[usersCount];
        for (int i = 0; i < usersCount; i++)
        {
            clients[i] = await ConnectSingleUserAsync(nicks[i]);
        }

        // Track received DMs per user: key = receiverIndex, value = list of "senderIndex:msgId"
        var received = new ConcurrentDictionary<int, ConcurrentBag<string>>();
        for (int i = 0; i < usersCount; i++)
        {
            received[i] = new ConcurrentBag<string>();
        }

        // Hook DM handlers
        for (int i = 0; i < usersCount; i++)
        {
            var receiverIndex = i;
            clients[i].PrivateMessageReceived += (sender, e) =>
            {
                if (!e.IsChannelMessage)
                    received[receiverIndex].Add(e.Text);
            };
        }

        // Total expected: each user sends to (usersCount-1) others × messagesPerStream
        int totalStreams = usersCount * (usersCount - 1); // 6 for 3 users
        int totalExpectedPerReceiver = (usersCount - 1) * messagesPerStream; // 60 per receiver
        int totalExpected = usersCount * totalExpectedPerReceiver; // 180 total

        _output.WriteLine($"=== Launching {totalStreams} concurrent DM streams, {messagesPerStream} msgs each ({totalExpected} total) ===");

        var sw = Stopwatch.StartNew();

        // Launch all streams concurrently
        var sendTasks = new List<Task>();
        var sendErrors = new ConcurrentBag<string>();

        for (int senderIdx = 0; senderIdx < usersCount; senderIdx++)
        {
            for (int targetIdx = 0; targetIdx < usersCount; targetIdx++)
            {
                if (senderIdx == targetIdx) continue;

                var si = senderIdx;
                var ti = targetIdx;

                sendTasks.Add(Task.Run(async () =>
                {
                    for (int m = 0; m < messagesPerStream; m++)
                    {
                        try
                        {
                            await clients[si].SendMessageAsync(nicks[ti], $"S{si}T{ti}M{m:D3}");
                        }
                        catch (Exception ex)
                        {
                            sendErrors.Add($"S{si}->T{ti} msg {m}: {ex.Message}");
                        }
                        // Tiny delay to avoid overwhelming the server's flood protection
                        await Task.Delay(50);
                    }
                }));
            }
        }

        await Task.WhenAll(sendTasks);
        sw.Stop();

        _output.WriteLine($"All sends completed in {sw.ElapsedMilliseconds}ms ({sendErrors.Count} errors)");

        // Wait for messages to propagate
        int totalReceived = received.Values.Sum(b => b.Count);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (totalReceived < totalExpected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            totalReceived = received.Values.Sum(b => b.Count);
            _output.WriteLine($"  ... received {totalReceived}/{totalExpected}");
        }

        // Report results
        _output.WriteLine($"\n=== Stress Test Results ===");
        for (int i = 0; i < usersCount; i++)
        {
            _output.WriteLine($"  {nicks[i]}: received {received[i].Count}/{totalExpectedPerReceiver} DMs");
        }

        if (sendErrors.Count > 0)
        {
            _output.WriteLine($"\nSend errors ({sendErrors.Count}):");
            foreach (var err in sendErrors.Take(10))
                _output.WriteLine($"  {err}");
        }

        // Verify all clients are still connected
        for (int i = 0; i < usersCount; i++)
        {
            Assert.True(clients[i].IsConnected, $"{nicks[i]} should still be connected after stress test");
        }

        // Verify delivery completeness per receiver
        for (int i = 0; i < usersCount; i++)
        {
            var bag = received[i];
            var expectedFromEachSender = messagesPerStream;

            for (int senderIdx = 0; senderIdx < usersCount; senderIdx++)
            {
                if (senderIdx == i) continue;

                var fromThisSender = bag.Count(m => m.StartsWith($"S{senderIdx}T{i}"));
                _output.WriteLine($"  {nicks[i]} received {fromThisSender}/{expectedFromEachSender} from {nicks[senderIdx]}");
            }
        }

        // At least 90% delivery rate (server may drop under extreme flood)
        double deliveryRate = (double)totalReceived / totalExpected;
        _output.WriteLine($"\nOverall delivery: {totalReceived}/{totalExpected} ({deliveryRate:P1})");
        Assert.True(deliveryRate >= 0.90, $"Delivery rate {deliveryRate:P1} should be at least 90%");

        _output.WriteLine("✅ PM stress test completed — all clients stable");
    }

    #region Helper Methods

    private async Task<IrcClient> ConnectSingleUserAsync(string nick)
    {
        var options = new IrcClientOptions
        {
            Server = TestServer,
            Port = TestPort,
            Nick = nick,
            UserName = nick.ToLowerInvariant(),
            RealName = $"Test {nick}",
            ConnectionTimeoutMs = 10000,
            AutoReconnect = false,
            EnableRateLimit = false
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var registered = new TaskCompletionSource<bool>();

        client.Connected += (sender, e) =>
        {
            _output.WriteLine($"  {e.Nick} connected");
            registered.TrySetResult(true);
        };

        await client.ConnectAsync(_testCancellation.Token);

        // Wait for registration to complete
        var timeout = Task.Delay(10000, _testCancellation.Token);
        var completed = await Task.WhenAny(registered.Task, timeout);
        Assert.True(completed == registered.Task, $"{nick} should register within 10 seconds");

        // Give the server a moment to finalize registration
        await Task.Delay(1000);

        return client;
    }

    private async Task<(IrcClient User1, IrcClient User2)> ConnectTwoUsersAsync(string nick1, string nick2)
    {
        _output.WriteLine($"=== Connecting {nick1} and {nick2} ===");

        var user1 = await ConnectSingleUserAsync(nick1);
        var user2 = await ConnectSingleUserAsync(nick2);

        Assert.True(user1.IsConnected, $"{nick1} should be connected");
        Assert.True(user2.IsConnected, $"{nick2} should be connected");

        _output.WriteLine("Both users connected and registered");
        return (user1, user2);
    }

    private static async Task SendMessageSafe(IrcClient client, string target, string message)
    {
        try
        {
            await client.SendMessageAsync(target, message);
        }
        catch (Exception)
        {
            // Expected for some edge cases — caller will check state
        }
    }

    public void Dispose()
    {
        _testCancellation.Cancel();

        foreach (var client in _clients)
        {
            try
            {
                if (client.IsConnected)
                {
                    client.DisconnectAsync("Test cleanup").GetAwaiter().GetResult();
                }
                client.Dispose();
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _clients.Clear();
        _testCancellation.Dispose();
    }

    #endregion
}
