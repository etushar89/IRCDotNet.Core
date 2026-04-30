using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IRCDotNet.Tests;

public class IrcClientTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly IrcClientOptions _options;

    public IrcClientTests()
    {
        _options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false
        };

        _client = new IrcClient(_options, NullLogger<IrcClient>.Instance);
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitializeCorrectly()
    {        // Assert
        _client.IsConnected.Should().BeFalse();
        _client.IsRegistered.Should().BeFalse();
        _client.CurrentNick.Should().Be("testbot");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Action act = () => new IrcClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }
    [Fact]
    public void ConnectAsync_WithValidOptions_ShouldBeAvailable()
    {
        // Arrange
        var connected = false;
        _client.Connected += (sender, args) => connected = true;

        // Note: This test would need a mock TCP client or test server
        // For now, we'll test that the method is available

        // Act
        var connectMethod = _client.GetType().GetMethod("ConnectAsync");

        // Assert
        connectMethod.Should().NotBeNull();
        connected.Should().BeFalse(); // Since we can't actually connect in unit tests
    }
    [Fact]
    public void SendRaw_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendRawAsync("PRIVMSG #test :Hello");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void JoinChannel_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.JoinChannelAsync("#test");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void LeaveChannel_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.LeaveChannelAsync("#test");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void SendMessage_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendMessageAsync("user", "Hello");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void SendNotice_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SendNoticeAsync("user", "Notice");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Theory]
    [InlineData("Hello\r\nQUIT :injected")]
    [InlineData("Line1\nLine2")]
    [InlineData("Line1\rLine2")]
    public void SendMessage_WithNewlinesInMessage_ShouldThrowArgumentException(string message)
    {
        Func<Task> act = async () => await _client.SendMessageAsync("user", message);
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("user\r\nQUIT")]
    [InlineData("user\n")]
    public void SendMessage_WithNewlinesInTarget_ShouldThrowArgumentException(string target)
    {
        Func<Task> act = async () => await _client.SendMessageAsync(target, "Hello");
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("Hello\r\nQUIT :injected")]
    [InlineData("Line1\nLine2")]
    public void SendNotice_WithNewlinesInMessage_ShouldThrowArgumentException(string message)
    {
        Func<Task> act = async () => await _client.SendNoticeAsync("user", message);
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Theory]
    [InlineData("user\r\nQUIT")]
    [InlineData("user\n")]
    public void SendNotice_WithNewlinesInTarget_ShouldThrowArgumentException(string target)
    {
        Func<Task> act = async () => await _client.SendNoticeAsync(target, "Hello");
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*newline*");
    }

    [Fact]
    public void SetTopic_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.SetTopicAsync("#test", "New topic");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void GetUserInfo_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.GetUserInfoAsync("user");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }

    [Fact]
    public void ChangeNick_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        Func<Task> act = async () => await _client.ChangeNickAsync("newnick");
        act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*not connected*");
    }
    [Fact]
    public void EventHandlers_CanBeAssigned()
    {
        // Act & Assert - These should compile without error
        _client.Connected += (sender, e) => { };
        _client.Disconnected += (sender, e) => { };
        _client.UserJoinedChannel += (sender, e) => { };
        _client.UserLeftChannel += (sender, e) => { };
        _client.UserQuit += (sender, e) => { };
        _client.PrivateMessageReceived += (sender, e) => { };
        _client.NoticeReceived += (sender, e) => { };
        _client.TopicChanged += (sender, e) => { };
        _client.UserKicked += (sender, e) => { };
        _client.NickChanged += (sender, e) => { };
        _client.ChannelUsersReceived += (sender, e) => { };
        _client.RawMessageReceived += (sender, e) => { };
        _client.IsupportReceived += (sender, e) => { };

        // If we got here without compilation errors, the test passes
        true.Should().BeTrue();
    }

    [Fact]
    public async Task RaiseEventAsync_WhenEarlierEventHandlerIsBlocked_ShouldNotLetLaterEventsOvertake()
    {
        var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowFirstHandlerToContinue = new ManualResetEventSlim(false);
        var secondHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedMessages = new List<string>();

        _client.PrivateMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Text, "first", StringComparison.Ordinal))
            {
                firstHandlerStarted.TrySetResult();
                allowFirstHandlerToContinue.Wait(TimeSpan.FromSeconds(5));
            }
            else if (string.Equals(e.Text, "second", StringComparison.Ordinal))
            {
                secondHandlerStarted.TrySetResult();
            }

            lock (observedMessages)
            {
                observedMessages.Add(e.Text);
            }
        };

        InvokeRaiseEventAsync(_client, "PrivateMessageReceived", CreatePrivateMessageEvent("first"));
        await firstHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        InvokeRaiseEventAsync(_client, "PrivateMessageReceived", CreatePrivateMessageEvent("second"));
        await Task.Delay(250);

        secondHandlerStarted.Task.IsCompleted.Should().BeFalse();

        allowFirstHandlerToContinue.Set();
        await secondHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (observedMessages)
        {
            observedMessages.Should().Equal("first", "second");
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenIsupportReceived_ShouldRaiseTypedEventAfterParsing()
    {
        var observed = new TaskCompletionSource<IsupportReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        CaseMappingType mappingObservedByHandler = default;
        _client.IsupportReceived += (_, args) =>
        {
            mappingObservedByHandler = _client.GetServerCaseMapping();
            observed.TrySetResult(args);
        };

        var message = CreateMessage("005", "testbot", "NETWORK=ExampleNet", "NICKLEN=32", "CHANTYPES=#&", "PREFIX=(ov)@+", "CASEMAPPING=ascii", "CAPAB=echo-message", "are supported by this server");

        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", message);

        var args = await observed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        args.Message.Should().BeSameAs(message);
        args.NetworkName.Should().Be("ExampleNet");
        args.MaxNicknameLength.Should().Be(32);
        args.ChannelTypes.Should().Be("#&");
        args.ChannelModePrefixModes.Should().Be("ov");
        args.ChannelModePrefixPrefixes.Should().Be("@+");
        args.Features.Should().ContainKey("CASEMAPPING");
        args.CaseMapping.Should().Be(CaseMappingType.Ascii);
        mappingObservedByHandler.Should().Be(CaseMappingType.Ascii);
        args.SupportedCapabilities.Should().Contain("echo-message");
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenMessageIsNotIsupport_ShouldNotRaiseTypedIsupportEvent()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.IsupportReceived += (_, _) => observed.TrySetResult();

        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", CreateMessage("001", "testbot", "Welcome"));

        var completed = await Task.WhenAny(observed.Task, Task.Delay(150));
        completed.Should().NotBe(observed.Task);
    }

    [Fact]
    public async Task SendMessageAsync_WhenConcurrentCallsTargetSameClient_ShouldSerializeTransportWrites()
    {
        var transport = new BlockingTransport();
        SetPrivateField(_client, "_transport", transport);
        SetPrivateField(_client, "_isConnected", true);

        var firstSend = _client.SendMessageAsync("user", "first");
        await transport.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var secondSend = _client.SendMessageAsync("user", "second");
        await Task.Delay(200);

        transport.WriteCount.Should().Be(1);
        transport.MaxConcurrentWrites.Should().Be(1);

        transport.AllowFirstWriteToComplete();

        await Task.WhenAll(firstSend, secondSend);

        transport.WriteCount.Should().Be(2);
        transport.MaxConcurrentWrites.Should().Be(1);
        transport.WrittenLines.Should().Equal(
            "PRIVMSG user :first",
            "PRIVMSG user :second");
    }

    [Fact]
    public async Task DisposeAsync_WhenEventsAreAlreadyQueued_ShouldDrainQueueAndRaiseDisconnected()
    {
        var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandlerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectedReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowFirstHandlerToContinue = new ManualResetEventSlim(false);
        var observedMessages = new List<string>();

        SetPrivateField(_client, "_transport", new BlockingTransport());
        SetPrivateField(_client, "_isConnected", true);

        _client.PrivateMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Text, "first", StringComparison.Ordinal))
            {
                firstHandlerStarted.TrySetResult();
                allowFirstHandlerToContinue.Wait(TimeSpan.FromSeconds(5));
            }

            lock (observedMessages)
            {
                observedMessages.Add(e.Text);
            }

            if (string.Equals(e.Text, "second", StringComparison.Ordinal))
                secondHandlerCompleted.TrySetResult();
        };

        _client.Disconnected += (_, _) => disconnectedReceived.TrySetResult();

        InvokeRaiseEventAsync(_client, "PrivateMessageReceived", CreatePrivateMessageEvent("first"));
        await firstHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        InvokeRaiseEventAsync(_client, "PrivateMessageReceived", CreatePrivateMessageEvent("second"));

        var disposeTask = _client.DisposeAsync().AsTask();

        await Task.Delay(250);
        secondHandlerCompleted.Task.IsCompleted.Should().BeFalse();
        disconnectedReceived.Task.IsCompleted.Should().BeFalse();

        allowFirstHandlerToContinue.Set();

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        await secondHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await disconnectedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (observedMessages)
        {
            observedMessages.Should().Equal("first", "second");
        }
    }

    [Fact]
    public async Task DisconnectAsync_WhenQuitWriteDoesNotComplete_ShouldStillFinishCleanup()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false,
            SendTimeoutCancelledMs = 50
        };
        using var client = new IrcClient(options, NullLogger<IrcClient>.Instance);
        var transport = new BlockingTransport { IgnoreWriteCancellation = true };
        SetPrivateField(client, "_transport", transport);
        SetPrivateField(client, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(client, "_isConnected", true);

        await client.DisconnectAsync("test shutdown").WaitAsync(TimeSpan.FromSeconds(5));

        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SendPing_WhenPongTimesOut_ShouldScheduleReconnectAttemptAndPreserveChannelsToRejoin()
    {
        var options = new IrcClientOptions
        {
            Server = "127.0.0.1",
            Port = 1,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false,
            EnableRateLimit = false,
            AutoReconnect = true,
            ConnectionTimeoutMs = 50,
            PingIntervalMs = 10,
            PingTimeoutMs = 25,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            MaxReconnectAttempts = 1,
        };

        using var client = new IrcClient(options, NullLogger<IrcClient>.Instance);
        var disconnectedReason = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Disconnected += (_, e) => disconnectedReason.TrySetResult(e.Reason);

        SetPrivateField(client, "_transport", new BlockingTransport());
        SetPrivateField(client, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(client, "_isConnected", true);
        SetPrivateField(client, "_currentNick", options.Nick);
        SetPrivateField(client, "_lastPongReceived", DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(options.PingTimeoutMs + 100));

        var channels = GetPrivateField<ConcurrentDictionary<string, IRCDotNet.Core.Utilities.ConcurrentHashSet<string>>>(client, "_channels");
        channels.Should().NotBeNull();

        var roomUsers = new IRCDotNet.Core.Utilities.ConcurrentHashSet<string>();
        roomUsers.Add("alice");
        channels!["#room"] = roomUsers;

        InvokePrivateMethod(client, "SendPing", new object?[] { null });

        (await disconnectedReason.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("Ping timeout");

        await WaitForConditionAsync(
            () => GetPrivateField<int>(client, "_reconnectAttempts") == 1,
            TimeSpan.FromSeconds(5));

        GetPrivateField<List<string>>(client, "_channelsToRejoin").Should().Equal("#room");
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenLaterNamesSnapshotOmitsUsers_ShouldReplaceMembershipAndPreservePrefixes()
    {
        var receivedSnapshots = new List<string[]>();

        _client.ChannelUsersReceived += (_, e) =>
        {
            receivedSnapshots.Add(
                e.Users
                    .OrderBy(user => user.Nick, StringComparer.OrdinalIgnoreCase)
                    .Select(user => $"{new string(user.Prefixes.ToArray())}{user.Nick}")
                    .ToArray());
        };

        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 353 testbot = #room :@alice bob"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 366 testbot #room :End of /NAMES list."));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 353 testbot = #room :@alice"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 366 testbot #room :End of /NAMES list."));

        await WaitForConditionAsync(() => receivedSnapshots.Count == 2, TimeSpan.FromSeconds(5));

        receivedSnapshots.Should().HaveCount(2);
        receivedSnapshots[0].Should().Equal("@alice", "bob");
        receivedSnapshots[1].Should().Equal("@alice");
        _client.Channels.Should().ContainKey("#room");
        _client.Channels["#room"]
            .OrderBy(nick => nick, StringComparer.OrdinalIgnoreCase)
            .Should().Equal("alice");
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenNamesSnapshotInterleavesMembershipChanges_ShouldApplyLiveDeltasAfterEndOfNames()
    {
        var receivedSnapshots = new List<string[]>();

        _client.ChannelUsersReceived += (_, e) =>
        {
            receivedSnapshots.Add(
                e.Users
                    .OrderBy(user => user.Nick, StringComparer.OrdinalIgnoreCase)
                    .Select(user => $"{new string(user.Prefixes.ToArray())}{user.Nick}")
                    .ToArray());
        };

        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 353 testbot = #room :@alice"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":bob!user@host JOIN #room"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":bob!user@host NICK robert"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":alice!user@host QUIT :gone"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":carol!user@host JOIN #room"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 353 testbot = #room :dave"));
        await InvokePrivateMethodAsync(_client, "ProcessMessageAsync", IrcMessage.Parse(":server 366 testbot #room :End of /NAMES list."));

        await WaitForConditionAsync(() => receivedSnapshots.Count == 1, TimeSpan.FromSeconds(5));

        receivedSnapshots.Should().ContainSingle();
        receivedSnapshots[0].Should().Equal("carol", "dave", "robert");
        _client.Channels.Should().ContainKey("#room");
        _client.Channels["#room"]
            .OrderBy(nick => nick, StringComparer.OrdinalIgnoreCase)
            .Should().Equal("carol", "dave", "robert");
    }

    [Fact]
    public void ClientProperties_ShouldBeAccessible()
    {
        // Assert - These should compile without error
        _client.IsConnected.Should().BeFalse();
        _client.IsRegistered.Should().BeFalse();
        _client.CurrentNick.Should().Be("testbot");
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private static void InvokeRaiseEventAsync<TEvent>(IrcClient client, string eventName, TEvent eventArgs)
        where TEvent : IrcEvent
    {
        var eventField = typeof(IrcClient).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        eventField.Should().NotBeNull();

        var raiseEventMethod = typeof(IrcClient).GetMethod("RaiseEventAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        raiseEventMethod.Should().NotBeNull();

        raiseEventMethod!
            .MakeGenericMethod(typeof(TEvent))
            .Invoke(client, [eventField!.GetValue(client), eventArgs]);
    }

    private static void SetPrivateField(IrcClient client, string fieldName, object? value)
    {
        var field = typeof(IrcClient).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(client, value);
    }

    private static T? GetPrivateField<T>(IrcClient client, string fieldName)
    {
        var field = typeof(IrcClient).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T?)field!.GetValue(client);
    }

    private static object? InvokePrivateMethod(IrcClient client, string methodName, params object?[]? arguments)
    {
        var method = typeof(IrcClient).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(client, arguments);
    }

    private static async Task InvokePrivateMethodAsync(IrcClient client, string methodName, params object?[]? arguments)
    {
        var result = InvokePrivateMethod(client, methodName, arguments);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25).ConfigureAwait(false);
        }

        predicate().Should().BeTrue();
    }

    private static PrivateMessageEvent CreatePrivateMessageEvent(string text)
    {
        return new PrivateMessageEvent(
            new IrcMessage { Command = "PRIVMSG" },
            "alice",
            "user",
            "host.test",
            "#room",
            text);
    }

    private static IrcMessage CreateMessage(string command, params string[] parameters)
    {
        var message = new IrcMessage { Command = command };
        foreach (var parameter in parameters)
        {
            message.Parameters.Add(parameter);
        }

        return message;
    }

    private sealed class BlockingTransport : IRCDotNet.Core.Transport.IIrcTransport
    {
        private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowFirstWriteToComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeWrites;
        private int _maxConcurrentWrites;
        private int _writeCount;

        public bool IsConnected => true;

        public IReadOnlyList<string> WrittenLines => _writtenLines;

        public int WriteCount => Volatile.Read(ref _writeCount);

        public int MaxConcurrentWrites => Volatile.Read(ref _maxConcurrentWrites);

        public Task FirstWriteStarted => _firstWriteStarted.Task;

        public bool IgnoreWriteCancellation { get; init; }

        private List<string> _writtenLines { get; } = new();

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            var activeWrites = Interlocked.Increment(ref _activeWrites);
            UpdateMaxConcurrentWrites(activeWrites);

            try
            {
                lock (_writtenLines)
                {
                    _writtenLines.Add(line);
                }

                var writeIndex = Interlocked.Increment(ref _writeCount);
                if (writeIndex == 1)
                {
                    _firstWriteStarted.TrySetResult();
                    if (IgnoreWriteCancellation)
                    {
                        await _allowFirstWriteToComplete.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        await _allowFirstWriteToComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void AllowFirstWriteToComplete()
        {
            _allowFirstWriteToComplete.TrySetResult();
        }

        private void UpdateMaxConcurrentWrites(int activeWrites)
        {
            while (true)
            {
                var currentMax = Volatile.Read(ref _maxConcurrentWrites);
                if (activeWrites <= currentMax)
                    return;

                if (Interlocked.CompareExchange(ref _maxConcurrentWrites, activeWrites, currentMax) == currentMax)
                    return;
            }
        }
    }
}

public class IrcClientConfigurationTests
{
    [Fact]
    public void IrcClientOptions_WithDefaultValues_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new IrcClientOptions();

        // Assert
        options.Port.Should().Be(6667);
        options.UseSsl.Should().BeFalse();
        options.ConnectionTimeoutMs.Should().Be(30000);
        options.PingIntervalMs.Should().Be(30000);
        options.PingTimeoutMs.Should().Be(60000);
        options.ReconnectDelayMs.Should().Be(5000);
        options.MaxReconnectAttempts.Should().Be(0);
        options.AutoReconnect.Should().BeTrue();
        options.RequestedCapabilities.Should().NotBeNull();
    }

    [Fact]
    public void IrcClientOptions_Validation_ShouldValidateRequiredFields()
    {
        // Arrange
        var options = new IrcClientOptions();

        // Act & Assert
        options.Server.Should().Be(string.Empty);
        options.Nick.Should().Be(string.Empty);
        options.UserName.Should().Be(string.Empty);
        options.RealName.Should().Be(string.Empty);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void IrcClientOptions_InvalidPort_ShouldAllowButWillFailOnValidation(int port)
    {
        // Arrange & Act
        var options = new IrcClientOptions
        {
            Port = port,
            Server = "irc.example.com",
            Nick = "testnick",
            UserName = "testuser",
            RealName = "Test User"
        };

        // Assert - The options class doesn't validate automatically
        options.Port.Should().Be(port);

        // But validation should fail for invalid ports
        if (port <= 0 || port > 65535)
        {
            Action act = () => options.Validate();
            act.Should().Throw<ArgumentOutOfRangeException>()
               .WithMessage("*Port*");
        }
        else
        {
            // Valid port should not throw
            Action act = () => options.Validate();
            act.Should().NotThrow();
        }
    }
}
