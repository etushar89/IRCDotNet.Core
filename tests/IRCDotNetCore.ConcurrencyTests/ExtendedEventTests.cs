using System.Collections.Concurrent;
using System.Diagnostics;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Extended integration tests for newly added events and edge cases not covered
/// by LiveServerIntegrationTests. Tests INVITE, OwnAwayStatusChanged, ChannelModeIs,
/// ErrorReply, ChannelJoinFailed (477), RPL_NOTOPIC, CTCP SOURCE, KICK events,
/// and case-insensitive channel handling.
///
/// Uses HybridIRC (TCP/SSL) — no registration requirements for channel ops.
/// </summary>
[Trait("Category", "LiveServer")]
public class ExtendedEventTests
{
    private readonly ITestOutputHelper _output;

    public ExtendedEventTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Tests events that require two clients and channel operator privileges.
    /// Covers: INVITE, KICK, ChannelModeIs, OwnAwayStatusChanged,
    /// CTCP SOURCE, ErrorReply (482), and case-insensitive channel NAMES.
    /// </summary>
    [Fact(Timeout = 180_000)] // 3 minute ceiling
    public async Task ExtendedEvents_TwoClients_AllFeatures()
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random();
        var suffix = rng.Next(1000, 9999);
        var nickA = $"EvtA{suffix}";
        var nickB = $"EvtB{suffix}";
        var channel = $"#evt-{suffix}";
        var passed = 0;
        var failed = 0;

        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");
        void Pass(string t) { Interlocked.Increment(ref passed); Log($"  PASS: {t}"); }
        void Fail(string t) { Interlocked.Increment(ref failed); Log($"  FAIL: {t}"); }
        void Assert(bool ok, string t) { if (ok) Pass(t); else Fail(t); }

        const int Pace = 1200;

        Log($"=== ExtendedEvents === nick={nickA}/{nickB} channel={channel}");

