using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IRCDotNet.Tests.Transport;

/// <summary>
/// Tests for the IRC transport abstraction layer (IIrcTransport, TcpIrcTransport, WebSocketIrcTransport).
/// </summary>
public class TransportTests
{
    // ─── IrcClientOptions WebSocket Configuration ───────────────────────

    [Fact]
    public void IrcClientOptions_WebSocketUri_DefaultsToNull()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        options.WebSocketUri.Should().BeNull();
    }

    [Fact]
    public void IrcClientOptions_WebSocketUri_CanBeSet()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.libera.chat/webirc"
        };

        options.WebSocketUri.Should().Be("wss://irc.libera.chat/webirc");
    }

    [Fact]
    public void IrcClientOptions_Clone_ShouldCopyWebSocketUri()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://example.com/irc"
        };

        var cloned = options.Clone();

        cloned.WebSocketUri.Should().Be("wss://example.com/irc");
    }

    [Fact]
    public void IrcClientOptions_Clone_NullWebSocketUri_ShouldRemainNull()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var cloned = options.Clone();

        cloned.WebSocketUri.Should().BeNull();
    }

    [Fact]
    public void IrcClientOptions_Validate_WebSocketMode_ShouldNotRequireServer()
    {
        var options = new IrcClientOptions
        {
            Server = "",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        // Should not throw — WebSocket mode skips Server/Port validation
        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void IrcClientOptions_Validate_TcpMode_ShouldRequireServer()
    {
        var options = new IrcClientOptions
        {
            Server = "",
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>().WithMessage("*Server*");
    }

    [Fact]
    public void IrcClientOptions_Validate_TcpMode_ShouldRequireValidPort()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 0,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var act = () => options.Validate();
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Port*");
    }

    [Fact]
    public void IrcClientOptions_Validate_WebSocketMode_ShouldStillRequireNick()
    {
        var options = new IrcClientOptions
        {
            Server = "",
            Nick = "",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>().WithMessage("*Nick*");
    }

    // ─── TcpIrcTransport Construction ───────────────────────────────────

    [Fact]
    public void TcpIrcTransport_Constructor_ShouldAcceptValidOptions()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void TcpIrcTransport_Constructor_ShouldAcceptLogger()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options, NullLogger.Instance);

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void TcpIrcTransport_Constructor_ShouldRejectNullOptions()
    {
        var act = () => new TcpIrcTransport(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TcpIrcTransport_IsConnected_ShouldBeFalseBeforeConnect()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TcpIrcTransport_ReadLineAsync_BeforeConnect_ShouldReturnNull()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        var result = await transport.ReadLineAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void TcpIrcTransport_WriteLineAsync_BeforeConnect_ShouldThrow()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        var act = () => transport.WriteLineAsync("PING :test");

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task TcpIrcTransport_DisconnectAsync_BeforeConnect_ShouldNotThrow()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        await transport.DisconnectAsync();

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void TcpIrcTransport_Dispose_BeforeConnect_ShouldNotThrow()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        var act = () => transport.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task TcpIrcTransport_DisposeAsync_BeforeConnect_ShouldNotThrow()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var transport = new TcpIrcTransport(options);

        await transport.DisposeAsync();

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TcpIrcTransport_ConnectAsync_InvalidServer_ShouldThrow()
    {
        var options = new IrcClientOptions
        {
            Server = "this.server.does.not.exist.invalid",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test",
            ConnectionTimeoutMs = 2000
        };

        var transport = new TcpIrcTransport(options);

        var act = () => transport.ConnectAsync();

        await act.Should().ThrowAsync<Exception>();
        transport.IsConnected.Should().BeFalse();
    }

    // ─── WebSocketIrcTransport Construction ──────────────────────────────

    [Fact]
    public void WebSocketIrcTransport_Constructor_ShouldAcceptValidOptions()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var transport = new WebSocketIrcTransport(options);

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void WebSocketIrcTransport_Constructor_ShouldRejectNullOptions()
    {
        var act = () => new WebSocketIrcTransport(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WebSocketIrcTransport_Constructor_ShouldRejectMissingUri()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test"
            // WebSocketUri not set
        };

        var act = () => new WebSocketIrcTransport(options);

        act.Should().Throw<ArgumentException>().WithMessage("*WebSocketUri*");
    }

    [Fact]
    public void WebSocketIrcTransport_IsConnected_ShouldBeFalseBeforeConnect()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var transport = new WebSocketIrcTransport(options);

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void WebSocketIrcTransport_WriteLineAsync_BeforeConnect_ShouldThrow()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var transport = new WebSocketIrcTransport(options);

        var act = () => transport.WriteLineAsync("PING :test");

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task WebSocketIrcTransport_ReadLineAsync_BeforeConnect_ShouldReturnNull()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var transport = new WebSocketIrcTransport(options);

        var result = await transport.ReadLineAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task WebSocketIrcTransport_DisconnectAsync_BeforeConnect_ShouldNotThrow()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var transport = new WebSocketIrcTransport(options);

        await transport.DisconnectAsync();

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task WebSocketIrcTransport_ConnectAsync_InvalidUri_ShouldThrow()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://this.server.does.not.exist.invalid/webirc",
            ConnectionTimeoutMs = 2000
        };

        var transport = new WebSocketIrcTransport(options);

        var act = () => transport.ConnectAsync();

        await act.Should().ThrowAsync<Exception>();
        transport.IsConnected.Should().BeFalse();
    }

    // ─── IrcClient Transport Selection ──────────────────────────────────

    [Fact]
    public void IrcClient_WithTcpOptions_ShouldCreateSuccessfully()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var client = new IrcClient(options);

        client.IsConnected.Should().BeFalse();
        client.Configuration.WebSocketUri.Should().BeNull();
    }

    [Fact]
    public void IrcClient_WithWebSocketOptions_ShouldCreateSuccessfully()
    {
        var options = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var client = new IrcClient(options);

        client.IsConnected.Should().BeFalse();
        client.Configuration.WebSocketUri.Should().Be("wss://irc.example.com/webirc");
    }

    // ─── IIrcTransport Interface Conformance ────────────────────────────

    [Fact]
    public void TcpIrcTransport_ShouldImplementIIrcTransport()
    {
        typeof(TcpIrcTransport).Should().Implement<IIrcTransport>();
    }

    [Fact]
    public void WebSocketIrcTransport_ShouldImplementIIrcTransport()
    {
        typeof(WebSocketIrcTransport).Should().Implement<IIrcTransport>();
    }

    [Fact]
    public void TcpIrcTransport_ShouldImplementIDisposable()
    {
        typeof(TcpIrcTransport).Should().Implement<IDisposable>();
    }

    [Fact]
    public void TcpIrcTransport_ShouldImplementIAsyncDisposable()
    {
        typeof(TcpIrcTransport).Should().Implement<IAsyncDisposable>();
    }

    [Fact]
    public void WebSocketIrcTransport_ShouldImplementIDisposable()
    {
        typeof(WebSocketIrcTransport).Should().Implement<IDisposable>();
    }

    [Fact]
    public void WebSocketIrcTransport_ShouldImplementIAsyncDisposable()
    {
        typeof(WebSocketIrcTransport).Should().Implement<IAsyncDisposable>();
    }
}
