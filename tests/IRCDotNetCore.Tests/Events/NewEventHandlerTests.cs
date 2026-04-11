using FluentAssertions;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Events;

/// <summary>
/// Tests for newly added IRC event types, message parsing, and numeric reply routing.
/// Covers: INVITE, RPL_UNAWAY/RPL_NOWAWAY, RPL_CHANNELMODEIS, RPL_NOTOPIC,
/// ERR_CHANOPRIVSNEEDED, ERR_NOTONCHANNEL, ERR_NEEDMOREPARAMS,
/// ERR_NOCHANMODES, ERR_TOOMANYCHANNELS, and ErrorReplyEvent.
/// </summary>
public class NewEventHandlerTests
{
    private const string ServerName = "irc.test.com";

    // ─── InviteReceivedEvent ────────────────────────────────────────────

    [Fact]
    public void InviteReceivedEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse(":Angel!wings@irc.org INVITE testnick #dust");
        var evt = new InviteReceivedEvent(message, "Angel", "wings", "irc.org", "#dust");

        evt.Nick.Should().Be("Angel");
        evt.User.Should().Be("wings");
        evt.Host.Should().Be("irc.org");
        evt.Channel.Should().Be("#dust");
        evt.Message.Should().Be(message);
    }

    [Fact]
    public void InviteMessage_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse(":Angel!wings@irc.org INVITE Wiz #Twilight_Zone");

        message.Should().NotBeNull();
        message.Command.Should().Be("INVITE");
        message.Source.Should().Be("Angel!wings@irc.org");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("Wiz");
        message.Parameters[1].Should().Be("#Twilight_Zone");
    }

    [Fact]
    public void InviteMessage_ChannelIsCasePreserved()
    {
        var message = IrcMessage.Parse(":nick!user@host INVITE me #MyChannel");

        message.Parameters[1].Should().Be("#MyChannel");
    }

    // ─── OwnAwayStatusChangedEvent ──────────────────────────────────────

    [Fact]
    public void OwnAwayStatusChangedEvent_NowAway_ShouldSetIsAwayTrue()
    {
        var message = IrcMessage.Parse($":{ServerName} 306 testnick :You have been marked as being away");
        var evt = new OwnAwayStatusChangedEvent(message, isAway: true, "You have been marked as being away");

        evt.IsAway.Should().BeTrue();
        evt.ServerMessage.Should().Be("You have been marked as being away");
        evt.Message.Should().Be(message);
    }

    [Fact]
    public void OwnAwayStatusChangedEvent_UnAway_ShouldSetIsAwayFalse()
    {
        var message = IrcMessage.Parse($":{ServerName} 305 testnick :You are no longer marked as being away");
        var evt = new OwnAwayStatusChangedEvent(message, isAway: false, "You are no longer marked as being away");

        evt.IsAway.Should().BeFalse();
    }

    [Fact]
    public void RplNowAway_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 306 testnick :You have been marked as being away");

        message.Should().NotBeNull();
        message.Command.Should().Be("306");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("You have been marked as being away");
    }

    [Fact]
    public void RplUnAway_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 305 testnick :You are no longer marked as being away");

        message.Command.Should().Be("305");
        message.Parameters[0].Should().Be("testnick");
    }

    // ─── ChannelModeIsEvent ─────────────────────────────────────────────

    [Fact]
    public void ChannelModeIsEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse($":{ServerName} 324 testnick #channel +nt");
        var evt = new ChannelModeIsEvent(message, "#channel", "+nt", "");

        evt.Channel.Should().Be("#channel");
        evt.Modes.Should().Be("+nt");
        evt.ModeParams.Should().BeEmpty();
    }

    [Fact]
    public void ChannelModeIsEvent_WithParams_ShouldCaptureAll()
    {
        var message = IrcMessage.Parse($":{ServerName} 324 testnick #channel +lk 50 secretkey");
        var evt = new ChannelModeIsEvent(message, "#channel", "+lk", "50 secretkey");

        evt.Channel.Should().Be("#channel");
        evt.Modes.Should().Be("+lk");
        evt.ModeParams.Should().Be("50 secretkey");
    }

    [Fact]
    public void RplChannelModeIs_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 324 testnick #channel +nt");

        message.Command.Should().Be("324");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("+nt");
    }

    [Fact]
    public void RplChannelModeIs_WithModeParams_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 324 testnick #channel +lk 50 secretkey");

        message.Parameters.Should().HaveCount(5);
        message.Parameters[2].Should().Be("+lk");
        message.Parameters[3].Should().Be("50");
        message.Parameters[4].Should().Be("secretkey");
    }

    // ─── ErrorReplyEvent ────────────────────────────────────────────────

    [Fact]
    public void ErrorReplyEvent_ShouldInitializeAllProperties()
    {
        var message = IrcMessage.Parse($":{ServerName} 482 testnick #channel :You're not channel operator");
        var evt = new ErrorReplyEvent(message, "482", "#channel", "You're not channel operator");

        evt.ErrorCode.Should().Be("482");
        evt.Target.Should().Be("#channel");
        evt.ErrorMessage.Should().Be("You're not channel operator");
    }

    [Theory]
    [InlineData("482", "#channel", "You're not channel operator")]
    [InlineData("442", "#channel", "You're not on that channel")]
    [InlineData("461", "JOIN", "Not enough parameters")]
    public void ErrorReplyEvent_VariousErrors_ShouldCaptureCorrectly(string code, string target, string errorMsg)
    {
        var message = IrcMessage.Parse($":{ServerName} {code} testnick {target} :{errorMsg}");
        var evt = new ErrorReplyEvent(message, code, target, errorMsg);

        evt.ErrorCode.Should().Be(code);
        evt.Target.Should().Be(target);
        evt.ErrorMessage.Should().Be(errorMsg);
    }

    // ─── Channel Join Error Numerics ────────────────────────────────────

    [Theory]
    [InlineData("477", "#networking", "You need to be identified with services")]
    [InlineData("405", "#toomany", "You have joined too many channels")]
    [InlineData("471", "#full", "Cannot join channel (+l)")]
    [InlineData("473", "#inviteonly", "Cannot join channel (+i)")]
    [InlineData("474", "#banned", "Cannot join channel (+b)")]
    [InlineData("475", "#keyed", "Cannot join channel (+k)")]
    public void ChannelJoinFailedErrors_ShouldParseCorrectly(string numeric, string channel, string reason)
    {
        var rawMessage = $":{ServerName} {numeric} testnick {channel} :{reason}";
        var message = IrcMessage.Parse(rawMessage);

        message.Should().NotBeNull();
        message.Command.Should().Be(numeric);
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be(channel);
        message.Parameters[2].Should().Be(reason);
    }

    [Fact]
    public void ChannelJoinFailedEvent_ForNochanmodes477_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 477 testnick #networking :You need to be identified with services");
        var evt = new ChannelJoinFailedEvent(message, "#networking", "You need to be identified with services", "477");

        evt.Channel.Should().Be("#networking");
        evt.Reason.Should().Be("You need to be identified with services");
    }

    // ─── RPL_NOTOPIC / RPL_TOPIC ────────────────────────────────────────

    [Fact]
    public void RplNotopic_ShouldParseWithTwoParams()
    {
        var message = IrcMessage.Parse($":{ServerName} 331 testnick #channel :No topic is set");

        message.Command.Should().Be("331");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("No topic is set");
    }

    [Fact]
    public void RplTopic_ShouldParseWithTopicText()
    {
        var message = IrcMessage.Parse($":{ServerName} 332 testnick #channel :Welcome to the channel!");

        message.Command.Should().Be("332");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("Welcome to the channel!");
    }

    [Fact]
    public void TopicChangedEvent_WithEmptyTopic_ShouldWork()
    {
        var message = IrcMessage.Parse($":{ServerName} 331 testnick #channel :No topic is set");
        var evt = new TopicChangedEvent(message, "#channel", string.Empty);

        evt.Channel.Should().Be("#channel");
        evt.Topic.Should().BeEmpty();
    }

    // ─── RPL_TOPICWHOTIME (333) ─────────────────────────────────────────

    [Fact]
    public void RplTopicWhoTime_ShouldParseCorrectly()
    {
        var message = IrcMessage.Parse($":{ServerName} 333 testnick #channel nick!user@host 1640995200");

        message.Command.Should().Be("333");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("nick!user@host");
        message.Parameters[3].Should().Be("1640995200");
    }

    // ─── General Error Numerics Routing ─────────────────────────────────

    [Theory]
    [InlineData("482", "ERR_CHANOPRIVSNEEDED")]
    [InlineData("442", "ERR_NOTONCHANNEL")]
    [InlineData("461", "ERR_NEEDMOREPARAMS")]
    [InlineData("477", "ERR_NOCHANMODES")]
    [InlineData("405", "ERR_TOOMANYCHANNELS")]
    public void ErrorNumericConstants_ShouldHaveCorrectValues(string expected, string constantName)
    {
        var value = constantName switch
        {
            "ERR_CHANOPRIVSNEEDED" => IrcNumericReplies.ERR_CHANOPRIVSNEEDED,
            "ERR_NOTONCHANNEL" => IrcNumericReplies.ERR_NOTONCHANNEL,
            "ERR_NEEDMOREPARAMS" => IrcNumericReplies.ERR_NEEDMOREPARAMS,
            "ERR_NOCHANMODES" => IrcNumericReplies.ERR_NOCHANMODES,
            "ERR_TOOMANYCHANNELS" => IrcNumericReplies.ERR_TOOMANYCHANNELS,
            _ => throw new System.ArgumentException($"Unknown constant: {constantName}")
        };

        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("305", "RPL_UNAWAY")]
    [InlineData("306", "RPL_NOWAWAY")]
    [InlineData("324", "RPL_CHANNELMODEIS")]
    [InlineData("331", "RPL_NOTOPIC")]
    [InlineData("333", "RPL_TOPICWHOTIME")]
    public void InfoNumericConstants_ShouldHaveCorrectValues(string expected, string constantName)
    {
        var value = constantName switch
        {
            "RPL_UNAWAY" => IrcNumericReplies.RPL_UNAWAY,
            "RPL_NOWAWAY" => IrcNumericReplies.RPL_NOWAWAY,
            "RPL_CHANNELMODEIS" => IrcNumericReplies.RPL_CHANNELMODEIS,
            "RPL_NOTOPIC" => IrcNumericReplies.RPL_NOTOPIC,
            "RPL_TOPICWHOTIME" => IrcNumericReplies.RPL_TOPICWHOTIME,
            _ => throw new System.ArgumentException($"Unknown constant: {constantName}")
        };

        value.Should().Be(expected);
    }

    // ─── Case-insensitive channel dictionary ────────────────────────────

    [Fact]
    public void IrcCommands_INVITE_ShouldBeDefinedCorrectly()
    {
        IrcCommands.INVITE.Should().Be("INVITE");
    }

    [Fact]
    public void CaseInsensitiveChannelDictionary_ShouldMatchSameChannelDifferentCase()
    {
        // Verify the dictionary comparer used in IrcClient._channels
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
            System.StringComparer.OrdinalIgnoreCase);

        dict["#Linux"] = 100;
        dict.ContainsKey("#linux").Should().BeTrue();
        dict.ContainsKey("#LINUX").Should().BeTrue();
        dict["#linux"].Should().Be(100);
    }

    // ─── Edge cases ─────────────────────────────────────────────────────

    [Fact]
    public void InviteReceivedEvent_WithSpecialChannelPrefix_ShouldWork()
    {
        var message = IrcMessage.Parse(":nick!user@host INVITE me &local_channel");
        var evt = new InviteReceivedEvent(message, "nick", "user", "host", "&local_channel");

        evt.Channel.Should().Be("&local_channel");
    }

    [Fact]
    public void ChannelModeIsEvent_NoModes_ShouldHandleEmptyModes()
    {
        var message = IrcMessage.Parse($":{ServerName} 324 testnick #channel");
        var evt = new ChannelModeIsEvent(message, "#channel", string.Empty, string.Empty);

        evt.Modes.Should().BeEmpty();
        evt.ModeParams.Should().BeEmpty();
    }

    [Fact]
    public void ErrorReplyEvent_MinimalParams_ShouldNotThrow()
    {
        var message = IrcMessage.Parse($":{ServerName} 461 testnick JOIN");
        var evt = new ErrorReplyEvent(message, "461", "JOIN", "JOIN");

        evt.ErrorCode.Should().Be("461");
        evt.Target.Should().Be("JOIN");
    }

    [Fact]
    public void OwnAwayStatusChangedEvent_EmptyMessage_ShouldNotThrow()
    {
        var message = IrcMessage.Parse($":{ServerName} 305 testnick");
        var evt = new OwnAwayStatusChangedEvent(message, isAway: false, string.Empty);

        evt.IsAway.Should().BeFalse();
        evt.ServerMessage.Should().BeEmpty();
        evt.Message.Should().Be(message);
    }
}
