using System.Collections;
using System.Reflection;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using IRCDotNet.Core.Transport;
using IRCDotNet.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace IRCDotNet.Tests;

/// <summary>
/// Behavioural regression tests for the fixes applied after the post-2.5.1 review pass:
/// MONITOR list-full handling, stray offline suppression, synthetic-quit correlation,
/// bounded 433 retries, NICKLEN-aware fallback nicks, echoed CTCP-reply surfacing,
/// ISUPPORT reset on disconnect, phantom-channel prevention and join-failure cleanup.
/// </summary>
public sealed class IrcClientReviewRemediationTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly MethodInfo _processMessageAsync;

    public IrcClientReviewRemediationTests()
    {
        _client = CreateClient();
        _processMessageAsync = typeof(IrcClient).GetMethod("ProcessMessageAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find ProcessMessageAsync via reflection.");
    }

    // ---- MON-2: ERR_MONLISTFULL (734) ---------------------------------------------------------

    [Fact]
    public async Task MonitorListFull_RemovesRejectedTargets_AndRaisesErrorReply()
    {
        await RegisterAsync("me");
        AddEnabledCapability(IrcCapabilities.MONITOR);
        AddMonitoredNick("Alice");
        AddMonitoredNick("Bob");
        AddMonitoredNick("Carol");

        var errors = new List<ErrorReplyEvent>();
        _client.ErrorReplyReceived += (_, e) => errors.Add(e);

        // ":server 734 me 5 Bob,Carol :Monitor list is full."
        await ProcessAsync($":server {IrcNumericReplies.ERR_MONLISTFULL} me 5 Bob,Carol :Monitor list is full.");

        var monitored = GetMonitoredNicks();
        monitored.Should().Contain("Alice", "Alice was accepted and must remain monitored");
        monitored.Should().NotContain("Bob");
        monitored.Should().NotContain("Carol");

        await WaitUntilAsync(() => errors.Count > 0, TimeSpan.FromSeconds(3));
        errors.Should().ContainSingle();
        errors[0].ErrorCode.Should().Be(IrcNumericReplies.ERR_MONLISTFULL);
        errors[0].Target.Should().Be("Bob,Carol");
    }

    // ---- MON-3: stray RPL_MONOFFLINE for an unmonitored nick ----------------------------------

    [Fact]
    public async Task MonitorOffline_ForUnmonitoredNick_DoesNotRaiseSyntheticQuit()
    {
        // Use a SHORT correlation window so that an erroneously scheduled finalizer would already
        // have fired by the time we assert — otherwise (with the default 750ms window) the test would
        // pass even if a stray quit were merely still pending behind the window.
        using var client = CreateClient(correlationWindowMs: 60);
        await RegisterAsync("me", client: client);
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        // Deliberately NOT monitoring "Stranger".

        var quits = new List<UserQuitEvent>();
        client.UserQuit += (_, e) => quits.Add(e);

        await ProcessAsync($":server {IrcNumericReplies.RPL_MONOFFLINE} me :Stranger", client);

        // Wait well beyond the (short) correlation window: a wrongly scheduled finalizer fires at ~60ms.
        await Task.Delay(250);
        quits.Should().BeEmpty("an offline notice for a nick we never monitored must not manufacture a quit");
    }

    // ---- MON-1 / synthetic quit: genuine offline for a monitored nick -------------------------

    [Fact]
    public async Task MonitorOffline_ForMonitoredNick_RaisesSyntheticUserQuit()
    {
        using var client = CreateClient(correlationWindowMs: 60);
        await RegisterAsync("me", client: client);
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        AddMonitoredNick("Bob", client);

        var quits = new List<UserQuitEvent>();
        client.UserQuit += (_, e) => quits.Add(e);

        await ProcessAsync($":server {IrcNumericReplies.RPL_MONOFFLINE} me :Bob", client);

        await WaitUntilAsync(() => quits.Count > 0, TimeSpan.FromSeconds(3));
        quits.Should().ContainSingle();
        quits[0].Nick.Should().Be("Bob");
        quits[0].IsSynthetic.Should().BeTrue("a MONITOR-derived quit must be flagged so consumers can distinguish it from a real QUIT");
    }

    // ---- MON-1 correlation: offline then online inside the window -----------------------------

    [Fact]
    public async Task MonitorOffline_ThenOnlineWithinWindow_SuppressesSyntheticQuit()
    {
        using var client = CreateClient(correlationWindowMs: 300);
        await RegisterAsync("me", client: client);
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        AddMonitoredNick("Bob", client);

        var quits = new List<UserQuitEvent>();
        client.UserQuit += (_, e) => quits.Add(e);

        // A flap: offline immediately followed by online before the correlation window elapses.
        await ProcessAsync($":server {IrcNumericReplies.RPL_MONOFFLINE} me :Bob", client);
        await ProcessAsync($":server {IrcNumericReplies.RPL_MONONLINE} me :Bob!u@h", client);

        await Task.Delay(600); // > correlation window
        quits.Should().BeEmpty("the user came back online within the correlation window, so no quit should fire");
    }

    // ---- M2: bounded 433 retries --------------------------------------------------------------

    [Fact]
    public async Task NicknameInUse_WithNoAlternatives_DisconnectsAfterMaxRetries()
    {
        using var client = CreateClient();
        var transport = new RecordingTransport();
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);
        SetPrivateField(client, "_currentNick", "testbot");

        var errors = new List<ErrorReplyEvent>();
        client.ErrorReplyReceived += (_, e) => errors.Add(e);

        // MaxNickRetries is 5; the 6th rejection must abort registration rather than retry forever.
        for (var i = 0; i < 6; i++)
        {
            await ProcessAsync(":server 433 * testbot :Nickname is already in use.", client);
        }

        await WaitUntilAsync(() => errors.Count > 0, TimeSpan.FromSeconds(3));
        errors.Should().Contain(e => e.ErrorCode == "433" && e.ErrorMessage.Contains("Exhausted", StringComparison.Ordinal));
        GetPrivateField<bool>(client, "_isConnected").Should().BeFalse("the client must disconnect once retries are exhausted");

        var nickRetries = transport.WrittenLines.Count(l => l.StartsWith("NICK ", StringComparison.Ordinal));
        nickRetries.Should().BeLessThanOrEqualTo(5, "no more than MaxNickRetries NICK attempts may be emitted");
    }

    // ---- Min5: fallback nick respects advertised NICKLEN --------------------------------------

    [Fact]
    public async Task NicknameInUse_FallbackNick_RespectsAdvertisedNickLength()
    {
        using var client = CreateClient(nick: "VeryLongBotName");
        var transport = new RecordingTransport();
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);
        SetPrivateField(client, "_currentNick", "VeryLongBotName");

        // Server advertises a short NICKLEN; the generated fallback must not exceed it.
        await ProcessAsync($":server {IrcNumericReplies.RPL_ISUPPORT} VeryLongBotName NICKLEN=9 :are supported by this server", client);
        await ProcessAsync(":server 433 * VeryLongBotName :Nickname is already in use.", client);

        await WaitUntilAsync(
            () => transport.WrittenLines.Any(l => l.StartsWith("NICK ", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));

        var nick = client.CurrentNick;
        nick.Length.Should().BeLessThanOrEqualTo(9, "the fallback nick must fit within the server's advertised NICKLEN");
    }

    // ---- S-3: echoed CTCP reply is surfaced with IsEcho=true ----------------------------------

    [Fact]
    public async Task EchoedCtcpReply_IsSurfaced_WithIsEchoTrue()
    {
        await RegisterAsync("me");
        AddEnabledCapability("echo-message");

        var replies = new List<CtcpReplyEvent>();
        _client.CtcpReplyReceived += (_, e) => replies.Add(e);

        // Our own CTCP PING reply, echoed back to us by the server (echo-message capability).
        await ProcessAsync(":me!u@h NOTICE #chan :\u0001PING 1234567890\u0001");

        await WaitUntilAsync(() => replies.Count > 0, TimeSpan.FromSeconds(3));
        replies.Should().ContainSingle();
        replies[0].Command.Should().Be("PING");
        replies[0].IsEcho.Should().BeTrue("an echoed CTCP reply must be surfaced but flagged as an echo");
    }

    // ---- I-1: ISUPPORT tokens reset on disconnect ---------------------------------------------

    [Fact]
    public async Task Isupport_CaseMapping_ResetsToDefault_AfterDisconnect()
    {
        await RegisterAsync("me");
        await ProcessAsync($":server {IrcNumericReplies.RPL_ISUPPORT} me CASEMAPPING=ascii :are supported by this server");
        GetServerCaseMapping().Should().Be(CaseMappingType.Ascii);

        await InvokePrivateMethodAsync(_client, "DisconnectInternalAsync", "test reset");

        GetServerCaseMapping().Should().Be(CaseMappingType.Rfc1459,
            "negotiated ISUPPORT tokens must reset on disconnect so a stale value cannot leak into the next connection");
    }

    // ---- Med4: self-part between NAMES and ENDOFNAMES must not resurrect the channel ----------

    [Fact]
    public async Task SelfPartBetweenNamesAndEndOfNames_DoesNotResurrectChannel()
    {
        await RegisterAsync("me");

        var channelUserEvents = new List<ChannelUsersEvent>();
        _client.ChannelUsersReceived += (_, e) => channelUserEvents.Add(e);

        await ProcessAsync(":me!u@h JOIN #chan");
        await ProcessAsync(":server 353 me = #chan :me Alice Bob");
        await ProcessAsync(":me!u@h PART #chan");                 // we leave before ENDOFNAMES
        await ProcessAsync(":server 366 me #chan :End of /NAMES list.");

        _client.Channels.Should().NotContainKey("#chan", "a channel we have left must not be resurrected by a late ENDOFNAMES");
        PendingNamesContains("#chan").Should().BeFalse();
        channelUserEvents.Should().NotContain(e => string.Equals(e.Channel, "#chan", StringComparison.OrdinalIgnoreCase));
    }

    // ---- N-1: join failure clears the pending NAMES state -------------------------------------

    [Fact]
    public async Task JoinFailure_ClearsPendingNamesState()
    {
        await RegisterAsync("me");

        await ProcessAsync(":me!u@h JOIN #secret");
        PendingNamesContains("#secret").Should().BeTrue("a self-join pre-creates the pending NAMES buffer");

        // 477 ERR_NOCHANMODES — Libera/Solanum "you must identify with services" join rejection.
        await ProcessAsync($":server {IrcNumericReplies.ERR_NOCHANMODES} me #secret :You need to be identified with services");

        PendingNamesContains("#secret").Should().BeFalse("a failed join must not leave an orphaned pending NAMES buffer");
        _client.Channels.Should().NotContainKey("#secret");
    }

    // ---- P1: reconnect must abort once disposal is requested (not merely once it completes) ----

    [Fact]
    public async Task ReconnectScheduledBeforeDispose_AbortsPromptly_WithoutReopening()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "me",
            UserName = "me",
            RealName = "me",
            ReconnectDelayMs = 30000,
            MaxReconnectDelayMs = 30000,
            MaxReconnectAttempts = 5
        };
        var client = new IrcClient(options, NullLogger<IrcClient>.Instance);

        // Kick off a reconnect attempt; it parks on the long backoff delay.
        var attemptMethod = typeof(IrcClient).GetMethod("AttemptReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var attempt = (Task)attemptMethod.Invoke(client, null)!;

        await Task.Delay(100);
        attempt.IsCompleted.Should().BeFalse("the reconnect should still be parked on its backoff delay");

        // DisposeAsync sets _disposeRequested immediately and must cancel the pending reconnect delay
        // rather than letting it wake later and reopen the transport mid-disposal.
        await client.DisposeAsync();

        var finished = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.Should().BeSameAs(attempt, "disposal must cancel the pending reconnect backoff delay instead of waiting it out");
        client.IsConnected.Should().BeFalse("a reconnect scheduled before disposal must not reopen the connection");
    }

    // ---- P2: MonitorNickAsync must register the nick BEFORE sending MONITOR + ------------------

    [Fact]
    public async Task MonitorNickAsync_AddsToMonitoredSet_BeforeWritingMonitorPlus()
    {
        using var client = CreateClient();
        AddEnabledCapability(IrcCapabilities.MONITOR, client);

        var monitoredAtWriteTime = false;
        var transport = new RecordingTransport(line =>
        {
            if (line.StartsWith("MONITOR + Bob", StringComparison.Ordinal))
            {
                monitoredAtWriteTime = GetMonitoredNicks(client).Contains("Bob");
            }
        });
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);

        await client.MonitorNickAsync("Bob");

        monitoredAtWriteTime.Should().BeTrue(
            "the nick must be in the monitored set before MONITOR + is written, so a fast RPL_MONOFFLINE/MONONLINE reply is not dropped by the unmonitored-offline guard");
        GetMonitoredNicks(client).Should().Contain("Bob");
    }

    // ---- P2: fallback nick must stay RFC 2812 §2.3.1-valid even for a tiny advertised NICKLEN ---

    [Fact]
    public async Task NicknameInUse_FallbackNick_StartsWithValidChar_WhenNickLenIsTiny()
    {
        using var client = CreateClient(nick: "Bot");
        var transport = new RecordingTransport();
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);
        SetPrivateField(client, "_currentNick", "Bot");

        // A pathologically small NICKLEN: the 4-digit suffix alone would fill it and start with a digit.
        await ProcessAsync($":server {IrcNumericReplies.RPL_ISUPPORT} Bot NICKLEN=4 :are supported by this server", client);
        await ProcessAsync(":server 433 * Bot :Nickname is already in use.", client);

        await WaitUntilAsync(
            () => transport.WrittenLines.Any(l => l.StartsWith("NICK ", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3));

        var nick = client.CurrentNick;
        nick.Length.Should().BeInRange(1, 4, "the fallback must fit within the advertised NICKLEN");
        IsValidNickStart(nick).Should().BeTrue(
            "RFC 2812 §2.3.1 requires a nickname to start with a letter or special character, never a digit");
    }

    private static bool IsValidNickStart(string nick) =>
        !string.IsNullOrEmpty(nick) && (char.IsLetter(nick[0]) || "[]\\`_^{|}".IndexOf(nick[0]) >= 0);

    // ---- P1: disposal must serialize with an in-flight connect (post-delay reconnect interleaving) ----

    [Fact]
    public async Task DisposeAsync_SerializesBehindInFlightConnect()
    {
        var client = CreateClient();
        var connectLock = GetPrivateField<SemaphoreSlim>(client, "_connectLock")!;

        // Simulate a reconnect worker that has passed every disposal check and is now inside ConnectAsync
        // holding the connect lock while it builds the transport.
        await connectLock.WaitAsync();

        var disposeTask = client.DisposeAsync().AsTask();

        await Task.Delay(150);
        disposeTask.IsCompleted.Should().BeFalse(
            "disposal must serialize behind an in-flight connect (via the connect lock) instead of clearing state underneath it and letting the connect reopen a live transport");

        // Releasing the lock — as a finishing or aborting ConnectAsync would — lets disposal proceed.
        connectLock.Release();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(10)));
        finished.Should().BeSameAs(disposeTask, "disposal must complete once the in-flight connect releases the connect lock");
    }

    // ---- P2: UnmonitorNickAsync must remove locally BEFORE sending MONITOR - --------------------

    [Fact]
    public async Task UnmonitorNickAsync_RemovesFromMonitoredSet_BeforeWritingMonitorMinus()
    {
        using var client = CreateClient();
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        AddMonitoredNick("Bob", client);

        var monitoredAtWriteTime = true;
        var transport = new RecordingTransport(line =>
        {
            if (line.StartsWith("MONITOR - Bob", StringComparison.Ordinal))
            {
                monitoredAtWriteTime = GetMonitoredNicks(client).Contains("Bob");
            }
        });
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);

        await client.UnmonitorNickAsync("Bob");

        monitoredAtWriteTime.Should().BeFalse(
            "the nick must leave the monitored set before MONITOR - is written, so a final RPL_MONOFFLINE reply is dropped by the unmonitored-offline guard");
        GetMonitoredNicks(client).Should().NotContain("Bob");
    }

    // ---- P2: unmonitoring within the correlation window cancels a pending synthetic quit -------

    [Fact]
    public async Task UnmonitorNick_DuringCorrelationWindow_SuppressesSyntheticQuit()
    {
        using var client = CreateClient(correlationWindowMs: 200);
        await RegisterAsync("me", client: client);
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        AddMonitoredNick("Bob", client);
        var transport = new RecordingTransport();
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_isConnected", true);

        var quits = new List<UserQuitEvent>();
        client.UserQuit += (_, e) => quits.Add(e);

        // The offline schedules a synthetic-quit finalizer behind the correlation window.
        await ProcessAsync($":server {IrcNumericReplies.RPL_MONOFFLINE} me :Bob", client);
        // The consumer stops monitoring Bob before the window elapses.
        await client.UnmonitorNickAsync("Bob");

        await Task.Delay(400); // > correlation window
        quits.Should().BeEmpty("unmonitoring a nick within the correlation window must cancel its pending synthetic quit");
    }

    // ---- P2: the offline finalizer must re-check monitoring before raising UserQuit -----------

    [Fact]
    public async Task MonitorOffline_ThenUnmonitoredViaSetRemoval_DoesNotRaiseSyntheticQuit()
    {
        using var client = CreateClient(correlationWindowMs: 150);
        await RegisterAsync("me", client: client);
        AddEnabledCapability(IrcCapabilities.MONITOR, client);
        AddMonitoredNick("Bob", client);

        var quits = new List<UserQuitEvent>();
        client.UserQuit += (_, e) => quits.Add(e);

        // The offline schedules the finalizer while Bob is still monitored (pending entry created).
        await ProcessAsync($":server {IrcNumericReplies.RPL_MONOFFLINE} me :Bob", client);
        // Model the race where monitoring is dropped AFTER the pending entry exists but the pending entry
        // itself is not cleared: the finalizer must re-check monitoring and suppress the quit.
        RemoveMonitoredNick("Bob", client);

        await Task.Delay(350); // > correlation window
        quits.Should().BeEmpty("the offline finalizer must re-check monitoring and suppress the quit for a nick that is no longer monitored");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static IrcClient CreateClient(string nick = "me", int? correlationWindowMs = null)
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = nick,
            UserName = nick,
            RealName = nick
        };

        if (correlationWindowMs.HasValue)
        {
            options.MonitorOfflineCorrelationWindowMs = correlationWindowMs.Value;
        }

        return new IrcClient(options, NullLogger<IrcClient>.Instance);
    }

    private async Task RegisterAsync(string nick, IrcClient? client = null)
    {
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_WELCOME} {nick} :Welcome", client);
    }

    private async Task ProcessAsync(string rawMessage, IrcClient? client = null)
    {
        var message = IrcMessage.Parse(rawMessage);
        await (Task)_processMessageAsync.Invoke(client ?? _client, [message])!;
    }

    private CaseMappingType GetServerCaseMapping()
    {
        var parser = typeof(IrcClient)
            .GetField("_isupportParser", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_client)!;
        var value = parser.GetType().GetProperty("CaseMapping")!.GetValue(parser)!;
        return (CaseMappingType)value;
    }

    private bool PendingNamesContains(string channel)
    {
        var dict = (IDictionary)typeof(IrcClient)
            .GetField("_pendingNamesUsers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_client)!;
        return dict.Contains(channel);
    }

    private IReadOnlyCollection<string> GetMonitoredNicks(IrcClient? client = null)
    {
        var set = (ConcurrentHashSet<string>)typeof(IrcClient)
            .GetField("_monitoredNicks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client ?? _client)!;
        return set.ToArray();
    }

    private void AddEnabledCapability(string capability, IrcClient? client = null)
    {
        var set = (ConcurrentHashSet<string>)typeof(IrcClient)
            .GetField("_enabledCapabilities", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client ?? _client)!;
        set.Add(capability);
    }

    private void AddMonitoredNick(string nick, IrcClient? client = null)
    {
        var set = (ConcurrentHashSet<string>)typeof(IrcClient)
            .GetField("_monitoredNicks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client ?? _client)!;
        set.Add(nick);
    }

    private void RemoveMonitoredNick(string nick, IrcClient? client = null)
    {
        var set = (ConcurrentHashSet<string>)typeof(IrcClient)
            .GetField("_monitoredNicks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client ?? _client)!;
        set.Remove(nick);
    }

    private static void SetPrivateField(IrcClient client, string fieldName, object? value)
    {
        typeof(IrcClient).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, value);
    }

    private static T? GetPrivateField<T>(IrcClient client, string fieldName)
    {
        return (T?)typeof(IrcClient).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client);
    }

    private static async Task InvokePrivateMethodAsync(IrcClient client, string methodName, params object?[]? arguments)
    {
        var method = typeof(IrcClient).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        if (method!.Invoke(client, arguments) is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Condition was not met within the allotted timeout.");
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Minimal in-memory transport that records every written line and completes writes immediately,
    /// so handlers that issue NICK/MONITOR/etc. can run to completion without a real socket.
    /// </summary>
    private sealed class RecordingTransport : IIrcTransport
    {
        private readonly List<string> _writtenLines = new();
        private readonly Action<string>? _onWrite;

        public RecordingTransport(Action<string>? onWrite = null)
        {
            _onWrite = onWrite;
        }

        public bool IsConnected => true;

        public IReadOnlyList<string> WrittenLines
        {
            get
            {
                lock (_writtenLines)
                {
                    return _writtenLines.ToArray();
                }
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            _onWrite?.Invoke(line);
            lock (_writtenLines)
            {
                _writtenLines.Add(line);
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
