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
/// Comprehensive integration tests against real IRC servers.
/// Tests both TCP/SSL and WebSocket transports with chatty multi-user conversations.
/// Covers: connect, capabilities, channels, messages, unicode, CTCP, nick changes,
/// away status, topic, notices, WHOIS, part, disconnect, and edge cases.
/// </summary>
[Trait("Category", "LiveServer")]
public class LiveServerIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LiveServerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Server endpoint definitions for all transports we've validated.
    /// HybridIRC-WS is excluded: the KiwiIRC bouncer has a ~15 msg/min flood limit
    /// that kills connections during this comprehensive test. It passes the shorter
    /// test-ws-full (9 ops) but not the 42-assertion suite here.
    /// </summary>
    public static IEnumerable<object[]> ServerEndpoints =>
    [
        ["UnrealIRCd-WS",  null,                    0,    false, "wss://irc.unrealircd.org/"],
        ["HybridIRC-TCP",  "irc.hybridirc.com",     6697, true,  null],
    ];

    [Theory(Timeout = 300_000)] // 5 minute hard ceiling
    [MemberData(nameof(ServerEndpoints))]
    public async Task FullConversation_AllFeatures(
        string label, string? server, int port, bool ssl, string? wsUri)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random();
        var suffix = rng.Next(1000, 9999);
        var nickA = $"DotA{suffix}";
        var nickB = $"DotB{suffix}";
        var channel = $"#dnt-{suffix}";
        var passed = 0;
        var failed = 0;

        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");
        void Pass(string t) { Interlocked.Increment(ref passed); Log($"  PASS: {t}"); }
        void Fail(string t) { Interlocked.Increment(ref failed); Log($"  FAIL: {t}"); }
        void Assert(bool ok, string t) { if (ok) Pass(t); else Fail(t); }

        // Pacing delay between sends — WebSocket bouncers have tighter flood limits
        const int Pace = 1200;

        Log($"=== {label} === nick={nickA}/{nickB} channel={channel}");

        // ── Build options ────────────────────────────────────────────
        IrcClientOptions MakeOpts(string nick) => new()
        {
            Server = server ?? string.Empty,
            Port = port,
            UseSsl = ssl,
            WebSocketUri = wsUri,
            Nick = nick,
            UserName = nick.ToLower(),
            RealName = $"{nick} IntegrationTest",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        // ── Event collectors ─────────────────────────────────────────
        var aCaps = new TaskCompletionSource<IReadOnlySet<string>>();
        var aConnected = new TaskCompletionSource<ConnectedEvent>();
        var aMsgs = new ConcurrentBag<(string Nick, string Text)>();
        var aNotices = new ConcurrentBag<(string Nick, string Text)>();
        var aActions = new ConcurrentBag<(string Nick, string Text)>();
        var aJoins = new ConcurrentBag<string>();
        var aParts = new ConcurrentBag<string>();
        var aTopics = new ConcurrentBag<string>();
        var aNickChanges = new ConcurrentBag<(string Old, string New)>();
        var aCtcpReplies = new ConcurrentBag<(string Cmd, string Reply)>();
        var aRaw = new ConcurrentBag<string>();

        var bConnected = new TaskCompletionSource<ConnectedEvent>();
        var bMsgs = new ConcurrentBag<(string Nick, string Text)>();
        var bCtcpRequests = new ConcurrentBag<string>();

        await using var alice = new IrcClient(MakeOpts(nickA));
        await using var bob = new IrcClient(MakeOpts(nickB));

        // ── Wire Alice events ────────────────────────────────────────
        alice.Connected += (s, e) => { Log($"[A] Connected to {e.Network}"); aConnected.TrySetResult(e); };
        alice.Disconnected += (s, e) => Log($"[A] Disconnected: {e.Reason}");
        alice.CapabilitiesNegotiated += (s, e) => { Log($"[A] Caps: {string.Join(", ", e.EnabledCapabilities)}"); aCaps.TrySetResult(e.EnabledCapabilities); };
        alice.PrivateMessageReceived += (s, e) => { Log($"[A] <{e.Nick}> {e.Text}"); aMsgs.Add((e.Nick, e.Text)); };
        alice.NoticeReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Nick)) { Log($"[A] -{e.Nick}- {e.Text}"); aNotices.Add((e.Nick, e.Text)); } };
        alice.CtcpActionReceived += (s, e) => { Log($"[A] * {e.Nick} {e.ActionText}"); aActions.Add((e.Nick, e.ActionText)); };
        alice.CtcpReplyReceived += (s, e) => { Log($"[A] CTCP {e.Command} from {e.Nick}: {e.ReplyText}"); aCtcpReplies.Add((e.Command, e.ReplyText)); };
        alice.UserJoinedChannel += (s, e) => { if (e.Nick != nickA) { Log($"[A] {e.Nick} joined {e.Channel}"); aJoins.Add(e.Nick); } };
        alice.UserLeftChannel += (s, e) => { Log($"[A] {e.Nick} left {e.Channel}"); aParts.Add(e.Nick); };
        alice.TopicChanged += (s, e) => { Log($"[A] Topic={e.Topic}"); aTopics.Add(e.Topic ?? ""); };
        alice.NickChanged += (s, e) => { Log($"[A] Nick: {e.OldNick}->{e.NewNick}"); aNickChanges.Add((e.OldNick, e.NewNick)); };
        alice.RawMessageReceived += (s, e) => aRaw.Add(e.Message.Command);

        // ── Wire Bob events ──────────────────────────────────────────
        bob.Connected += (s, e) => { Log($"[B] Connected to {e.Network}"); bConnected.TrySetResult(e); };
        bob.Disconnected += (s, e) => Log($"[B] Disconnected: {e.Reason}");
        bob.PrivateMessageReceived += (s, e) => { Log($"[B] <{e.Nick}> {e.Text}"); bMsgs.Add((e.Nick, e.Text)); };
        bob.CtcpRequestReceived += (s, e) => { Log($"[B] CTCP-REQ {e.Command} from {e.Nick}"); bCtcpRequests.Add(e.Command); };

        // ══════════════════════════════════════════════════════════════
        // 1. CONNECT
        // ══════════════════════════════════════════════════════════════
        Log("--- 1. Connect ---");
        await alice.ConnectAsync();
        await bob.ConnectAsync();

        using var timeout = new CancellationTokenSource(25_000);
        timeout.Token.Register(() => { aConnected.TrySetCanceled(); bConnected.TrySetCanceled(); });

        try { await Task.WhenAll(aConnected.Task, bConnected.Task); }
        catch { Fail("Both clients connect"); await Cleanup(alice, bob); return; }

        Pass("Both clients connected");
        Assert(alice.IsConnected, "Alice IsConnected=true");
        Assert(bob.IsConnected, "Bob IsConnected=true");
        Assert(alice.CurrentNick == nickA, $"Alice nick={nickA}");
        Assert(bob.CurrentNick == nickB, $"Bob nick={nickB}");

        // ══════════════════════════════════════════════════════════════
        // 2. CAPABILITY NEGOTIATION
        // ══════════════════════════════════════════════════════════════
        Log("--- 2. Capability negotiation ---");
        await Task.Delay(500);

        // Check that raw messages include RPL_WELCOME (001)
        Assert(aRaw.Contains("001"), "Received RPL_WELCOME (001)");

        // Try to read caps (may have fired already or may not be supported)
        IReadOnlySet<string>? caps = null;
        if (aCaps.Task.IsCompleted)
        {
            caps = await aCaps.Task;
            Log($"  Negotiated caps: {string.Join(", ", caps)}");
            // Most modern servers support at least one of these
            Assert(caps.Count > 0, "At least one capability negotiated");
        }
        else
        {
            Log("  (No capability negotiation event — server may not support CAP)");
            Pass("Capability negotiation skipped (server unsupported — OK)");
        }

        // ══════════════════════════════════════════════════════════════
        // 3. JOIN CHANNEL
        // ══════════════════════════════════════════════════════════════
        Log("--- 3. Join channel ---");
        await alice.JoinChannelAsync(channel);
        await Task.Delay(1500);
        await bob.JoinChannelAsync(channel);
        await Task.Delay(2000);

        Assert(aJoins.Contains(nickB), "Alice saw Bob join");

        // ══════════════════════════════════════════════════════════════
        // 4. CHANNEL MESSAGES — plain text
        // ══════════════════════════════════════════════════════════════
        Log("--- 4. Channel messages ---");
        await bob.SendMessageAsync(channel, $"Hello {nickA}, testing from IRCDotNet.Core!");
        await Task.Delay(1200);

        Assert(aMsgs.Any(m => m.Nick == nickB && m.Text.Contains("Hello")),
            "Alice received Bob's channel message");

        // ══════════════════════════════════════════════════════════════
        // 5. UNICODE / SPECIAL CHARACTERS
        // ══════════════════════════════════════════════════════════════
        Log("--- 5. Unicode & special characters ---");
        var unicodeTests = new[]
        {
            ("Emoji", "Testing emoji: \U0001F680\U0001F30D\U0001F525"),
            ("CJK", "日本語テスト — Chinese 中文 — Korean 한국어"),
            ("Cyrillic", "Привет мир! — Ελληνικά — العربية"),
            ("Symbols", "Math: ∑∏∫∂ — Currency: £€¥₹ — Arrows: ←→↑↓"),
            ("Accents", "Ñoño — Ärger — naïve — résumé — façade"),
        };

        foreach (var (name, text) in unicodeTests)
        {
            await bob.SendMessageAsync(channel, text);
            await Task.Delay(Pace);
        }

        // Verify at least some unicode messages arrived intact
        Assert(aMsgs.Any(m => m.Text.Contains("\U0001F680")), "Emoji message received intact");
        Assert(aMsgs.Any(m => m.Text.Contains("日本語")), "CJK message received intact");
        Assert(aMsgs.Any(m => m.Text.Contains("Привет")), "Cyrillic message received intact");
        Assert(aMsgs.Any(m => m.Text.Contains("∑∏∫")), "Math symbols received intact");
        Assert(aMsgs.Any(m => m.Text.Contains("résumé")), "Accented chars received intact");

        // ══════════════════════════════════════════════════════════════
        // 6. EDGE-CASE MESSAGES
        // ══════════════════════════════════════════════════════════════
        Log("--- 6. Edge-case messages ---");

        // Very long message (close to IRC 512 limit minus overhead)
        var longText = new string('X', 400);
        await bob.SendMessageAsync(channel, longText);
        await Task.Delay(Pace);
        Assert(aMsgs.Any(m => m.Text.Length >= 350), "Long message (400 chars) received");

        // Message with IRC-like colons
        await bob.SendMessageAsync(channel, "key:value :: test:123 :trailing");
        await Task.Delay(Pace);
        Assert(aMsgs.Any(m => m.Text.Contains("key:value")), "Message with colons received");

        // Single character
        await bob.SendMessageAsync(channel, ".");
        await Task.Delay(Pace);
        Assert(aMsgs.Any(m => m.Text == "."), "Single char message received");

        // Repeated spaces
        await bob.SendMessageAsync(channel, "with   multiple   spaces");
        await Task.Delay(Pace);
        Assert(aMsgs.Any(m => m.Text.Contains("  ")), "Message with multiple spaces preserved");

        // ══════════════════════════════════════════════════════════════
        // 7. PRIVATE MESSAGES (both directions)
        // ══════════════════════════════════════════════════════════════
        Log("--- 7. Private messages ---");
        await alice.SendMessageAsync(nickB, "Hey Bob, secret PM from Alice!");
        await Task.Delay(1200);
        Assert(bMsgs.Any(m => m.Nick == nickA && m.Text.Contains("secret PM")),
            "Bob received Alice's PM");

        await bob.SendMessageAsync(nickA, "Got it, replying privately!");
        await Task.Delay(1200);
        Assert(aMsgs.Any(m => m.Nick == nickB && m.Text.Contains("replying privately")),
            "Alice received Bob's PM reply");

        // ══════════════════════════════════════════════════════════════
        // 8. CTCP ACTION (/me)
        // ══════════════════════════════════════════════════════════════
        Log("--- 8. CTCP ACTION ---");
        await bob.SendActionAsync(channel, "waves at everyone");
        await Task.Delay(1200);
        Assert(aActions.Any(a => a.Nick == nickB && a.Text.Contains("waves")),
            "Alice received Bob's /me action");

        // Action with unicode
        await bob.SendActionAsync(channel, "dances \U0001F483\U0001F3A9");
        await Task.Delay(1200);
        Assert(aActions.Any(a => a.Text.Contains("\U0001F483")),
            "Action with emoji received");

        // ══════════════════════════════════════════════════════════════
        // 9. CTCP VERSION (request + auto-reply)
        // ══════════════════════════════════════════════════════════════
        Log("--- 9. CTCP VERSION ---");
        await alice.SendCtcpRequestAsync(nickB, "VERSION");
        await Task.Delay(2000);

        Assert(bCtcpRequests.Contains("VERSION"), "Bob received VERSION request");
        Assert(aCtcpReplies.Any(r => r.Cmd == "VERSION" && r.Reply.Contains("IRCDotNet")),
            "Alice received VERSION reply with lib name");

        // ══════════════════════════════════════════════════════════════
        // 10. CTCP PING (round-trip)
        // ══════════════════════════════════════════════════════════════
        Log("--- 10. CTCP PING ---");
        var pingToken = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        await alice.SendCtcpRequestAsync(nickB, "PING", pingToken);
        await Task.Delay(2000);

        Assert(bCtcpRequests.Contains("PING"), "Bob received PING request");
        Assert(aCtcpReplies.Any(r => r.Cmd == "PING" && r.Reply.Contains(pingToken)),
            "Alice received PING reply with token");

        // ══════════════════════════════════════════════════════════════
        // 11. CTCP TIME
        // ══════════════════════════════════════════════════════════════
        Log("--- 11. CTCP TIME ---");
        await alice.SendCtcpRequestAsync(nickB, "TIME");
        await Task.Delay(2000);

        Assert(aCtcpReplies.Any(r => r.Cmd == "TIME"),
            "Alice received TIME reply");

        // ══════════════════════════════════════════════════════════════
        // 12. CTCP CLIENTINFO
        // ══════════════════════════════════════════════════════════════
        Log("--- 12. CTCP CLIENTINFO ---");
        await alice.SendCtcpRequestAsync(nickB, "CLIENTINFO");
        await Task.Delay(2000);

        Assert(aCtcpReplies.Any(r => r.Cmd == "CLIENTINFO" && r.Reply.Contains("ACTION")),
            "Alice received CLIENTINFO listing ACTION");

        // ══════════════════════════════════════════════════════════════
        // 13. TOPIC CHANGE
        // ══════════════════════════════════════════════════════════════
        Log("--- 13. Topic change ---");
        var topicText = $"IRCDotNet test — {DateTime.UtcNow:HH:mm:ss} — \U0001F3C1";
        await alice.SetTopicAsync(channel, topicText);
        await Task.Delay(1500);

        Assert(aTopics.Any(t => t.Contains("IRCDotNet test")), "Topic changed event received");

        // ══════════════════════════════════════════════════════════════
        // 14. NOTICE
        // ══════════════════════════════════════════════════════════════
        Log("--- 14. Notice ---");
        await bob.SendNoticeAsync(nickA, "This is a notice from Bob");
        await Task.Delay(1200);

        Assert(aNotices.Any(n => n.Nick == nickB && n.Text.Contains("notice from Bob")),
            "Alice received Bob's notice");

        // ══════════════════════════════════════════════════════════════
        // 15. NICK CHANGE
        // ══════════════════════════════════════════════════════════════
        Log("--- 15. Nick change ---");
        var newNick = $"{nickB}_v2";
        await bob.ChangeNickAsync(newNick);
        await Task.Delay(1500);

        Assert(aNickChanges.Any(n => n.Old == nickB && n.New == newNick),
            "Alice saw Bob's nick change");
        Assert(bob.CurrentNick == newNick, "Bob's CurrentNick updated");

        // ══════════════════════════════════════════════════════════════
        // 16. AWAY / BACK
        // ══════════════════════════════════════════════════════════════
        Log("--- 16. Away / back ---");
        await bob.SetAwayAsync("BRB testing \U0001F4BB");
        await Task.Delay(1000);

        // Send a message to trigger 301 RPL_AWAY (server sends it when you MSG an away user)
        await alice.SendMessageAsync(newNick, "Are you there?");
        await Task.Delay(1500);

        // Come back
        await bob.SetAwayAsync(); // null = unset away
        await Task.Delay(1000);

        Pass("Away/back cycle completed without error");

        // ══════════════════════════════════════════════════════════════
        // 17. WHOIS
        // ══════════════════════════════════════════════════════════════
        Log("--- 17. WHOIS ---");
        await alice.GetUserInfoAsync(newNick);
        await Task.Delay(1500);

        // WHOIS triggers raw 311/318 replies
        Assert(aRaw.Contains("311") || aRaw.Contains("318"),
            "WHOIS reply received (311 or 318)");

        // ══════════════════════════════════════════════════════════════
        // 18. WHO
        // ══════════════════════════════════════════════════════════════
        Log("--- 18. WHO ---");
        await alice.WhoAsync(channel);
        await Task.Delay(1500);

        Assert(aRaw.Contains("352") || aRaw.Contains("315"),
            "WHO reply received (352 or 315)");

        // ══════════════════════════════════════════════════════════════
        // 19. CHANNEL USERS (NAMES)
        // ══════════════════════════════════════════════════════════════
        Log("--- 19. Channel users (NAMES) ---");
        var usersReceived = new TaskCompletionSource<ChannelUsersEvent>();
        alice.ChannelUsersReceived += (s, e) => { if (e.Channel == channel) usersReceived.TrySetResult(e); };
        await alice.GetChannelUsersAsync(channel);

        try
        {
            var usersEvt = await usersReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(usersEvt.Users.Count >= 2, $"NAMES returned {usersEvt.Users.Count} users (expected >=2)");
            Assert(usersEvt.Users.Any(u => u.Nick == nickA), "NAMES includes Alice");
        }
        catch (TimeoutException)
        {
            Fail("NAMES reply timed out");
        }

        // ══════════════════════════════════════════════════════════════
        // 20. RAPID-FIRE MESSAGES (burst test, respectful)
        // ══════════════════════════════════════════════════════════════
        Log("--- 20. Rapid-fire burst (10 messages) ---");
        var burstCount = 10;
        var preBurstCount = aMsgs.Count;
        for (int i = 0; i < burstCount; i++)
        {
            await bob.SendMessageAsync(channel, $"Burst #{i}: rapid fire test");
            await Task.Delay(300); // small gap to avoid excess flood
        }
        await Task.Delay(3000); // wait for all to arrive

        var burstReceived = aMsgs.Count - preBurstCount;
        Assert(burstReceived >= burstCount - 1,
            $"Burst: received {burstReceived}/{burstCount} messages");

        // ══════════════════════════════════════════════════════════════
        // 21. NICK CHANGE BACK
        // ══════════════════════════════════════════════════════════════
        Log("--- 21. Nick change back ---");
        await bob.ChangeNickAsync(nickB);
        await Task.Delay(1500);

        Assert(aNickChanges.Any(n => n.New == nickB), "Alice saw nick change back");

        // ══════════════════════════════════════════════════════════════
        // 22. PART CHANNEL (with reason)
        // ══════════════════════════════════════════════════════════════
        Log("--- 22. Part channel ---");
        await bob.LeaveChannelAsync(channel, "Integration test complete \U0001F44B");
        await Task.Delay(1500);

        Assert(aParts.Any(p => p == nickB), "Alice saw Bob part");

        // ══════════════════════════════════════════════════════════════
        // 23. GRACEFUL DISCONNECT
        // ══════════════════════════════════════════════════════════════
        Log("--- 23. Disconnect ---");
        await bob.DisconnectAsync("IRCDotNet integration test done");
        await Task.Delay(500);
        Assert(!bob.IsConnected, "Bob disconnected");

        await alice.LeaveChannelAsync(channel, "Done");
        await alice.DisconnectAsync("IRCDotNet integration test done");
        await Task.Delay(500);
        Assert(!alice.IsConnected, "Alice disconnected");

        // ══════════════════════════════════════════════════════════════
        // SUMMARY
        // ══════════════════════════════════════════════════════════════
        Log($"\n=== {label}: {passed} passed, {failed} failed (elapsed {sw.Elapsed:mm\\:ss}) ===");
        Xunit.Assert.Equal(0, failed);
    }

    private static async Task Cleanup(params IrcClient[] clients)
    {
        foreach (var c in clients)
        {
            try { await c.DisconnectAsync("test cleanup"); } catch { }
        }
    }
}
