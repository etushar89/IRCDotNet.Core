using FluentAssertions;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcCommandsTests
{
    [Fact]
    public void CommonCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.NICK.Should().Be("NICK");
        IrcCommands.USER.Should().Be("USER");
        IrcCommands.JOIN.Should().Be("JOIN");
        IrcCommands.PART.Should().Be("PART");
        IrcCommands.PRIVMSG.Should().Be("PRIVMSG");
        IrcCommands.NOTICE.Should().Be("NOTICE");
        IrcCommands.QUIT.Should().Be("QUIT");
        IrcCommands.PING.Should().Be("PING");
        IrcCommands.PONG.Should().Be("PONG");
        IrcCommands.MODE.Should().Be("MODE");
        IrcCommands.TOPIC.Should().Be("TOPIC");
        IrcCommands.KICK.Should().Be("KICK");
        IrcCommands.INVITE.Should().Be("INVITE");
    }

    [Fact]
    public void CapabilityCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.CAP.Should().Be("CAP");
    }

    [Fact]
    public void InformationCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.WHOIS.Should().Be("WHOIS");
        IrcCommands.WHOWAS.Should().Be("WHOWAS");
        IrcCommands.WHO.Should().Be("WHO");
        IrcCommands.LIST.Should().Be("LIST");
        IrcCommands.NAMES.Should().Be("NAMES");
    }

    [Fact]
    public void ServerCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.MOTD.Should().Be("MOTD");
        IrcCommands.VERSION.Should().Be("VERSION");
        IrcCommands.TIME.Should().Be("TIME");
        IrcCommands.INFO.Should().Be("INFO");
    }

    [Fact]
    public void OperatorCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.OPER.Should().Be("OPER");
        IrcCommands.KILL.Should().Be("KILL");
        IrcCommands.REHASH.Should().Be("REHASH");
        IrcCommands.RESTART.Should().Be("RESTART");
    }

    [Fact]
    public void ChannelCommands_ShouldHaveCorrectValues()
    {
        // Assert
        IrcCommands.INVITE.Should().Be("INVITE");
        IrcCommands.KICK.Should().Be("KICK");
        IrcCommands.MODE.Should().Be("MODE");
        IrcCommands.TOPIC.Should().Be("TOPIC");
    }
}

public class IrcNumericRepliesTests
{
    [Fact]
    public void WelcomeReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_WELCOME.Should().Be("001");
        IrcNumericReplies.RPL_YOURHOST.Should().Be("002");
        IrcNumericReplies.RPL_CREATED.Should().Be("003");
        IrcNumericReplies.RPL_MYINFO.Should().Be("004");
        IrcNumericReplies.RPL_ISUPPORT.Should().Be("005");
    }
    [Fact]
    public void UserReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_AWAY.Should().Be("301");
        IrcNumericReplies.RPL_UNAWAY.Should().Be("305");
        IrcNumericReplies.RPL_NOWAWAY.Should().Be("306");
    }

    [Fact]
    public void WhoisReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_WHOISUSER.Should().Be("311");
        IrcNumericReplies.RPL_WHOISSERVER.Should().Be("312");
        IrcNumericReplies.RPL_WHOISOPERATOR.Should().Be("313");
        IrcNumericReplies.RPL_WHOISIDLE.Should().Be("317");
        IrcNumericReplies.RPL_ENDOFWHOIS.Should().Be("318");
        IrcNumericReplies.RPL_WHOISCHANNELS.Should().Be("319");
    }

    [Fact]
    public void ChannelReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_LISTSTART.Should().Be("321");
        IrcNumericReplies.RPL_LIST.Should().Be("322");
        IrcNumericReplies.RPL_LISTEND.Should().Be("323");
        IrcNumericReplies.RPL_CHANNELMODEIS.Should().Be("324");
        IrcNumericReplies.RPL_NOTOPIC.Should().Be("331");
        IrcNumericReplies.RPL_TOPIC.Should().Be("332");
        IrcNumericReplies.RPL_TOPICWHOTIME.Should().Be("333");
    }

    [Fact]
    public void NamesReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_NAMREPLY.Should().Be("353");
        IrcNumericReplies.RPL_ENDOFNAMES.Should().Be("366");
    }

    [Fact]
    public void BanListReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_BANLIST.Should().Be("367");
        IrcNumericReplies.RPL_ENDOFBANLIST.Should().Be("368");
    }
    [Fact]
    public void InfoReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.RPL_VERSION.Should().Be("351");
        IrcNumericReplies.RPL_MOTDSTART.Should().Be("375");
        IrcNumericReplies.RPL_MOTD.Should().Be("372");
        IrcNumericReplies.RPL_ENDOFMOTD.Should().Be("376");
    }
    [Fact]
    public void ErrorReplies_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_NOSUCHNICK.Should().Be("401");
        IrcNumericReplies.ERR_NOSUCHSERVER.Should().Be("402");
        IrcNumericReplies.ERR_NOSUCHCHANNEL.Should().Be("403");
        IrcNumericReplies.ERR_CANNOTSENDTOCHAN.Should().Be("404");
        IrcNumericReplies.ERR_TOOMANYCHANNELS.Should().Be("405");
        IrcNumericReplies.ERR_WASNOSUCHNICK.Should().Be("406");
    }
    [Fact]
    public void NicknameErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_NONICKNAMEGIVEN.Should().Be("431");
        IrcNumericReplies.ERR_ERRONEUSNICKNAME.Should().Be("432");
        IrcNumericReplies.ERR_NICKNAMEINUSE.Should().Be("433");
    }

    [Fact]
    public void ChannelErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_USERNOTINCHANNEL.Should().Be("441");
        IrcNumericReplies.ERR_NOTONCHANNEL.Should().Be("442");
        IrcNumericReplies.ERR_USERONCHANNEL.Should().Be("443");
    }

    [Fact]
    public void RegistrationErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_NOTREGISTERED.Should().Be("451");
    }

    [Fact]
    public void ParameterErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_NEEDMOREPARAMS.Should().Be("461");
        IrcNumericReplies.ERR_ALREADYREGISTERED.Should().Be("462");
        IrcNumericReplies.ERR_PASSWDMISMATCH.Should().Be("464");
        IrcNumericReplies.ERR_YOUREBANNEDCREEP.Should().Be("465");
    }

    [Fact]
    public void ChannelModeErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_CHANNELISFULL.Should().Be("471");
        IrcNumericReplies.ERR_UNKNOWNMODE.Should().Be("472");
        IrcNumericReplies.ERR_INVITEONLYCHAN.Should().Be("473");
        IrcNumericReplies.ERR_BANNEDFROMCHAN.Should().Be("474");
        IrcNumericReplies.ERR_BADCHANNELKEY.Should().Be("475");
    }

    [Fact]
    public void OperatorErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_NOPRIVILEGES.Should().Be("481");
        IrcNumericReplies.ERR_CHANOPRIVSNEEDED.Should().Be("482");
    }

    [Fact]
    public void MiscellaneousErrors_ShouldHaveCorrectValues()
    {
        // Assert
        IrcNumericReplies.ERR_UMODEUNKNOWNFLAG.Should().Be("501");
        IrcNumericReplies.ERR_USERSDONTMATCH.Should().Be("502");
    }

    [Theory]
    [InlineData("001")]
    [InlineData("401")]
    [InlineData("433")]
    public void NumericReplies_ShouldBeThreeDigitStrings(string numeric)
    {
        // Assert
        numeric.Should().HaveLength(3);
        numeric.Should().MatchRegex(@"^\d{3}$");
    }
}
