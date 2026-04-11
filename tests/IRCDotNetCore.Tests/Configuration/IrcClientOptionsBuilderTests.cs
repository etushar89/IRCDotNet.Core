using FluentAssertions;
using IRCDotNet.Core.Configuration;
using Xunit;

namespace IRCDotNet.Tests.Configuration;

public class IrcClientOptionsBuilderTests
{
    // ─── WithWebSocket ──────────────────────────────────────────────────

    [Fact]
    public void WithWebSocket_ShouldSetWebSocketUri()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .WithWebSocket("wss://irc.example.com/webirc")
            .Build();

        options.WebSocketUri.Should().Be("wss://irc.example.com/webirc");
    }

    [Fact]
    public void WithWebSocket_ShouldNotRequireAddServer()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .WithWebSocket("wss://irc.example.com/webirc")
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithoutServerOrWebSocket_ShouldThrow()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*server*WebSocket*");
    }

    [Fact]
    public void WithWebSocket_NullUri_ShouldThrow()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithWebSocket(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithWebSocket_EmptyUri_ShouldThrow()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithWebSocket("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithWebSocket_InvalidScheme_ShouldThrowOnBuild()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .WithWebSocket("http://not-a-ws-uri.com/")
            .Build();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ws://*wss://*");
    }

    [Fact]
    public void WithWebSocket_WsScheme_ShouldBeValid()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .WithWebSocket("ws://localhost:8080/irc")
            .Build();

        options.WebSocketUri.Should().Be("ws://localhost:8080/irc");
    }

    [Fact]
    public void WithWebSocket_ServerFieldsShouldBeEmpty()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .WithWebSocket("wss://irc.example.com/webirc")
            .Build();

        options.Server.Should().BeEmpty();
        options.Port.Should().Be(6667);
        options.UseSsl.Should().BeFalse();
    }

    [Fact]
    public void WithWebSocket_AndAddServer_ShouldUseBoth()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com", 6697, true)
            .WithWebSocket("wss://irc.example.com/webirc")
            .Build();

        options.Server.Should().Be("irc.example.com");
        options.Port.Should().Be(6697);
        options.WebSocketUri.Should().Be("wss://irc.example.com/webirc");
    }

    // ─── BuildForWebSocket ──────────────────────────────────────────────

    [Fact]
    public void BuildForWebSocket_ShouldSetUriAndBuild()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .BuildForWebSocket("wss://irc.example.com/webirc");

        options.WebSocketUri.Should().Be("wss://irc.example.com/webirc");
    }

    // ─── WithCtcpAutoReply ──────────────────────────────────────────────

    [Fact]
    public void WithCtcpAutoReply_Enabled_ShouldSetTrue()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com")
            .WithCtcpAutoReply(true)
            .Build();

        options.EnableCtcpAutoReply.Should().BeTrue();
    }

    [Fact]
    public void WithCtcpAutoReply_Disabled_ShouldSetFalse()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com")
            .WithCtcpAutoReply(false)
            .Build();

        options.EnableCtcpAutoReply.Should().BeFalse();
    }

    [Fact]
    public void WithCtcpAutoReply_DefaultsToTrue()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com")
            .Build();

        options.EnableCtcpAutoReply.Should().BeTrue();
    }

    // ─── WithCtcpVersionString ──────────────────────────────────────────

    [Fact]
    public void WithCtcpVersionString_ShouldSetCustomVersion()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com")
            .WithCtcpVersionString("MyApp v3.0")
            .Build();

        options.CtcpVersionString.Should().Be("MyApp v3.0");
    }

    [Fact]
    public void WithCtcpVersionString_Default_ShouldUseAssemblyVersion()
    {
        var options = new IrcClientOptionsBuilder()
            .WithNick("test")
            .WithUserName("test")
            .WithRealName("test")
            .AddServer("irc.example.com")
            .Build();

        options.CtcpVersionString.Should().StartWith("IRCDotNet.Core ");
    }

    [Fact]
    public void WithCtcpVersionString_NullValue_ShouldThrow()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithCtcpVersionString(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithCtcpVersionString_EmptyValue_ShouldThrow()
    {
        var act = () => new IrcClientOptionsBuilder()
            .WithCtcpVersionString("  ");

        act.Should().Throw<ArgumentException>();
    }

    // ─── FromTemplate ───────────────────────────────────────────────────

    [Fact]
    public void FromTemplate_ShouldCopyWebSocketUri()
    {
        var template = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .Build();

        options.WebSocketUri.Should().Be("wss://irc.example.com/webirc");
    }

    [Fact]
    public void FromTemplate_ShouldCopyCtcpAutoReply()
    {
        var template = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            EnableCtcpAutoReply = false
        };

        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .AddServer("irc.example.com")
            .Build();

        options.EnableCtcpAutoReply.Should().BeFalse();
    }

    [Fact]
    public void FromTemplate_ShouldCopyCtcpVersionString()
    {
        var template = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            CtcpVersionString = "Custom v1.0"
        };

        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .AddServer("irc.example.com")
            .Build();

        options.CtcpVersionString.Should().Be("Custom v1.0");
    }

    [Fact]
    public void FromTemplate_TcpServer_ShouldCopyPrimaryServer()
    {
        var template = new IrcClientOptions
        {
            Server = "irc.libera.chat",
            Port = 6697,
            UseSsl = true,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .Build();

        options.Server.Should().Be("irc.libera.chat");
        options.Port.Should().Be(6697);
        options.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void FromTemplate_WebSocket_BuildWithoutAddServer_ShouldWork()
    {
        var template = new IrcClientOptions
        {
            Nick = "test",
            UserName = "test",
            RealName = "test",
            WebSocketUri = "wss://irc.example.com/webirc"
        };

        var act = () => IrcClientOptionsBuilder.FromTemplate(template).Build();

        act.Should().NotThrow();
    }
}
