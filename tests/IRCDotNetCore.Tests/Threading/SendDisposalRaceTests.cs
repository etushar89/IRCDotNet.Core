using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Transport;
using Xunit;

namespace IRCDotNet.Tests.Threading;

/// <summary>
/// Pins the Core-side contract for the VPN-triggered SemaphoreSlim disposal
/// race. Investigation memo in the Client repo:
/// /memories/repo/ircdotnet-client-vpn-semaphoreslim-disposal-race-20260529.md.
///
/// Scenario:
///   1. Connect attempt is mid-flight; <c>_isConnected = true</c> and
///      <c>_transport</c> is set.
///   2. A concurrent <c>Dispose</c> disposes <c>_sendLock</c> /
///      <c>_connectLock</c> in its <c>finally</c> block, while a background
///      continuation (auto-PONG, CAP-REQ reply, or a registration step) is
///      still mid-<c>SendRawAsync</c>.
///   3. <c>_sendLock.WaitAsync</c> on the disposed semaphore throws
///      <see cref="ObjectDisposedException"/>.
///
/// Contract under test: <c>SendRawAsync</c> / <c>SendRawWithCancellationAsync</c>
/// / <c>SendCommandAsync</c> must NOT propagate ObjectDisposedException to
/// callers. The client is being torn down; the send was unobservable to the
/// user anyway. Surfacing it as
/// <see cref="InvalidOperationException"/>("Not connected") matches the
/// "client is no longer usable" contract that callers already handle.
/// </summary>
public sealed class SendDisposalRaceTests : IDisposable
{
    private readonly IrcClient _client;

    public SendDisposalRaceTests()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.net",
            Port = 6667,
            Nick = "tester",
            UserName = "tester",
            RealName = "Test User",
            EnableRateLimit = false,
        };
        _client = new IrcClient(options);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task SendRawAsync_WhenSendLockDisposedMidRace_ShouldNotPropagateObjectDisposedException()
    {
        // Arrange: simulate the post-_isConnected-check state where a
        // background dispose has already torn down _sendLock.
        ForceConnectedStateWithStubTransport(_client);
        DisposeSendLockOnly(_client);

        // Act: invoking the send must surface a clean "Not connected" rather
        // than leaking the SemaphoreSlim ODE.
        var act = async () => await _client.SendRawAsync("PING :keepalive");

        // Assert: NOT ObjectDisposedException — that is the leak the user saw.
        await act.Should().NotThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task SendRawAsync_WhenSendLockDisposedMidRace_ShouldThrowInvalidOperationNotConnected()
    {
        // Tightens the contract: callers (the adapter, in particular) already
        // map "Not connected" to a clean reason code. Throwing ObjectDisposed
        // would walk through GetInnermostMessage and leak the SemaphoreSlim
        // text into ConnectionFault.UserReason.
        ForceConnectedStateWithStubTransport(_client);
        DisposeSendLockOnly(_client);

        var act = async () => await _client.SendRawAsync("PING :keepalive");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Not connected");
    }

    [Fact]
    public async Task SendRawWithCancellationAsync_WhenSendLockDisposedMidRace_ShouldNotPropagateObjectDisposedException()
    {
        ForceConnectedStateWithStubTransport(_client);
        DisposeSendLockOnly(_client);

        var act = async () => await _client.SendRawWithCancellationAsync("PING :keepalive", CancellationToken.None);

        await act.Should().NotThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task SendMessageAsync_WhenSendLockDisposedMidRace_ShouldNotPropagateObjectDisposedException()
    {
        // Top-level convenience API for PRIVMSG. Same disposal race must not
        // leak through this path either.
        ForceConnectedStateWithStubTransport(_client);
        DisposeSendLockOnly(_client);

        var act = async () => await _client.SendMessageAsync("#general", "hello");

        await act.Should().NotThrowAsync<ObjectDisposedException>();
    }

    private static void ForceConnectedStateWithStubTransport(IrcClient client)
    {
        // The send path requires _isConnected == true AND _transport != null
        // (checked under _stateLock) to reach the _sendLock.WaitAsync site.
        // Test stub transport: marked connected so the inner state check
        // passes; WriteLineAsync is never reached because WaitAsync throws
        // before then.
        SetField(client, "_isConnected", true);
        SetField(client, "_isRegistered", true);
        SetField(client, "_transport", new TestStubTransport());
        SetField(client, "_cancellationTokenSource", new CancellationTokenSource());
    }

    private static void DisposeSendLockOnly(IrcClient client)
    {
        // Simulate the precise window the user's VPN repro hits: outer
        // dispose has run far enough to release the semaphores in its
        // finally block, but _isConnected has not yet been observed false
        // by the in-flight send caller.
        var sendLock = GetField<SemaphoreSlim>(client, "_sendLock");
        sendLock.Should().NotBeNull("the test depends on Core's send gate field name being '_sendLock'");
        sendLock!.Dispose();
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {fieldName} should exist on {instance.GetType().Name}");
        field!.SetValue(instance, value);
    }

    private static T? GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (T?)field?.GetValue(instance);
    }

    private sealed class TestStubTransport : IIrcTransport
    {
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteLineAsync(string line, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