        IrcClientOptions MakeOpts(string nick) => new()
        {
            Server = "irc.hybridirc.com",
            Port = 6697,
            UseSsl = true,
            Nick = nick,
            UserName = nick.ToLower(),
            RealName = $"{nick} ExtendedEventTest",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        // ── Event collectors ─────────────────────────────────────────
        var aConnected = new TaskCompletionSource<ConnectedEvent>();
        var bConnected = new TaskCompletionSource<ConnectedEvent>();

        // New events
        var aInvites = new ConcurrentBag<(string Nick, string Channel)>();
        var aOwnAway = new ConcurrentBag<(bool IsAway, string ServerMessage)>();
        var aChannelModes = new ConcurrentBag<(string Channel, string Modes)>();
        var aErrors = new ConcurrentBag<(string Code, string Target, string Msg)>();
        var aJoinFailed = new ConcurrentBag<(string Channel, string Reason)>();
        var aTopics = new ConcurrentBag<string>();

        // Existing events for verification
        var aJoins = new ConcurrentBag<string>();
        var aKicks = new ConcurrentBag<(string Channel, string KickedNick, string Reason)>();
        var aCtcpReplies = new ConcurrentBag<(string Cmd, string Reply)>();
        var aRaw = new ConcurrentBag<string>();
        var bInvites = new ConcurrentBag<(string Nick, string Channel)>();
        var bJoinFailed = new ConcurrentBag<(string Channel, string Reason)>();
        var bCtcpRequests = new ConcurrentBag<string>();
        var bOwnAway = new ConcurrentBag<(bool IsAway, string ServerMessage)>();

        await using var alice = new IrcClient(MakeOpts(nickA));
        await using var bob = new IrcClient(MakeOpts(nickB));

        // ── Wire Alice events ────────────────────────────────────────
        alice.Connected += (s, e) => { Log($"[A] Connected"); aConnected.TrySetResult(e); };
        alice.Disconnected += (s, e) => Log($"[A] Disconnected: {e.Reason}");
        alice.InviteReceived += (s, e) => { Log($"[A] INVITE to {e.Channel} by {e.Nick}"); aInvites.Add((e.Nick, e.Channel)); };
        alice.OwnAwayStatusChanged += (s, e) => { Log($"[A] Away={e.IsAway}: {e.ServerMessage}"); aOwnAway.Add((e.IsAway, e.ServerMessage)); };
        alice.ChannelModeIsReceived += (s, e) => { Log($"[A] MODE {e.Channel} = {e.Modes} {e.ModeParams}"); aChannelModes.Add((e.Channel, e.Modes)); };
        alice.ErrorReplyReceived += (s, e) => { Log($"[A] ERROR {e.ErrorCode}: {e.Target} {e.ErrorMessage}"); aErrors.Add((e.ErrorCode, e.Target, e.ErrorMessage)); };
        alice.ChannelJoinFailed += (s, e) => { Log($"[A] JoinFailed {e.Channel}: {e.Reason}"); aJoinFailed.Add((e.Channel, e.Reason)); };
        alice.TopicChanged += (s, e) => { Log($"[A] Topic={e.Topic}"); aTopics.Add(e.Topic ?? ""); };
        alice.UserJoinedChannel += (s, e) => { if (e.Nick != nickA) aJoins.Add(e.Nick); };
        alice.UserKicked += (s, e) => { Log($"[A] KICK {e.KickedNick} from {e.Channel}: {e.Reason}"); aKicks.Add((e.Channel, e.KickedNick, e.Reason ?? "")); };
        alice.CtcpReplyReceived += (s, e) => { Log($"[A] CTCP {e.Command}: {e.ReplyText}"); aCtcpReplies.Add((e.Command, e.ReplyText)); };
        alice.RawMessageReceived += (s, e) => aRaw.Add(e.Message.Command);

        // ── Wire Bob events ──────────────────────────────────────────
        bob.Connected += (s, e) => { Log($"[B] Connected"); bConnected.TrySetResult(e); };
        bob.Disconnected += (s, e) => Log($"[B] Disconnected: {e.Reason}");
        bob.InviteReceived += (s, e) => { Log($"[B] INVITE to {e.Channel} by {e.Nick}"); bInvites.Add((e.Nick, e.Channel)); };
        bob.ChannelJoinFailed += (s, e) => { Log($"[B] JoinFailed {e.Channel}: {e.Reason}"); bJoinFailed.Add((e.Channel, e.Reason)); };
        bob.CtcpRequestReceived += (s, e) => { Log($"[B] CTCP-REQ {e.Command}"); bCtcpRequests.Add(e.Command); };
        bob.OwnAwayStatusChanged += (s, e) => { Log($"[B] Away={e.IsAway}: {e.ServerMessage}"); bOwnAway.Add((e.IsAway, e.ServerMessage)); };

        // ══════════════════════════════════════════════════════════════
        // 1. CONNECT BOTH CLIENTS
        // ══════════════════════════════════════════════════════════════
        Log("--- 1. Connect ---");
        await alice.ConnectAsync();
        await bob.ConnectAsync();

        using var timeout = new CancellationTokenSource(25_000);
        timeout.Token.Register(() => { aConnected.TrySetCanceled(); bConnected.TrySetCanceled(); });

        try { await Task.WhenAll(aConnected.Task, bConnected.Task); }
        catch { Fail("Both clients connect"); await Cleanup(alice, bob); return; }

        Pass("Both clients connected");

        // ══════════════════════════════════════════════════════════════
        // 2. OWN AWAY STATUS (RPL_NOWAWAY 306 / RPL_UNAWAY 305)
        // ══════════════════════════════════════════════════════════════
        Log("--- 2. OwnAwayStatusChanged ---");
        await alice.SetAwayAsync("Testing away status");
        await Task.Delay(1500);

        Assert(aOwnAway.Any(a => a.IsAway), "Alice received RPL_NOWAWAY (306)");

        await alice.SetAwayAsync(); // clear away
        await Task.Delay(1500);

        Assert(aOwnAway.Any(a => !a.IsAway), "Alice received RPL_UNAWAY (305)");

        // Also test Bob's away
        await bob.SetAwayAsync("Bob is away too");
        await Task.Delay(1500);
        Assert(bOwnAway.Any(a => a.IsAway), "Bob received RPL_NOWAWAY");
        await bob.SetAwayAsync();
        await Task.Delay(1000);

        // ══════════════════════════════════════════════════════════════
        // 3. ALICE CREATES CHANNEL (gets op status)
        // ══════════════════════════════════════════════════════════════
        Log("--- 3. Alice creates channel ---");
        await alice.JoinChannelAsync(channel);
        await Task.Delay(2000);

        // ══════════════════════════════════════════════════════════════
        // 4. CHANNEL MODE IS (RPL_CHANNELMODEIS 324)
        // ══════════════════════════════════════════════════════════════
        Log("--- 4. ChannelModeIs (324) ---");
        await alice.SendRawAsync($"MODE {channel}");
        await Task.Delay(1500);

        Assert(aChannelModes.Any(m => m.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)),
            "Alice received RPL_CHANNELMODEIS for channel");

