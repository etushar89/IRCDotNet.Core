using System.Reflection;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace IRCDotNet.Tests;

public sealed class IrcClientMonitorLifecycleTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly MethodInfo _processMessageAsync;

    public IrcClientMonitorLifecycleTests()
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
    public async Task MonitorOfflineFollowedByPrivateMessageWithMatchingUserHost_ShouldNotGuessNickChanged()
    {
        var nickChanges = new List<NickChangedEvent>();
        var quitEvents = new List<UserQuitEvent>();
        var privateMessages = new List<PrivateMessageEvent>();

        _client.NickChanged += (_, e) => nickChanges.Add(e);
        _client.UserQuit += (_, e) => quitEvents.Add(e);
        _client.PrivateMessageReceived += (_, e) => privateMessages.Add(e);

        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONONLINE} observer :Bob!shareduser@host.test");
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONOFFLINE} observer :Bob");
        await ProcessAsync(":Bob2!shareduser@host.test PRIVMSG observer :hello-after-rename");

        await WaitUntilAsync(() => quitEvents.Count > 0 && privateMessages.Count > 0, TimeSpan.FromSeconds(3));

        nickChanges.Should().BeEmpty();
        quitEvents.Should().ContainSingle(e =>
            string.Equals(e.Nick, "Bob", StringComparison.OrdinalIgnoreCase));
        privateMessages.Should().ContainSingle(e =>
            string.Equals(e.Nick, "Bob2", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Text, "hello-after-rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MonitorOfflineWithMultipleMatchingCandidates_ShouldNotGuessNickChange()
    {
        var nickChanges = new List<NickChangedEvent>();
        var quitEvents = new List<UserQuitEvent>();

        _client.NickChanged += (_, e) => nickChanges.Add(e);
        _client.UserQuit += (_, e) => quitEvents.Add(e);

        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONONLINE} observer :Bob!shareduser@host.test");
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONONLINE} observer :Alice!shareduser@host.test");

        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONOFFLINE} observer :Bob");
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONOFFLINE} observer :Alice");
        await ProcessAsync(":Bob2!shareduser@host.test PRIVMSG observer :ambiguous-identity");

        await Task.Delay(1100);

        nickChanges.Should().BeEmpty();
        quitEvents.Should().Contain(e => string.Equals(e.Nick, "Bob", StringComparison.OrdinalIgnoreCase));
        quitEvents.Should().Contain(e => string.Equals(e.Nick, "Alice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MonitorOfflineWithSingleMatchingCandidate_ShouldNotGuessNickChange()
    {
        var nickChanges = new List<NickChangedEvent>();
        var quitEvents = new List<UserQuitEvent>();

        _client.NickChanged += (_, e) => nickChanges.Add(e);
        _client.UserQuit += (_, e) => quitEvents.Add(e);

        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONONLINE} observer :Bob!shareduser@host.test");
        await ProcessAsync($":irc.example.com {IrcNumericReplies.RPL_MONOFFLINE} observer :Bob");
        await ProcessAsync(":Bob2!shareduser@host.test NOTICE observer :no-guess-notice");

        await WaitUntilAsync(() => quitEvents.Count > 0, TimeSpan.FromSeconds(3));

        nickChanges.Should().BeEmpty();
        quitEvents.Should().ContainSingle(e =>
            string.Equals(e.Nick, "Bob", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ProcessAsync(string rawMessage)
    {
        var message = IrcMessage.Parse(rawMessage);
        await (Task)_processMessageAsync.Invoke(_client, [message])!;
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
}
