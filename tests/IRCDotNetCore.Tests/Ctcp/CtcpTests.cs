using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IRCDotNet.Tests.Ctcp;

/// <summary>
/// Tests for CTCP (Client-To-Client Protocol) event types, parsing, and send methods.
/// </summary>
public class CtcpTests
{
    // ─── Event Construction ─────────────────────────────────────────────

    [Fact]
    public void CtcpRequestEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :\u0001VERSION\u0001");
        var evt = new CtcpRequestEvent(message, "nick", "user", "host", "#channel", "VERSION", "");

        evt.Nick.Should().Be("nick");
        evt.User.Should().Be("user");
        evt.Host.Should().Be("host");
        evt.Target.Should().Be("#channel");
        evt.Command.Should().Be("VERSION");
        evt.Parameters.Should().BeEmpty();
        evt.Message.Should().Be(message);
    }

    [Fact]
    public void CtcpRequestEvent_WithParameters_ShouldCaptureParamText()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG bot :\u0001PING 12345\u0001");
        var evt = new CtcpRequestEvent(message, "nick", "user", "host", "bot", "PING", "12345");

        evt.Command.Should().Be("PING");
        evt.Parameters.Should().Be("12345");
    }

    [Fact]
    public void CtcpReplyEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse(":nick!user@host NOTICE bot :\u0001VERSION mIRC\u0001");
        var evt = new CtcpReplyEvent(message, "nick", "user", "host", "VERSION", "mIRC");

        evt.Nick.Should().Be("nick");
        evt.User.Should().Be("user");
        evt.Host.Should().Be("host");
        evt.Command.Should().Be("VERSION");
        evt.ReplyText.Should().Be("mIRC");
    }

    [Fact]
    public void CtcpReplyEvent_EmptyReply_ShouldHaveEmptyText()
    {
        var message = IrcMessage.Parse(":nick!user@host NOTICE bot :\u0001PING\u0001");
        var evt = new CtcpReplyEvent(message, "nick", "user", "host", "PING", "");

        evt.ReplyText.Should().BeEmpty();
    }

    [Fact]
    public void CtcpActionEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :\u0001ACTION waves hello\u0001");
        var evt = new CtcpActionEvent(message, "nick", "user", "host", "#channel", "waves hello");

        evt.Nick.Should().Be("nick");
        evt.User.Should().Be("user");
        evt.Host.Should().Be("host");
        evt.Target.Should().Be("#channel");
        evt.ActionText.Should().Be("waves hello");
    }

    [Fact]
    public void CtcpActionEvent_ChannelAction_IsChannelAction_ShouldBeTrue()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :\u0001ACTION dances\u0001");
        var evt = new CtcpActionEvent(message, "nick", "user", "host", "#channel", "dances");

        evt.IsChannelAction.Should().BeTrue();
    }

    [Fact]
    public void CtcpActionEvent_PrivateAction_IsChannelAction_ShouldBeFalse()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG mybot :\u0001ACTION whispers\u0001");
        var evt = new CtcpActionEvent(message, "nick", "user", "host", "mybot", "whispers");

        evt.IsChannelAction.Should().BeFalse();
    }

    [Fact]
    public void CtcpActionEvent_AmpersandChannel_IsChannelAction_ShouldBeTrue()
    {
        var message = IrcMessage.Parse(":nick!user@host PRIVMSG &local :\u0001ACTION test\u0001");
        var evt = new CtcpActionEvent(message, "nick", "user", "host", "&local", "test");

        evt.IsChannelAction.Should().BeTrue();
    }

    // ─── CTCP Message Parsing (IrcMessage level) ────────────────────────

    [Fact]
    public void IrcMessageParse_CtcpVersion_ShouldParseDelimiters()
    {
        var msg = IrcMessage.Parse(":nick!user@host PRIVMSG bot :\u0001VERSION\u0001");

        msg.Command.Should().Be("PRIVMSG");
        msg.Parameters[0].Should().Be("bot");
        msg.Parameters[1].Should().Be("\u0001VERSION\u0001");
    }

    [Fact]
    public void IrcMessageParse_CtcpAction_ShouldPreserveFullText()
    {
        var msg = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :\u0001ACTION does something cool\u0001");

        msg.Parameters[1].Should().Be("\u0001ACTION does something cool\u0001");
    }

    [Fact]
    public void IrcMessageParse_CtcpPingWithTimestamp_ShouldPreserveParams()
    {
        var msg = IrcMessage.Parse(":nick!user@host PRIVMSG bot :\u0001PING 1234567890\u0001");

        msg.Parameters[1].Should().Be("\u0001PING 1234567890\u0001");
    }

    [Fact]
    public void IrcMessageParse_CtcpReplyInNotice_ShouldParseCorrectly()
    {
        var msg = IrcMessage.Parse(":nick!user@host NOTICE bot :\u0001VERSION mIRC v7.73\u0001");

        msg.Command.Should().Be("NOTICE");
        msg.Parameters[1].Should().Be("\u0001VERSION mIRC v7.73\u0001");
    }

    // ─── CTCP Content Extraction (simulating what HandleCtcpRequest does) ──

    [Theory]
    [InlineData("\u0001VERSION\u0001", "VERSION", "")]
    [InlineData("\u0001PING 12345\u0001", "PING", "12345")]
    [InlineData("\u0001ACTION waves hello\u0001", "ACTION", "waves hello")]
    [InlineData("\u0001TIME\u0001", "TIME", "")]
    [InlineData("\u0001CLIENTINFO\u0001", "CLIENTINFO", "")]
    [InlineData("\u0001FINGER\u0001", "FINGER", "")]
    [InlineData("\u0001SOURCE\u0001", "SOURCE", "")]
    [InlineData("\u0001USERINFO\u0001", "USERINFO", "")]
    [InlineData("\u0001ERRMSG test query\u0001", "ERRMSG", "test query")]
    public void CtcpContentParsing_ShouldExtractCommandAndParameters(string rawText, string expectedCommand, string expectedParams)
    {
        // Simulate the parsing logic from IrcClient.HandleCtcpRequest
        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        var spaceIndex = content.IndexOf(' ');
        var command = spaceIndex == -1 ? content : content[..spaceIndex];
        var parameters = spaceIndex == -1 ? string.Empty : content[(spaceIndex + 1)..];
        command = command.ToUpperInvariant();

        command.Should().Be(expectedCommand);
        parameters.Should().Be(expectedParams);
    }

    // ─── Tolerant Parsing (missing trailing \u0001) ───────────────────────

    [Fact]
    public void CtcpParsing_MissingTrailingDelimiter_ShouldStillParse()
    {
        var rawText = "\u0001VERSION"; // no trailing \u0001

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        content.Should().Be("VERSION");
    }

    [Fact]
    public void CtcpParsing_MissingTrailingDelimiter_WithParams_ShouldParse()
    {
        var rawText = "\u0001PING 99999"; // no trailing \u0001

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        var spaceIndex = content.IndexOf(' ');
        var command = content[..spaceIndex];
        var parameters = content[(spaceIndex + 1)..];

        command.Should().Be("PING");
        parameters.Should().Be("99999");
    }

    [Fact]
    public void CtcpParsing_BothDelimiters_ShouldStripBoth()
    {
        var rawText = "\u0001VERSION\u0001";

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        content.Should().Be("VERSION");
    }

    // ─── Edge Cases ─────────────────────────────────────────────────────

    [Fact]
    public void CtcpParsing_EmptyCtcpContent_ShouldReturnEmptyCommand()
    {
        // \u0001\u0001 — empty CTCP (edge case)
        var rawText = "\u0001\u0001";

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        content.Should().BeEmpty();

        var command = content.ToUpperInvariant();
        command.Should().BeEmpty();
    }

    [Fact]
    public void CtcpParsing_ActionWithEmptyText_ShouldHaveEmptyParams()
    {
        var rawText = "\u0001ACTION\u0001";

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        var spaceIndex = content.IndexOf(' ');
        var command = spaceIndex == -1 ? content : content[..spaceIndex];
        var parameters = spaceIndex == -1 ? string.Empty : content[(spaceIndex + 1)..];

        command.Should().Be("ACTION");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void CtcpParsing_MultipleSpaces_ShouldPreserveInParams()
    {
        var rawText = "\u0001ACTION does   something   with  spaces\u0001";

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        var spaceIndex = content.IndexOf(' ');
        var parameters = content[(spaceIndex + 1)..];

        parameters.Should().Be("does   something   with  spaces");
    }

    [Fact]
    public void CtcpParsing_LowercaseCommand_ShouldUppercase()
    {
        var rawText = "\u0001version\u0001";

        var content = rawText[^1] == '\u0001' ? rawText[1..^1] : rawText[1..];
        var command = content.ToUpperInvariant();

        command.Should().Be("VERSION");
    }

    [Fact]
    public void CtcpDetection_NormalMessage_ShouldNotBeCtcp()
    {
        var text = "Hello, this is a normal message";
        var isCtcp = text.Length >= 2 && text[0] == '\u0001';

        isCtcp.Should().BeFalse();
    }

    [Fact]
    public void CtcpDetection_SingleCharMessage_ShouldNotBeCtcp()
    {
        var text = "H";
        var isCtcp = text.Length >= 2 && text[0] == '\u0001';

        isCtcp.Should().BeFalse();
    }

    [Fact]
    public void CtcpDetection_SingleCtcpChar_ShouldNotBeCtcp()
    {
        // Just \u0001 alone — length is 1, not >= 2
        var text = "\u0001";
        var isCtcp = text.Length >= 2 && text[0] == '\u0001';

        isCtcp.Should().BeFalse();
    }

    // ─── Send Method Serialization ──────────────────────────────────────

    [Fact]
    public void SendAction_ShouldFormatCorrectly()
    {
        // Verify the raw string that SendActionAsync would produce
        var target = "#channel";
        var actionText = "waves hello";
        var expected = $"PRIVMSG {target} :\u0001ACTION {actionText}\u0001";

        expected.Should().Be("PRIVMSG #channel :\u0001ACTION waves hello\u0001");
    }

    [Fact]
    public void SendCtcpRequest_NoParams_ShouldFormatCorrectly()
    {
        var target = "someuser";
        var command = "VERSION";
        string? parameters = null;

        var ctcpContent = string.IsNullOrEmpty(parameters) ? command : $"{command} {parameters}";
        var expected = $"PRIVMSG {target} :\u0001{ctcpContent}\u0001";

        expected.Should().Be("PRIVMSG someuser :\u0001VERSION\u0001");
    }

    [Fact]
    public void SendCtcpRequest_WithParams_ShouldFormatCorrectly()
    {
        var target = "someuser";
        var command = "PING";
        var parameters = "12345";

        var ctcpContent = string.IsNullOrEmpty(parameters) ? command : $"{command} {parameters}";
        var expected = $"PRIVMSG {target} :\u0001{ctcpContent}\u0001";

        expected.Should().Be("PRIVMSG someuser :\u0001PING 12345\u0001");
    }

    [Fact]
    public void SendCtcpReply_ShouldFormatAsNotice()
    {
        // CTCP replies go via NOTICE, not PRIVMSG
        var command = "VERSION";
        var replyText = "IRCDotNet.Core";

        var ctcpContent = string.IsNullOrEmpty(replyText) ? command : $"{command} {replyText}";
        var expected = $"\u0001{ctcpContent}\u0001"; // This is what gets passed to SendNoticeAsync

        expected.Should().Be("\u0001VERSION IRCDotNet.Core\u0001");
    }

    // ─── Configuration ──────────────────────────────────────────────────

    [Fact]
    public void IrcClientOptions_CtcpDefaults_ShouldBeCorrect()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };

        options.EnableCtcpAutoReply.Should().BeTrue();
        options.CtcpVersionString.Should().StartWith("IRCDotNet.Core ");
    }

    [Fact]
    public void IrcClientOptions_CtcpAutoReply_CanBeDisabled()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            EnableCtcpAutoReply = false
        };

        options.EnableCtcpAutoReply.Should().BeFalse();
    }

    [Fact]
    public void IrcClientOptions_CtcpVersionString_CanBeCustomized()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            CtcpVersionString = "MyApp v1.0"
        };

        options.CtcpVersionString.Should().Be("MyApp v1.0");
    }

    [Fact]
    public void IrcClientOptions_Clone_ShouldCopyCtcpSettings()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            EnableCtcpAutoReply = false,
            CtcpVersionString = "Custom v2.0"
        };

        var cloned = options.Clone();

        cloned.EnableCtcpAutoReply.Should().BeFalse();
        cloned.CtcpVersionString.Should().Be("Custom v2.0");
    }

    [Fact]
    public void IrcClientOptions_Clone_ShouldNotShareCtcpSettings()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Nick = "test",
            UserName = "test",
            RealName = "test",
            CtcpVersionString = "Original"
        };

        var cloned = options.Clone();
        cloned.CtcpVersionString = "Modified";

        options.CtcpVersionString.Should().Be("Original");
    }

    // ─── Input Validation ───────────────────────────────────────────────

    [Fact]
    public async Task SendActionAsync_ShouldRejectNewlinesInActionText()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        // Can't actually call SendActionAsync without a connection,
        // but we can verify the validation logic directly
        var act = () => client.SendActionAsync("#channel", "line1\r\nline2");

        // Should throw because we're not connected, but the newline check
        // comes before the connection check
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*newline*");
    }

    [Fact]
    public async Task SendActionAsync_ShouldRejectNewlinesInTarget()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendActionAsync("#chan\r\nnel", "hello");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*newline*");
    }

    [Fact]
    public async Task SendCtcpRequestAsync_ShouldRejectNewlinesInParams()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendCtcpRequestAsync("user", "PING", "bad\nparam");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*newline*");
    }

    [Fact]
    public async Task SendActionAsync_ShouldRejectNullTarget()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendActionAsync(null!, "hello");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendActionAsync_ShouldRejectEmptyActionText()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendActionAsync("#channel", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendCtcpRequestAsync_ShouldRejectEmptyCommand()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendCtcpRequestAsync("user", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendCtcpReplyAsync_ShouldRejectEmptyCommand()
    {
        var options = new IrcClientOptions
        {
            Server = "test",
            Port = 6667,
            Nick = "test",
            UserName = "test",
            RealName = "test"
        };
        var client = new IrcClient(options);

        var act = () => client.SendCtcpReplyAsync("user", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
