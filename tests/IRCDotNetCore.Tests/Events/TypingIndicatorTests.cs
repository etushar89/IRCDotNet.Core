using FluentAssertions;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Events;

public class TypingIndicatorTests
{
    [Fact]
    public void TypingIndicatorEvent_Active_ShouldInitializeCorrectly()
    {
        var message = IrcMessage.Parse("@+typing=active :nick!user@host TAGMSG #channel");

        var eventArgs = new TypingIndicatorEvent(message, "nick", "user", "host", "#channel", TypingState.Active);

        eventArgs.Nick.Should().Be("nick");
        eventArgs.User.Should().Be("user");
        eventArgs.Host.Should().Be("host");
        eventArgs.Target.Should().Be("#channel");
        eventArgs.State.Should().Be(TypingState.Active);
        eventArgs.IsChannelTyping.Should().BeTrue();
    }

    [Fact]
    public void TypingIndicatorEvent_Paused_ShouldInitializeCorrectly()
    {
        var message = IrcMessage.Parse("@+typing=paused :nick!user@host TAGMSG #channel");

        var eventArgs = new TypingIndicatorEvent(message, "nick", "user", "host", "#channel", TypingState.Paused);

        eventArgs.State.Should().Be(TypingState.Paused);
        eventArgs.IsChannelTyping.Should().BeTrue();
    }

    [Fact]
    public void TypingIndicatorEvent_Done_ShouldInitializeCorrectly()
    {
        var message = IrcMessage.Parse("@+typing=done :nick!user@host TAGMSG someuser");

        var eventArgs = new TypingIndicatorEvent(message, "nick", "user", "host", "someuser", TypingState.Done);

        eventArgs.State.Should().Be(TypingState.Done);
        eventArgs.IsChannelTyping.Should().BeFalse();
    }

    [Fact]
    public void TypingIndicatorEvent_PrivateMessage_IsChannelTyping_ShouldBeFalse()
    {
        var message = IrcMessage.Parse("@+typing=active :nick!user@host TAGMSG myNick");

        var eventArgs = new TypingIndicatorEvent(message, "nick", "user", "host", "myNick", TypingState.Active);

        eventArgs.IsChannelTyping.Should().BeFalse();
    }

    [Fact]
    public void TypingIndicatorEvent_AmpersandChannel_IsChannelTyping_ShouldBeTrue()
    {
        var message = IrcMessage.Parse("@+typing=active :nick!user@host TAGMSG &local");

        var eventArgs = new TypingIndicatorEvent(message, "nick", "user", "host", "&local", TypingState.Active);

        eventArgs.IsChannelTyping.Should().BeTrue();
    }

    [Theory]
    [InlineData("active", TypingState.Active)]
    [InlineData("paused", TypingState.Paused)]
    [InlineData("done", TypingState.Done)]
    public void TypingState_Enum_ShouldHaveCorrectValues(string _, TypingState expected)
    {
        expected.Should().BeDefined();
    }

    [Fact]
    public void TagMessage_WithTypingTag_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse("@+typing=active :nick!user@host TAGMSG #channel");

        message.Tags.Should().ContainKey("+typing");
        message.Tags["+typing"].Should().Be("active");
        message.Command.Should().Be("TAGMSG");
        message.Parameters.Should().ContainSingle().Which.Should().Be("#channel");
        message.Source.Should().Be("nick!user@host");
    }
}
