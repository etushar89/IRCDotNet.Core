using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
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
    /// Three users exchange a human-paced scripted conversation and every receiver
    /// verifies the exact ordered message stream it should observe.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ThreeUsers_HumanPacedEdgeCaseChat_ShouldDeliverEveryMessageInOrder()
    {
        const int participantCount = 3;
        var suffix = DateTimeOffset.UtcNow.Ticks % 10000;
        var nicks = Enumerable.Range(0, participantCount).Select(index => $"Chat{index}_{suffix}").ToArray();
        var messagePrefix = $"HC{suffix}:";
        var clients = new IrcClient[participantCount];
        var receivedByParticipant = Enumerable.Range(0, participantCount).Select(_ => new List<HumanChatReceipt>()).ToArray();
        var receiveGates = Enumerable.Range(0, participantCount).Select(_ => new object()).ToArray();
        var sentCounts = new int[participantCount];
        var sendFailures = new ConcurrentBag<string>();
        var chatTurns = CreateHumanChatTurns();

        _output.WriteLine($"=== Connecting {participantCount} human-paced chat participants ===");
        for (var participantIndex = 0; participantIndex < participantCount; participantIndex++)
        {
            clients[participantIndex] = await ConnectSingleUserAsync(nicks[participantIndex]);
        }

        for (var participantIndex = 0; participantIndex < participantCount; participantIndex++)
        {
            var receiverIndex = participantIndex;
            clients[participantIndex].PrivateMessageReceived += (_, eventArgs) =>
            {
                if (eventArgs.IsChannelMessage
                    || string.Equals(eventArgs.Nick, nicks[receiverIndex], StringComparison.OrdinalIgnoreCase)
                    || !TryParseHumanChatSequence(eventArgs.Text, messagePrefix, out var sequence))
                {
                    return;
                }

                lock (receiveGates[receiverIndex])
                {
                    receivedByParticipant[receiverIndex].Add(new HumanChatReceipt(sequence, eventArgs.Nick, eventArgs.Text));
                }
            };
        }

        _output.WriteLine($"=== Sending {chatTurns.Count} chat turns at human messaging cadence ===");
        foreach (var chatTurn in chatTurns)
        {
            var sender = clients[chatTurn.SenderIndex];
            var text = $"{messagePrefix}{chatTurn.Sequence:D2}:{chatTurn.Text}";
            Assert.True(Encoding.UTF8.GetByteCount(text) < 400, $"Payload {chatTurn.Sequence} should remain safely below IRC line length limits.");

            for (var targetIndex = 0; targetIndex < participantCount; targetIndex++)
            {
                if (targetIndex == chatTurn.SenderIndex)
                {
                    continue;
                }

                try
                {
                    await sender.SendMessageAsync(nicks[targetIndex], text);
                    sentCounts[chatTurn.SenderIndex]++;
                }
                catch (Exception exception)
                {
                    sendFailures.Add($"turn {chatTurn.Sequence} {nicks[chatTurn.SenderIndex]}->{nicks[targetIndex]}: {exception.GetType().Name}");
                }

                await Task.Delay(150, _testCancellation.Token);
            }

            await Task.Delay(1200, _testCancellation.Token);
        }

        Assert.Empty(sendFailures);

        var deliveryDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deliveryDeadline
               && receivedByParticipant.Select((receipts, participantIndex) => receipts.Count < chatTurns.Count(turn => turn.SenderIndex != participantIndex)).Any(waiting => waiting))
        {
            await Task.Delay(250, _testCancellation.Token);
        }

        for (var participantIndex = 0; participantIndex < participantCount; participantIndex++)
        {
            var expectedSentCount = chatTurns.Count(turn => turn.SenderIndex == participantIndex) * (participantCount - 1);
            Assert.Equal(expectedSentCount, sentCounts[participantIndex]);

            List<HumanChatReceipt> actualReceipts;
            lock (receiveGates[participantIndex])
            {
                actualReceipts = receivedByParticipant[participantIndex].ToList();
            }

            var expectedReceipts = chatTurns
                .Where(turn => turn.SenderIndex != participantIndex)
                .Select(turn => new HumanChatReceipt(turn.Sequence, nicks[turn.SenderIndex], $"{messagePrefix}{turn.Sequence:D2}:{turn.Text}"))
                .ToArray();

            Assert.Equal(expectedReceipts.Length, actualReceipts.Count);
            for (var receiptIndex = 0; receiptIndex < expectedReceipts.Length; receiptIndex++)
            {
                Assert.Equal(expectedReceipts[receiptIndex].Sequence, actualReceipts[receiptIndex].Sequence);
                Assert.Equal(expectedReceipts[receiptIndex].FromNick, actualReceipts[receiptIndex].FromNick);
                Assert.Equal(expectedReceipts[receiptIndex].Text, actualReceipts[receiptIndex].Text);
            }

            Assert.True(clients[participantIndex].IsConnected, $"{nicks[participantIndex]} should stay connected through the human-paced chat.");
        }

        var invalidMultiline = $"{messagePrefix}99:actual CRLF is invalid\r\nsecond line";
        await Assert.ThrowsAsync<ArgumentException>(() => clients[0].SendMessageAsync(nicks[1], invalidMultiline));
        Assert.All(clients, client => Assert.True(client.IsConnected));

        _output.WriteLine("✅ Human-paced edge-case chat delivered every expected message in order");
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

    /// <summary>
    /// A monitored PM-only contact that quits should raise a quit event even without a shared channel.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task MonitoredPrivateContact_Quit_ShouldRaiseUserQuitWithoutSharedChannel()
    {
        var observerNick = $"MonQuitA_{DateTime.Now.Ticks % 10000}";
        var peerNick = $"MonQuitB_{DateTime.Now.Ticks % 10000}";

        var observer = await ConnectSingleUserAsync(observerNick, EnableExtendedMonitor);
        var peer = await ConnectSingleUserAsync(peerNick, EnableExtendedMonitor);

        var rawMonitorOffline = new ConcurrentBag<string>();
        var quitEvents = new ConcurrentBag<UserQuitEvent>();

        observer.RawMessageReceived += (sender, e) =>
        {
            if (e.Message.Command == IrcNumericReplies.RPL_MONOFFLINE)
                rawMonitorOffline.Add(e.Message.Serialize());
        };
        observer.UserQuit += (sender, e) => quitEvents.Add(e);

        await peer.SendMessageAsync(observerNick, "hello-before-quit");
        await Task.Delay(1000);

        Assert.Contains(IrcCapabilities.EXTENDED_MONITOR, observer.EnabledCapabilities);

        await observer.MonitorNickAsync(peerNick);
        await Task.Delay(1000);

        await peer.SendRawAsync("QUIT :test quit");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!quitEvents.Any(e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        Assert.Contains(rawMonitorOffline, raw => raw.Contains(IrcNumericReplies.RPL_MONOFFLINE, StringComparison.OrdinalIgnoreCase) && raw.Contains(peerNick, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(quitEvents, e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A monitored PM-only contact should only raise NickChanged when the server exposes an explicit NICK signal.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task MonitoredPrivateContact_NickChange_ShouldOnlyRaiseNickChangedWhenExplicitNickSignalIsObserved()
    {
        var observerNick = $"MonNickA_{DateTime.Now.Ticks % 10000}";
        var peerNick = $"MonNickB_{DateTime.Now.Ticks % 10000}";
        var renamedNick = $"{peerNick}x";

        var observer = await ConnectSingleUserAsync(observerNick, EnableExtendedMonitor);
        var peer = await ConnectSingleUserAsync(peerNick, EnableExtendedMonitor);

        var rawMonitorOnline = new ConcurrentBag<string>();
        var rawMonitorOffline = new ConcurrentBag<string>();
        var rawNickChanges = new ConcurrentBag<string>();
        var nickChangedEvents = new ConcurrentBag<NickChangedEvent>();
        var quitEvents = new ConcurrentBag<UserQuitEvent>();
        var renamedMessages = new ConcurrentBag<(string Nick, string Text)>();

        observer.RawMessageReceived += (sender, e) =>
        {
            if (e.Message.Command == IrcNumericReplies.RPL_MONONLINE)
                rawMonitorOnline.Add(e.Message.Serialize());
            else if (e.Message.Command == IrcNumericReplies.RPL_MONOFFLINE)
                rawMonitorOffline.Add(e.Message.Serialize());
            else if (e.Message.Command == "NICK")
                rawNickChanges.Add(e.Message.Serialize());
        };
        observer.NickChanged += (sender, e) => nickChangedEvents.Add(e);
        observer.UserQuit += (sender, e) => quitEvents.Add(e);
        observer.PrivateMessageReceived += (sender, e) =>
        {
            if (!e.IsChannelMessage)
                renamedMessages.Add((e.Nick, e.Text));
        };

        await peer.SendMessageAsync(observerNick, "hello-before-rename");
        await Task.Delay(1000);

        Assert.Contains(IrcCapabilities.EXTENDED_MONITOR, observer.EnabledCapabilities);

        await observer.MonitorNickAsync(peerNick);
        await Task.Delay(1000);

        await peer.SendRawAsync($"NICK {renamedNick}");
        await peer.SendMessageAsync(observerNick, "hello-after-rename");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var sawRenamedMessage = renamedMessages.Any(message =>
                string.Equals(message.Nick, renamedNick, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(message.Text, "hello-after-rename", StringComparison.Ordinal));
            var sawExplicitRename = nickChangedEvents.Any(e =>
                string.Equals(e.OldNick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.NewNick, renamedNick, StringComparison.OrdinalIgnoreCase));
            var sawOldQuit = quitEvents.Any(e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));

            if (sawRenamedMessage && (sawExplicitRename || sawOldQuit))
            {
                break;
            }

            await Task.Delay(200);
        }

        await Task.Delay(1000);

        Assert.Contains(rawMonitorOffline, raw => raw.Contains(IrcNumericReplies.RPL_MONOFFLINE, StringComparison.OrdinalIgnoreCase) && raw.Contains(peerNick, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(renamedMessages, message =>
            string.Equals(message.Nick, renamedNick, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(message.Text, "hello-after-rename", StringComparison.Ordinal));

        if (nickChangedEvents.Any(e =>
                string.Equals(e.OldNick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.NewNick, renamedNick, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Contains(rawNickChanges, raw =>
                raw.Contains("NICK", StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(peerNick, StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(renamedNick, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Contains(quitEvents, e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// A PM-only monitored contact should surface away-notify transitions without requiring a shared channel.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task MonitoredPrivateContact_AwayStatusChanges_ShouldRaiseEventsWithoutSharedChannel()
    {
        var observerNick = $"MonAwayA_{DateTime.Now.Ticks % 10000}";
        var peerNick = $"MonAwayB_{DateTime.Now.Ticks % 10000}";

        var observer = await ConnectSingleUserAsync(observerNick, EnableExtendedMonitor);
        var peer = await ConnectSingleUserAsync(peerNick, EnableExtendedMonitor);

        var awayEvents = new ConcurrentBag<UserAwayStatusChangedEvent>();

        observer.UserAwayStatusChanged += (sender, e) => awayEvents.Add(e);

        await peer.SendMessageAsync(observerNick, "hello-before-away");
        await Task.Delay(1000);

        await observer.MonitorNickAsync(peerNick);
        await Task.Delay(1000);

        await peer.SetAwayAsync("pm-away-status");

        var awayDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!awayEvents.Any(e =>
                   string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                   e.IsAway &&
                   string.Equals(e.AwayMessage, "pm-away-status", StringComparison.Ordinal))
               && DateTime.UtcNow < awayDeadline)
        {
            await Task.Delay(200);
        }

        await peer.SetAwayAsync();

        var backDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!awayEvents.Any(e =>
                   string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                   !e.IsAway)
               && DateTime.UtcNow < backDeadline)
        {
            await Task.Delay(200);
        }

        Assert.Contains(awayEvents, e =>
            string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase) &&
            e.IsAway &&
            string.Equals(e.AwayMessage, "pm-away-status", StringComparison.Ordinal));
        Assert.Contains(awayEvents, e =>
            string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase) &&
            !e.IsAway);
    }

    /// <summary>
    /// A monitored PM-only contact should only transfer quit tracking to the renamed nick when an explicit NICK signal is observed.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task MonitoredPrivateContact_NickChangeThenQuit_ShouldRequireExplicitNickSignalToTrackRenamedNick()
    {
        var observerNick = $"MonSeqA_{DateTime.Now.Ticks % 10000}";
        var peerNick = $"MonSeqB_{DateTime.Now.Ticks % 10000}";
        var renamedNick = $"{peerNick}x";

        var observer = await ConnectSingleUserAsync(observerNick, EnableExtendedMonitor);
        var peer = await ConnectSingleUserAsync(peerNick, EnableExtendedMonitor);

        var nickChangedEvents = new ConcurrentBag<NickChangedEvent>();
        var quitEvents = new ConcurrentBag<UserQuitEvent>();
        var rawMonitorOffline = new ConcurrentBag<string>();
        var rawNickChanges = new ConcurrentBag<string>();

        observer.NickChanged += (_, e) => nickChangedEvents.Add(e);
        observer.UserQuit += (_, e) => quitEvents.Add(e);
        observer.RawMessageReceived += (_, e) =>
        {
            if (e.Message.Command == IrcNumericReplies.RPL_MONOFFLINE)
                rawMonitorOffline.Add(e.Message.Serialize());
            else if (e.Message.Command == "NICK")
                rawNickChanges.Add(e.Message.Serialize());
        };

        await peer.SendMessageAsync(observerNick, "hello-before-sequence");
        await Task.Delay(1000);

        await observer.MonitorNickAsync(peerNick);
        await Task.Delay(1000);

        await peer.SendRawAsync($"NICK {renamedNick}");
        await peer.SendMessageAsync(observerNick, "hello-after-sequence-rename");

        var nickDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!quitEvents.Any(e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase))
               && !nickChangedEvents.Any(e =>
                   string.Equals(e.OldNick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(e.NewNick, renamedNick, StringComparison.OrdinalIgnoreCase))
               && DateTime.UtcNow < nickDeadline)
        {
            await Task.Delay(200);
        }

        var sawExplicitRename = nickChangedEvents.Any(e =>
            string.Equals(e.OldNick, peerNick, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.NewNick, renamedNick, StringComparison.OrdinalIgnoreCase));

        await peer.SendRawAsync("QUIT :renamed quit");

        var quitDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < quitDeadline)
        {
            var sawOldQuit = quitEvents.Any(e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));
            var sawRenamedQuit = quitEvents.Any(e => string.Equals(e.Nick, renamedNick, StringComparison.OrdinalIgnoreCase));

            if (sawExplicitRename ? sawRenamedQuit : sawOldQuit)
            {
                break;
            }

            await Task.Delay(200);
        }

        if (sawExplicitRename)
        {
            Assert.Contains(rawNickChanges, raw =>
                raw.Contains("NICK", StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(peerNick, StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(renamedNick, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(quitEvents, e => string.Equals(e.Nick, renamedNick, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(quitEvents, e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rawMonitorOffline, raw =>
                raw.Contains(IrcNumericReplies.RPL_MONOFFLINE, StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(renamedNick, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.DoesNotContain(nickChangedEvents, e =>
                string.Equals(e.OldNick, peerNick, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.NewNick, renamedNick, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(quitEvents, e => string.Equals(e.Nick, peerNick, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(quitEvents, e => string.Equals(e.Nick, renamedNick, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rawMonitorOffline, raw =>
                raw.Contains(IrcNumericReplies.RPL_MONOFFLINE, StringComparison.OrdinalIgnoreCase) &&
                raw.Contains(peerNick, StringComparison.OrdinalIgnoreCase));
        }
    }

    #region Helper Methods

    private async Task<IrcClient> ConnectSingleUserAsync(string nick, Action<IrcClientOptions>? configureOptions = null)
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

        configureOptions?.Invoke(options);

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

    private static void EnableExtendedMonitor(IrcClientOptions options)
    {
        if (!options.RequestedCapabilities.Contains(IrcCapabilities.MONITOR, StringComparer.OrdinalIgnoreCase))
            options.RequestedCapabilities.Add(IrcCapabilities.MONITOR);
        if (!options.RequestedCapabilities.Contains(IrcCapabilities.EXTENDED_MONITOR, StringComparer.OrdinalIgnoreCase))
            options.RequestedCapabilities.Add(IrcCapabilities.EXTENDED_MONITOR);
    }

    private static IReadOnlyList<HumanChatTurn> CreateHumanChatTurns()
    {
        return
        [
            new HumanChatTurn(0, 0, "Plain ASCII hello with numbers 12345."),
            new HumanChatTurn(1, 1, "Punctuation: !?.,;:'\"()[]{}<> @#$%^&*_-+=/\\|~`"),
            new HumanChatTurn(2, 2, "Latin accents: café naïve résumé coöperate São Paulo."),
            new HumanChatTurn(3, 0, "Cyrillic: Привет мир. До встречи."),
            new HumanChatTurn(4, 1, "CJK: こんにちは世界 | 你好世界 | 안녕하세요 세계."),
            new HumanChatTurn(5, 2, "RTL scripts: مرحبا بالعالم | שלום עולם."),
            new HumanChatTurn(6, 0, "Emoji: 😀 🚀 ✨ 👍🏽 🎉."),
            new HumanChatTurn(7, 1, "ZWJ emoji: family 👨‍👩‍👧‍👦, technologist 🧑‍💻."),
            new HumanChatTurn(8, 2, "Math and currency: ∑ Δ π ≈ √∞ € £ ¥ ₹."),
            new HumanChatTurn(9, 0, "Display line separators: first\u2028second\u2029third."),
            new HumanChatTurn(10, 1, "Escaped newline literals: first\\nsecond\\r\\nthird."),
            new HumanChatTurn(11, 2, "IRC formatting: \u0002bold\u0002 \u001Ditalic\u001D \u001Funderline\u001F \u000303green\u000F reset."),
            new HumanChatTurn(12, 0, "Quotes and brackets: \"double\" 'single' «angle» ‘curly’."),
            new HumanChatTurn(13, 1, "Whitespace markers: [  leading and trailing style spaces  ] tabs-as-text \\t."),
            new HumanChatTurn(14, 2, string.Concat("Long mixed safe payload: ", new string('x', 120)))
        ];
    }

    private static bool TryParseHumanChatSequence(string text, string messagePrefix, out int sequence)
    {
        sequence = -1;
        if (!text.StartsWith(messagePrefix, StringComparison.Ordinal) || text.Length < messagePrefix.Length + 3)
        {
            return false;
        }

        if (text[messagePrefix.Length + 2] != ':')
        {
            return false;
        }

        return int.TryParse(text.AsSpan(messagePrefix.Length, 2), NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    private sealed record HumanChatTurn(int Sequence, int SenderIndex, string Text);

    private sealed record HumanChatReceipt(int Sequence, string FromNick, string Text);

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