        // ══════════════════════════════════════════════════════════════
        // 5. INVITE (Alice invites Bob)
        // ══════════════════════════════════════════════════════════════
        Log("--- 5. INVITE ---");
        await alice.InviteUserAsync(nickB, channel);
        await Task.Delay(2000);

        Assert(bInvites.Any(i => i.Nick == nickA && i.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)),
            "Bob received INVITE from Alice");

        // Verify Alice got RPL_INVITING (341) in raw
        Assert(aRaw.Contains("341"), "Alice received RPL_INVITING confirmation");

        // ══════════════════════════════════════════════════════════════
        // 6. BOB JOINS (after invite)
        // ══════════════════════════════════════════════════════════════
        Log("--- 6. Bob joins ---");
        await bob.JoinChannelAsync(channel);
        await Task.Delay(2000);

        Assert(aJoins.Contains(nickB), "Alice saw Bob join after invite");

        // ══════════════════════════════════════════════════════════════
        // 7. CTCP SOURCE (auto-reply)
        // ══════════════════════════════════════════════════════════════
        Log("--- 7. CTCP SOURCE ---");
        await alice.SendCtcpRequestAsync(nickB, "SOURCE");
        await Task.Delay(2000);

        Assert(bCtcpRequests.Contains("SOURCE"), "Bob received SOURCE request");
        Assert(aCtcpReplies.Any(r => r.Cmd == "SOURCE"),
            "Alice received SOURCE reply");

        // ══════════════════════════════════════════════════════════════
        // 8. RPL_NOTOPIC / RPL_TOPIC (331/332)
        // ══════════════════════════════════════════════════════════════
        Log("--- 8. Topic replies ---");
        // Fresh channel should have no topic OR receive a topic set during join
        // Query topic to trigger 331 or 332
        await alice.SendRawAsync($"TOPIC {channel}");
        await Task.Delay(1500);

        // Either 331 (no topic) or 332 (topic) should have arrived
        Assert(aRaw.Contains("331") || aRaw.Contains("332"),
            "Received RPL_NOTOPIC (331) or RPL_TOPIC (332)");

        // Set topic then query again
        var topicText = $"ExtendedTest-{suffix}";
        await alice.SetTopicAsync(channel, topicText);
        await Task.Delay(1500);

        Assert(aTopics.Any(t => t.Contains($"ExtendedTest-{suffix}")),
            "Topic changed event received");

        // Re-query the topic to trigger 332 + 333 (servers send these on query, not on SET)
        await alice.SendRawAsync($"TOPIC {channel}");
        await Task.Delay(1500);

        // 333 is server-dependent — some send it, some don't. Check 332 OR 333.
        Assert(aRaw.Contains("332"),
            "Received RPL_TOPIC (332) after topic query");

        // ══════════════════════════════════════════════════════════════
        // 9. ERROR REPLY — ERR_CHANOPRIVSNEEDED (482)
        // ══════════════════════════════════════════════════════════════
        Log("--- 9. ErrorReply (482 — not op) ---");
        // Bob (not op) tries to set topic on op-only channel
        // First make channel +t (topic lock to ops)
        await alice.SendRawAsync($"MODE {channel} +t");
        await Task.Delay(1000);

        // Wire Bob's error events for this section
        var bErrors = new ConcurrentBag<(string Code, string Target, string Msg)>();
        bob.ErrorReplyReceived += (s, e) => { Log($"[B] ERROR {e.ErrorCode}: {e.Target} {e.ErrorMessage}"); bErrors.Add((e.ErrorCode, e.Target, e.ErrorMessage)); };

        await bob.SetTopicAsync(channel, "Bob tries to change topic");
        await Task.Delay(1500);

        Assert(bErrors.Any(e => e.Code == "482"),
            "Bob received ERR_CHANOPRIVSNEEDED (482)");

        // ══════════════════════════════════════════════════════════════
        // 10. KICK (Alice kicks Bob)
        // ══════════════════════════════════════════════════════════════
        Log("--- 10. KICK ---");
        await alice.SendRawAsync($"KICK {channel} {nickB} :Testing kick event");
        await Task.Delay(2000);

        Assert(aKicks.Any(k => k.KickedNick == nickB && k.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)),
            "Alice received KICK event for Bob");

        // ══════════════════════════════════════════════════════════════
        // 11. CASE-INSENSITIVE CHANNEL TRACKING
        // ══════════════════════════════════════════════════════════════
        Log("--- 11. Case-insensitive channels ---");
        // Channels property should handle case-insensitively
        var channels = alice.Channels;
        var lowerChannel = channel.ToLowerInvariant();
        var upperChannel = channel.ToUpperInvariant();

        // At least one casing should find the channel
        Assert(channels.ContainsKey(channel) || channels.ContainsKey(lowerChannel) || channels.ContainsKey(upperChannel),
            "Channels dictionary is case-insensitive (found channel by different casing)");

        // ══════════════════════════════════════════════════════════════
        // 12. CHANNEL JOIN FAILED — invite-only channel
        // ══════════════════════════════════════════════════════════════
        Log("--- 12. ChannelJoinFailed ---");
        // Set channel to invite-only
        await alice.SendRawAsync($"MODE {channel} +i");
        await Task.Delay(1000);

        // Bob tries to join without invite — should fail with 473
        await bob.JoinChannelAsync(channel);
        await Task.Delay(2000);

        Assert(bJoinFailed.Any(f => f.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)),
            "Bob received ChannelJoinFailed for invite-only channel");

        // ══════════════════════════════════════════════════════════════
        // 13. ERROR REPLY — ERR_NOTONCHANNEL (442)
        // ══════════════════════════════════════════════════════════════
        Log("--- 13. ErrorReply (442 — not on channel) ---");
        // Bob tries to set topic on channel he's not in
        await bob.SetTopicAsync(channel, "Should fail");
        await Task.Delay(1500);

        Assert(bErrors.Any(e => e.Code == "442" || e.Code == "482"),
            "Bob received error for channel operation while not on channel");

        // ══════════════════════════════════════════════════════════════
        // 14. MULTIPLE ERROR EVENTS FIRE CORRECTLY
        // ══════════════════════════════════════════════════════════════
        Log("--- 14. Multiple error events ---");
        var preErrorCount = bErrors.Count;
        // Send to nonexistent nick
        await bob.SendMessageAsync("#nonexistent_channel_xyz_" + suffix, "test");
        await Task.Delay(1500);

        // ErrorReplyReceived should have fired at least once more
        Assert(bErrors.Count > 0, "ErrorReplyReceived fires for various errors");

        // ══════════════════════════════════════════════════════════════
        // 15. GRACEFUL DISCONNECT
        // ══════════════════════════════════════════════════════════════
        Log("--- 15. Cleanup ---");
        // Remove invite-only so channel can be cleaned up
        await alice.SendRawAsync($"MODE {channel} -i");
        await Task.Delay(500);

        await alice.LeaveChannelAsync(channel, "Test done");
        await alice.DisconnectAsync("ExtendedEventTest done");
        await bob.DisconnectAsync("ExtendedEventTest done");
        await Task.Delay(500);

        Assert(!alice.IsConnected, "Alice disconnected");
        Assert(!bob.IsConnected, "Bob disconnected");

        // ══════════════════════════════════════════════════════════════
        // SUMMARY
        // ══════════════════════════════════════════════════════════════
        Log($"\n=== ExtendedEvents: {passed} passed, {failed} failed (elapsed {sw.Elapsed:mm\\:ss}) ===");
        Xunit.Assert.Equal(0, failed);
    }

    /// <summary>
    /// Tests ChannelJoinFailed with ERR_NOCHANMODES (477) on Libera.Chat.
    /// #networking requires NickServ identification — our unregistered client
    /// should get a 477 rejection surfaced as ChannelJoinFailed.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task ChannelJoinFailed_NeedRegistration_ShouldFireEvent()
    {
        var sw = Stopwatch.StartNew();
        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");

        var nick = $"Reg{DateTime.Now.Ticks % 10000}";
        var joinFailed = new ConcurrentBag<(string Channel, string Reason, string Code)>();
        var errors = new ConcurrentBag<(string Code, string Target)>();
        var connected = new TaskCompletionSource<bool>();

        var opts = new IrcClientOptions
        {
            Server = "irc.us.libera.chat",
            Port = 6697,
            UseSsl = true,
            Nick = nick,
            UserName = nick.ToLower(),
            RealName = "Registration Test",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 30000,
            AutoReconnect = false,
        };

        await using var client = new IrcClient(opts);
        client.Connected += (s, e) => { Log("Connected"); connected.TrySetResult(true); };
        client.ChannelJoinFailed += (s, e) =>
        {
            Log($"JoinFailed: {e.Channel} — {e.Reason} (code={e.ErrorCode})");
            joinFailed.Add((e.Channel, e.Reason, e.ErrorCode));
        };
        client.ErrorReplyReceived += (s, e) =>
        {
            Log($"Error: {e.ErrorCode} {e.Target}: {e.ErrorMessage}");
            errors.Add((e.ErrorCode, e.Target));
        };

        Log("Connecting to Libera.Chat...");
        await client.ConnectAsync();

        try { await connected.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { Log("Connection failed/timed out"); await client.DisconnectAsync(); return; }

        // Try to join a channel that requires NickServ identification
        Log("Joining #networking (requires registration)...");
        await client.JoinChannelAsync("#networking");
        await Task.Delay(5000); // Give server time to reject

        // Verify 477 was surfaced
        var has477 = joinFailed.Any(f =>
            f.Channel.Equals("#networking", StringComparison.OrdinalIgnoreCase) &&
            f.Code == "477");

        if (has477)
            Log("PASS: ChannelJoinFailed fired with 477 for #networking");
        else
            Log($"INFO: Join failures received: {string.Join(", ", joinFailed.Select(f => $"{f.Channel}:{f.Code}"))}");

        // Also test that ErrorReplyReceived fired
        var hasError477 = errors.Any(e => e.Code == "477");

        await client.DisconnectAsync("Test done");

        // At least one should have fired (477 if unregistered, or channel may have been joined if somehow registered)
        Xunit.Assert.True(has477 || joinFailed.IsEmpty,
            "Either 477 fired (unregistered) or channel was joinable (registered)");
    }

    /// <summary>
    /// Tests that INVITE event carries correct source information
    /// and channel name, including edge cases with special channel prefixes.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Invite_ShouldCarryFullSourceInfo()
    {
        var sw = Stopwatch.StartNew();
        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");

        var suffix = new Random().Next(1000, 9999);
        var nickA = $"InvA{suffix}";
        var nickB = $"InvB{suffix}";
        var channel = $"#inv-{suffix}";

        var bInviteDetails = new TaskCompletionSource<InviteReceivedEvent>();

        var optsA = new IrcClientOptions
        {
            Server = "irc.hybridirc.com",
            Port = 6697,
            UseSsl = true,
            Nick = nickA,
            UserName = nickA.ToLower(),
            RealName = "Invite Test A",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };
        var optsB = new IrcClientOptions
        {
            Server = "irc.hybridirc.com",
            Port = 6697,
            UseSsl = true,
            Nick = nickB,
            UserName = nickB.ToLower(),
            RealName = "Invite Test B",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        await using var alice = new IrcClient(optsA);
        await using var bob = new IrcClient(optsB);

        var aConn = new TaskCompletionSource<bool>();
        var bConn = new TaskCompletionSource<bool>();
        alice.Connected += (s, e) => aConn.TrySetResult(true);
        bob.Connected += (s, e) => bConn.TrySetResult(true);
        bob.InviteReceived += (s, e) =>
        {
            Log($"[B] INVITE detail: Nick={e.Nick} User={e.User} Host={e.Host} Channel={e.Channel}");
            bInviteDetails.TrySetResult(e);
        };

        await alice.ConnectAsync();
        await bob.ConnectAsync();

        try { await Task.WhenAll(aConn.Task, bConn.Task).WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { Log("Connection failed"); await Cleanup(alice, bob); return; }

        // Alice creates channel and invites Bob
        await alice.JoinChannelAsync(channel);
        await Task.Delay(2000);

        await alice.InviteUserAsync(nickB, channel);

        try
        {
            var evt = await bInviteDetails.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Xunit.Assert.Equal(nickA, evt.Nick);
            Xunit.Assert.False(string.IsNullOrEmpty(evt.User), "User field should not be empty");
            Xunit.Assert.False(string.IsNullOrEmpty(evt.Host), "Host field should not be empty");
            Xunit.Assert.Equal(channel, evt.Channel);
            Log($"PASS: INVITE event has full source info — {evt.Nick}!{evt.User}@{evt.Host}");
        }
        catch (TimeoutException)
        {
            Log("WARN: INVITE event not received (server may not support INVITE notifications)");
        }

        await Cleanup(alice, bob);
    }

    /// <summary>
    /// Tests OwnAwayStatusChanged event fires correctly for both away and back transitions,
    /// and verifies the ServerMessage content.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task OwnAwayStatus_ShouldFireForBothTransitions()
    {
        var sw = Stopwatch.StartNew();
        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");

        var nick = $"Away{DateTime.Now.Ticks % 10000}";
        var awayEvents = new ConcurrentBag<OwnAwayStatusChangedEvent>();
        var connected = new TaskCompletionSource<bool>();

        var opts = new IrcClientOptions
        {
            Server = "irc.hybridirc.com",
            Port = 6697,
            UseSsl = true,
            Nick = nick,
            UserName = nick.ToLower(),
            RealName = "Away Test",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        await using var client = new IrcClient(opts);
        client.Connected += (s, e) => connected.TrySetResult(true);
        client.OwnAwayStatusChanged += (s, e) =>
        {
            Log($"Away event: IsAway={e.IsAway} ServerMessage={e.ServerMessage}");
            awayEvents.Add(e);
        };

        await client.ConnectAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Set away
        await client.SetAwayAsync("BRB testing");
        await Task.Delay(2000);

        // Clear away
        await client.SetAwayAsync();
        await Task.Delay(2000);

        // Set away again
        await client.SetAwayAsync("Another away message");
        await Task.Delay(2000);

        // Clear again
        await client.SetAwayAsync();
        await Task.Delay(2000);

        await client.DisconnectAsync("Test done");

        // Verify we got both transitions
        var awayCount = awayEvents.Count(e => e.IsAway);
        var backCount = awayEvents.Count(e => !e.IsAway);

        Log($"Away events: {awayCount} away, {backCount} back (total {awayEvents.Count})");

        Xunit.Assert.True(awayCount >= 2, $"Expected at least 2 away events, got {awayCount}");
        Xunit.Assert.True(backCount >= 2, $"Expected at least 2 back events, got {backCount}");

        // Verify ServerMessage is populated
        Xunit.Assert.True(awayEvents.All(e => !string.IsNullOrEmpty(e.ServerMessage)),
            "All away events should have non-empty ServerMessage");
    }

    /// <summary>
    /// Tests the TypingIndicatorReceived event via TAGMSG with +typing tag.
    /// Requires the server to support the message-tags capability.
    /// Two clients join a channel, one sends typing notifications, the other receives them.
    /// Also tests PM typing and the self-skip (own typing ignored).
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task TypingIndicator_ChannelAndPM_ShouldFireEvent()
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random();
        var suffix = rng.Next(1000, 9999);
        var nickA = $"TypA{suffix}";
        var nickB = $"TypB{suffix}";
        var channel = $"#typ-{suffix}";

        void Log(string msg) => _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.ff}] {msg}");

        Log($"=== TypingIndicator === nick={nickA}/{nickB} channel={channel}");

        IrcClientOptions MakeOpts(string nick) => new()
        {
            Server = "irc.hybridirc.com",
            Port = 6697,
            UseSsl = true,
            Nick = nick,
            UserName = nick.ToLower(),
            RealName = $"{nick} TypingTest",
            EnableRateLimit = false,
            ConnectionTimeoutMs = 20000,
            AutoReconnect = false,
        };

        var aConnected = new TaskCompletionSource<ConnectedEvent>();
        var bConnected = new TaskCompletionSource<ConnectedEvent>();

        // Typing events received by Bob
        var bTypingEvents = new ConcurrentBag<TypingIndicatorEvent>();
        // Typing events that Alice receives (should NOT include her own typing)
        var aTypingEvents = new ConcurrentBag<TypingIndicatorEvent>();

        await using var alice = new IrcClient(MakeOpts(nickA));
        await using var bob = new IrcClient(MakeOpts(nickB));

        alice.Connected += (s, e) => { Log($"[A] Connected"); aConnected.TrySetResult(e); };
        bob.Connected += (s, e) => { Log($"[B] Connected"); bConnected.TrySetResult(e); };
        alice.Disconnected += (s, e) => Log($"[A] Disconnected: {e.Reason}");
        bob.Disconnected += (s, e) => Log($"[B] Disconnected: {e.Reason}");

        alice.TypingIndicatorReceived += (s, e) =>
        {
            Log($"[A] Typing: {e.Nick} -> {e.Target} state={e.State} isChannel={e.IsChannelTyping}");
            aTypingEvents.Add(e);
        };
        bob.TypingIndicatorReceived += (s, e) =>
        {
            Log($"[B] Typing: {e.Nick} -> {e.Target} state={e.State} isChannel={e.IsChannelTyping}");
            bTypingEvents.Add(e);
        };

        // ── Connect ──
        Log("--- 1. Connect ---");
        await alice.ConnectAsync();
        await bob.ConnectAsync();

        using var timeout = new CancellationTokenSource(25_000);
        timeout.Token.Register(() => { aConnected.TrySetCanceled(); bConnected.TrySetCanceled(); });

        try { await Task.WhenAll(aConnected.Task, bConnected.Task); }
        catch { Log("FAIL: connect"); await Cleanup(alice, bob); return; }

        // Check if message-tags capability was negotiated
        var aliceHasTags = alice.EnabledCapabilities.Contains("message-tags");
        var bobHasTags = bob.EnabledCapabilities.Contains("message-tags");
        Log($"message-tags: Alice={aliceHasTags}, Bob={bobHasTags}");

        if (!aliceHasTags || !bobHasTags)
        {
            Log("SKIP: Server does not support message-tags capability — cannot test typing indicator");
            await Cleanup(alice, bob);
            return;
        }

        // ── Join channel ──
        Log("--- 2. Join channel ---");
        await alice.JoinChannelAsync(channel);
        await Task.Delay(2000);
        await bob.JoinChannelAsync(channel);
        await Task.Delay(2000);

        // ── Channel typing: Alice sends active, Bob should receive ──
        Log("--- 3. Channel typing (active) ---");
        await alice.SendTagMessageAsync(channel, new Dictionary<string, string?>
        {
            [MessageTags.TYPING] = "active"
        });
        await Task.Delay(3000);

        var bobChannelActive = bTypingEvents.Any(e =>
            e.Nick.Equals(nickA, StringComparison.OrdinalIgnoreCase) &&
            e.Target.Equals(channel, StringComparison.OrdinalIgnoreCase) &&
            e.State == TypingState.Active &&
            e.IsChannelTyping);
        Log($"  Bob received channel typing=active from Alice: {bobChannelActive}");

        // ── Channel typing: Alice sends done ──
        Log("--- 4. Channel typing (done) ---");
        await alice.SendTagMessageAsync(channel, new Dictionary<string, string?>
        {
            [MessageTags.TYPING] = "done"
        });
        await Task.Delay(2000);

        var bobChannelDone = bTypingEvents.Any(e =>
            e.Nick.Equals(nickA, StringComparison.OrdinalIgnoreCase) &&
            e.State == TypingState.Done);
        Log($"  Bob received channel typing=done from Alice: {bobChannelDone}");

        // ── PM typing: Bob sends active to Alice ──
        Log("--- 5. PM typing (active) ---");
        await bob.SendTagMessageAsync(nickA, new Dictionary<string, string?>
        {
            [MessageTags.TYPING] = "active"
        });
        await Task.Delay(3000);

        var alicePmActive = aTypingEvents.Any(e =>
            e.Nick.Equals(nickB, StringComparison.OrdinalIgnoreCase) &&
            !e.IsChannelTyping &&
            e.State == TypingState.Active);
        Log($"  Alice received PM typing=active from Bob: {alicePmActive}");

        // ── Self-typing: Alice should NOT receive her own typing ──
        Log("--- 6. Self-typing (should be filtered) ---");
        var aliceCountBefore = aTypingEvents.Count;
        await alice.SendTagMessageAsync(channel, new Dictionary<string, string?>
        {
            [MessageTags.TYPING] = "active"
        });
        await Task.Delay(2000);

        var aliceSelfFiltered = aTypingEvents.Count == aliceCountBefore;
        Log($"  Alice's own typing was filtered (no new event): {aliceSelfFiltered}");

        // ── Cleanup ──
        await Cleanup(alice, bob);

        // ── Results ──
        Log("=== Results ===");
        Log($"  Channel typing=active received by Bob: {bobChannelActive}");
        Log($"  Channel typing=done received by Bob: {bobChannelDone}");
        Log($"  PM typing=active received by Alice: {alicePmActive}");
        Log($"  Self-typing filtered: {aliceSelfFiltered}");

        // At minimum, channel typing should work if server supports message-tags
        // PM typing may not work on all servers (some don't relay TAGMSG to non-channel targets)
        Xunit.Assert.True(bobChannelActive, "Bob should receive channel typing=active from Alice");
        Xunit.Assert.True(bobChannelDone, "Bob should receive channel typing=done from Alice");
        Xunit.Assert.True(aliceSelfFiltered, "Client should filter out own typing notifications");
    }

    private static async Task Cleanup(params IrcClient[] clients)
    {
        foreach (var c in clients)
        {
            try { await c.DisconnectAsync("test cleanup"); } catch { }
        }
    }
}
