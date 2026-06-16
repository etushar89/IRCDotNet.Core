using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using IRCDotNet.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace IRCDotNet.Tests;

/// <summary>
/// Verifies that nickname/channel identity comparisons honour the server's CASEMAPPING (RFC 2812 §1.3)
/// rather than ordinal/OrdinalIgnoreCase comparison. On an rfc1459 network the characters
/// <c>[]\^</c> are the uppercase forms of <c>{}|~</c>, so "User[]" and "user{}" are the same identity.
/// </summary>
public sealed class IrcClientCaseMappingTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly MethodInfo _processMessageAsync;

    public IrcClientCaseMappingTests()
    {
        _client = new IrcClient(
            new IrcClientOptions
            {
                Server = "irc.example.com",
                Port = 6667,
                Nick = "observer",
                UserName = "observer",
                RealName = "Observer"
            },
            NullLogger<IrcClient>.Instance);

        _processMessageAsync = typeof(IrcClient).GetMethod("ProcessMessageAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find ProcessMessageAsync via reflection.");
    }

    [Fact]
    public async Task ChannelRoster_RemovesUser_WhenQuitNickIsRfc1459CaseVariant()
    {
        await RegisterAsync("observer", caseMapping: "rfc1459");

        await ProcessAsync(":observer!observer@host JOIN #chan");
        await ProcessAsync(":User[]!u@h JOIN #chan");

        await ProcessAsync(":user{}!u@h QUIT :gone");

        var members = GetChannelMembers("#chan");
        members.Should().NotContain(n => string.Equals(n, "User[]", StringComparison.Ordinal),
            "rfc1459 folds [] to {} so the quitting user is the same identity and must be removed");
    }

    [Fact]
    public async Task ChannelRoster_RemovesUser_WhenPartNickIsRfc1459CaseVariant()
    {
        await RegisterAsync("observer", caseMapping: "rfc1459");

        await ProcessAsync(":observer!observer@host JOIN #chan");
        await ProcessAsync(":Nick\\Slash!u@h JOIN #chan");

        // rfc1459: '\\' folds to '|'
        await ProcessAsync(":nick|slash!u@h PART #chan");

        GetChannelMembers("#chan").Should().BeEmpty();
    }

    [Fact]
    public async Task UserInfo_IsNotDoubleTracked_AcrossRfc1459CaseVariants()
    {
        await RegisterAsync("observer", caseMapping: "rfc1459");

        await ProcessAsync(":User[name]!u@h PRIVMSG observer :first");
        await ProcessAsync(":user{name}!u@h PRIVMSG observer :second");

        GetUserInfoKeys().Should().HaveCount(1, "both messages are from the same rfc1459 identity");
    }

    [Fact]
    public async Task EchoMessage_IsDetected_WhenSelfNickIsRfc1459CaseVariant()
    {
        await RegisterAsync("Nick[]", caseMapping: "rfc1459");
        AddEnabledCapability("echo-message");

        var messages = new List<PrivateMessageEvent>();
        _client.PrivateMessageReceived += (_, e) => messages.Add(e);

        // Server echoes our own message back with the rfc1459-equivalent form of our nick.
        await ProcessAsync(":nick{}!observer@host PRIVMSG #chan :echoed");

        await WaitUntilAsync(() => messages.Count > 0, TimeSpan.FromSeconds(3));
        messages.Should().ContainSingle();
        messages[0].IsEcho.Should().BeTrue();
    }

    [Fact]
    public async Task AsciiCaseMapping_DoesNotFoldBrackets()
    {
        await RegisterAsync("observer", caseMapping: "ascii");

        await ProcessAsync(":observer!observer@host JOIN #chan");
        await ProcessAsync(":User[]!u@h JOIN #chan");

        // Under ascii mapping, [] does NOT fold to {}, so this is a different identity and must NOT remove User[].
        await ProcessAsync(":user{}!u@h QUIT :gone");

        GetChannelMembers("#chan").Should().Contain(n => string.Equals(n, "User[]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MonitoredNicks_AreComparedUsingCaseMapping_OnNickChange()
    {
        await RegisterAsync("observer", caseMapping: "rfc1459");
        AddEnabledCapability(IrcCapabilities.MONITOR);
        AddMonitoredNick("Bob[]");
        // The monitor rename is propagated to the server (MONITOR -/+); a live transport is required so
        // RefreshMonitoredNickAsync confirms the new nick instead of reverting it as unmonitored.
        SetConnectedWithTransport();

        var nickChanges = new List<NickChangedEvent>();
        _client.NickChanged += (_, e) => nickChanges.Add(e);

        // rfc1459: Bob[] == bob{} ; the rename of the monitored identity must be tracked.
        await ProcessAsync(":bob{}!u@h NICK :Bobby");

        // Wait on the asynchronously-dispatched NickChanged event — the LAST signal in the rename path.
        // (The monitored-set mutation runs synchronously under the state lock and would otherwise let the
        // assertions race ahead of the event dispatch under parallel-suite load.)
        await WaitUntilAsync(() => nickChanges.Count > 0, TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => GetMonitoredNicks().Contains("Bobby"), TimeSpan.FromSeconds(3));
        nickChanges.Should().NotBeEmpty();
        GetMonitoredNicks().Should().Contain("Bobby");
        GetMonitoredNicks().Should().NotContain(n => string.Equals(n, "Bob[]", StringComparison.Ordinal));
    }

    // ---- P1: a dynamic-mapping comparer must keep a STABLE hash code -------------------------------
    // A comparer whose hash code shifts with the live CASEMAPPING corrupts any hash table it backs:
    // a key inserted under one mapping lands in one bucket, and after the mapping changes the same
    // key hashes to a different bucket and becomes unreachable (the reviewer reproduced an entry that
    // was unreachable by BOTH "Nick[]" and "nick{}" after rfc1459 -> ascii). The hash must therefore
    // be anchored to the most-folding (rfc1459) mapping while equality follows the live mapping.

    [Fact]
    public void DynamicCaseComparer_KeepsEntryReachable_WhenMappingChangesAfterInsertion()
    {
        var mapping = CaseMappingType.Rfc1459;
        var comparer = IrcCaseMapping.CreateComparer(() => mapping);
        var table = new Dictionary<string, int>(comparer) { ["Nick[]"] = 1 };

        // Server refines the mapping to ascii AFTER the key was inserted.
        mapping = CaseMappingType.Ascii;

        table.ContainsKey("Nick[]").Should().BeTrue("the inserted key must remain reachable after a mapping change");
        table["Nick[]"].Should().Be(1);
        table.Remove("Nick[]").Should().BeTrue("the inserted key must remain removable after a mapping change");
    }

    [Fact]
    public void DynamicCaseComparer_AppliesLiveMapping_ToEquality()
    {
        // Under ascii, "Nick[]" and "nick{}" are distinct identities and must coexist.
        var ascii = CaseMappingType.Ascii;
        var asciiTable = new Dictionary<string, int>(IrcCaseMapping.CreateComparer(() => ascii))
        {
            ["Nick[]"] = 1,
            ["nick{}"] = 2
        };
        asciiTable.Should().HaveCount(2);
        asciiTable["Nick[]"].Should().Be(1);
        asciiTable["nick{}"].Should().Be(2);

        // Under rfc1459, the same two strings fold to one identity and must collapse.
        var rfc = CaseMappingType.Rfc1459;
        var rfcTable = new Dictionary<string, int>(IrcCaseMapping.CreateComparer(() => rfc))
        {
            ["Nick[]"] = 1
        };
        rfcTable["nick{}"] = 2; // same key under rfc1459 -> overwrites
        rfcTable.Should().ContainSingle();
        rfcTable["Nick[]"].Should().Be(2);
    }

    [Fact]
    public async Task MonitoredNick_StaysReachable_WhenCaseMappingChangesAfterInsertion()
    {
        // Default mapping before ISUPPORT is rfc1459. Monitor a bracketed nick, then have the server
        // advertise CASEMAPPING=ascii: the monitored-nick set must not lose the entry to a shifted hash.
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_WELCOME} observer :Welcome");
        AddMonitoredNick("Nick[]");

        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_ISUPPORT} observer CASEMAPPING=ascii :are supported by this server");

        GetMonitoredNicks().Should().Contain("Nick[]",
            "a monitored nick inserted before the mapping changed must remain reachable afterwards");
    }

    private async Task RegisterAsync(string nick, string caseMapping)
    {
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_WELCOME} {nick} :Welcome");
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_ISUPPORT} {nick} CASEMAPPING={caseMapping} :are supported by this server");
    }

    private async Task ProcessAsync(string rawMessage)
    {
        var message = IrcMessage.Parse(rawMessage);
        await (Task)_processMessageAsync.Invoke(_client, [message])!;
    }

    private IReadOnlyCollection<string> GetChannelMembers(string channel)
    {
        return _client.Channels.TryGetValue(channel, out var members)
            ? members.ToArray()
            : Array.Empty<string>();
    }

    private IReadOnlyCollection<string> GetUserInfoKeys()
    {
        var field = typeof(IrcClient).GetField("_userInfo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, UserInfo>)field.GetValue(_client)!;
        return dict.Keys.ToArray();
    }

    private IReadOnlyCollection<string> GetMonitoredNicks()
    {
        var field = typeof(IrcClient).GetField("_monitoredNicks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var set = (ConcurrentHashSet<string>)field.GetValue(_client)!;
        return set.ToArray();
    }

    private void AddEnabledCapability(string capability)
    {
        var field = typeof(IrcClient).GetField("_enabledCapabilities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var set = (ConcurrentHashSet<string>)field.GetValue(_client)!;
        set.Add(capability);
    }

    private void AddMonitoredNick(string nick)
    {
        var field = typeof(IrcClient).GetField("_monitoredNicks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var set = (ConcurrentHashSet<string>)field.GetValue(_client)!;
        set.Add(nick);
    }

    private void SetConnectedWithTransport()
    {
        typeof(IrcClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_client, new RecordingTransport());
        typeof(IrcClient).GetField("_isConnected", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_client, true);
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

    private sealed class RecordingTransport : IRCDotNet.Core.Transport.IIrcTransport
    {
        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task WriteLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
