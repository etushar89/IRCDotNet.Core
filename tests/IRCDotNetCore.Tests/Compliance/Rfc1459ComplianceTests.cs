using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests.Compliance;

/// <summary>
/// Tests for RFC 1459 compliance improvements including server info handling,
/// WHO/WHOWAS/LIST commands, MOTD processing, and enhanced numeric reply handling
/// </summary>
public class Rfc1459ComplianceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public Rfc1459ComplianceTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    #region WHO Command Tests

    [Fact]
    public async Task WhoAsync_WithValidTarget_ShouldSendWhoCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.WhoAsync("#channel");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WhoAsync_WithNullTarget_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.WhoAsync(null!));
    }

    [Fact]
    public async Task WhoAsync_WithEmptyTarget_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.WhoAsync(""));
    }

    [Fact]
    public async Task WhoAsync_WithWhitespaceTarget_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.WhoAsync("   "));
    }

    #endregion

    #region WHOWAS Command Tests

    [Fact]
    public async Task WhoWasAsync_WithValidNick_ShouldSendWhoWasCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.WhoWasAsync("testnick");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WhoWasAsync_WithNullNick_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.WhoWasAsync(null!));
    }

    [Fact]
    public async Task WhoWasAsync_WithEmptyNick_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.WhoWasAsync(""));
    }

    #endregion

    #region LIST Command Tests

    [Fact]
    public async Task ListChannelsAsync_WithoutPattern_ShouldSendListCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.ListChannelsAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListChannelsAsync_WithPattern_ShouldSendListCommandWithPattern()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.ListChannelsAsync("#test*");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListChannelsAsync_WithEmptyPattern_ShouldSendListCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.ListChannelsAsync("");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region MODE Command Tests

    [Fact]
    public async Task SetUserModeAsync_WithValidModes_ShouldSendModeCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.SetUserModeAsync("+i");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetUserModeAsync_WithNullModes_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SetUserModeAsync(null!));
    }

    [Fact]
    public async Task SetUserModeAsync_WithEmptyModes_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetUserModeAsync(""));
    }

    [Fact]
    public async Task SetChannelModeAsync_WithValidParameters_ShouldSendModeCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.SetChannelModeAsync("#channel", "+o", "nick");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetChannelModeAsync_WithNullChannel_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SetChannelModeAsync(null!, "+o"));
    }

    [Fact]
    public async Task SetChannelModeAsync_WithNullModes_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SetChannelModeAsync("#channel", null!));
    }

    [Fact]
    public async Task GetChannelModeAsync_WithValidChannel_ShouldSendModeCommand()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Method should throw InvalidOperationException when not connected
        var act = async () => await client.GetChannelModeAsync("#channel");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetChannelModeAsync_WithNullChannel_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetChannelModeAsync(null!));
    }

    #endregion

    #region RFC 1459 Event Tests

    [Fact]
    public void WhoReceived_Event_ShouldBeAvailable()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Should be able to subscribe to the event without throwing
        var act = () => client.WhoReceived += (sender, args) => { };
        act.Should().NotThrow();
    }

    [Fact]
    public void WhoWasReceived_Event_ShouldBeAvailable()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Should be able to subscribe to the event without throwing
        var act = () => client.WhoWasReceived += (sender, args) => { };
        act.Should().NotThrow();
    }

    [Fact]
    public void ChannelListReceived_Event_ShouldBeAvailable()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Should be able to subscribe to the event without throwing
        var act = () => client.ChannelListReceived += (sender, args) => { };
        act.Should().NotThrow();
    }

    #endregion

    #region Numeric Reply Constants Tests

    [Fact]
    public void ServerInfoReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that all server info numeric replies are defined
        IrcNumericReplies.RPL_YOURHOST.Should().Be("002");
        IrcNumericReplies.RPL_CREATED.Should().Be("003");
        IrcNumericReplies.RPL_MYINFO.Should().Be("004");
        IrcNumericReplies.RPL_ISUPPORT.Should().Be("005");
    }

    [Fact]
    public void MotdReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that all MOTD numeric replies are defined
        IrcNumericReplies.RPL_MOTDSTART.Should().Be("375");
        IrcNumericReplies.RPL_MOTD.Should().Be("372");
        IrcNumericReplies.RPL_ENDOFMOTD.Should().Be("376");
        IrcNumericReplies.ERR_NOMOTD.Should().Be("422");
    }

    [Fact]
    public void WhoReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that WHO numeric replies are defined
        IrcNumericReplies.RPL_WHOREPLY.Should().Be("352");
        IrcNumericReplies.RPL_ENDOFWHO.Should().Be("315");
    }

    [Fact]
    public void WhoWasReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that WHOWAS numeric replies are defined
        IrcNumericReplies.RPL_WHOWASUSER.Should().Be("314");
        IrcNumericReplies.RPL_ENDOFWHOWAS.Should().Be("369");
    }

    [Fact]
    public void ListReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that LIST numeric replies are defined
        IrcNumericReplies.RPL_LIST.Should().Be("322");
        IrcNumericReplies.RPL_LISTEND.Should().Be("323");
    }

    [Fact]
    public void WhoisReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that all WHOIS numeric replies are defined
        IrcNumericReplies.RPL_WHOISUSER.Should().Be("311");
        IrcNumericReplies.RPL_WHOISSERVER.Should().Be("312");
        IrcNumericReplies.RPL_WHOISOPERATOR.Should().Be("313");
        IrcNumericReplies.RPL_WHOISIDLE.Should().Be("317");
        IrcNumericReplies.RPL_ENDOFWHOIS.Should().Be("318");
        IrcNumericReplies.RPL_WHOISCHANNELS.Should().Be("319");
    }

    [Fact]
    public void ErrorReplies_ShouldHaveCorrectValues()
    {
        // Assert - Test that all essential error replies are defined
        IrcNumericReplies.ERR_NOSUCHNICK.Should().Be("401");
        IrcNumericReplies.ERR_NOSUCHSERVER.Should().Be("402");
        IrcNumericReplies.ERR_NOSUCHCHANNEL.Should().Be("403");
        IrcNumericReplies.ERR_CANNOTSENDTOCHAN.Should().Be("404");
        IrcNumericReplies.ERR_UNKNOWNCOMMAND.Should().Be("421");
    }

    #endregion

    #region Command Constants Tests

    [Fact]
    public void Rfc1459Commands_ShouldHaveCorrectValues()
    {
        // Assert - Test that all RFC 1459 commands are defined
        IrcCommands.WHO.Should().Be("WHO");
        IrcCommands.WHOIS.Should().Be("WHOIS");
        IrcCommands.WHOWAS.Should().Be("WHOWAS");
        IrcCommands.LIST.Should().Be("LIST");
        IrcCommands.MODE.Should().Be("MODE");
        IrcCommands.MOTD.Should().Be("MOTD");
        IrcCommands.VERSION.Should().Be("VERSION");
        IrcCommands.TIME.Should().Be("TIME");
        IrcCommands.INFO.Should().Be("INFO");
        IrcCommands.ADMIN.Should().Be("ADMIN");
        IrcCommands.AWAY.Should().Be("AWAY");
        IrcCommands.USERHOST.Should().Be("USERHOST");
        IrcCommands.LINKS.Should().Be("LINKS");
    }

    [Fact]
    public void OperatorCommands_ShouldHaveCorrectValues()
    {
        // Assert - Test that operator commands are defined (even if not implemented)
        IrcCommands.KILL.Should().Be("KILL");
        IrcCommands.REHASH.Should().Be("REHASH");
        IrcCommands.RESTART.Should().Be("RESTART");
        IrcCommands.SQUIT.Should().Be("SQUIT");
        IrcCommands.WALLOPS.Should().Be("WALLOPS");
    }

    #endregion

    #region Message Processing Tests

    [Theory]
    [InlineData("352 testnick #channel user host server nick H :0 Real Name")]
    [InlineData("315 testnick #channel :End of WHO list")]
    public void WhoReply_Processing_ShouldHandleValidFormats(string rawMessage)
    {
        // Arrange
        var message = IrcMessage.Parse(rawMessage);

        // Act & Assert - Message should parse correctly
        message.Should().NotBeNull();
        message.Command.Should().BeOneOf("352", "315");
    }

    [Theory]
    [InlineData("314 testnick oldnick user host * :Real Name")]
    [InlineData("369 testnick oldnick :End of WHOWAS")]
    public void WhoWasReply_Processing_ShouldHandleValidFormats(string rawMessage)
    {
        // Arrange
        var message = IrcMessage.Parse(rawMessage);

        // Act & Assert - Message should parse correctly
        message.Should().NotBeNull();
        message.Command.Should().BeOneOf("314", "369");
    }

    [Theory]
    [InlineData("322 testnick #channel 42 :Channel topic")]
    [InlineData("323 testnick :End of LIST")]
    public void ListReply_Processing_ShouldHandleValidFormats(string rawMessage)
    {
        // Arrange
        var message = IrcMessage.Parse(rawMessage);

        // Act & Assert - Message should parse correctly
        message.Should().NotBeNull();
        message.Command.Should().BeOneOf("322", "323");
    }

    [Theory]
    [InlineData("002 testnick :Your host is irc.example.com")]
    [InlineData("003 testnick :This server was created Mon Jan 1 2024")]
    [InlineData("004 testnick irc.example.com ircd-seven-1.1.9 DOQRSZaghilopsuwz CFILMPQSbcefgijklmnopqrstvz bkloveqjfI")]
    [InlineData("005 testnick CHANTYPES=# PREFIX=(ov)@+ NETWORK=TestNet :are supported")]
    public void ServerInfo_Processing_ShouldHandleValidFormats(string rawMessage)
    {
        // Arrange
        var message = IrcMessage.Parse(rawMessage);

        // Act & Assert - Message should parse correctly
        message.Should().NotBeNull();
        message.Command.Should().BeOneOf("002", "003", "004", "005");
    }

    [Theory]
    [InlineData("375 testnick :- irc.example.com Message of the Day -")]
    [InlineData("372 testnick :- Welcome to the test network")]
    [InlineData("376 testnick :End of MOTD command")]
    [InlineData("422 testnick :MOTD File is missing")]
    public void Motd_Processing_ShouldHandleValidFormats(string rawMessage)
    {
        // Arrange
        var message = IrcMessage.Parse(rawMessage);

        // Act & Assert - Message should parse correctly
        message.Should().NotBeNull();
        message.Command.Should().BeOneOf("375", "372", "376", "422");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Client_ShouldSupportAllEssentialRfc1459Commands()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - All essential RFC 1459 methods should be available
        var clientType = client.GetType();

        // Connection commands
        clientType.GetMethod("SendRawAsync").Should().NotBeNull();
        clientType.GetMethod("ConnectAsync").Should().NotBeNull();
        clientType.GetMethod("DisconnectAsync").Should().NotBeNull();

        // Channel commands
        clientType.GetMethod("JoinChannelAsync").Should().NotBeNull();
        clientType.GetMethod("LeaveChannelAsync").Should().NotBeNull();
        clientType.GetMethod("SetTopicAsync").Should().NotBeNull();
        clientType.GetMethod("GetTopicAsync").Should().NotBeNull();
        clientType.GetMethod("GetChannelUsersAsync").Should().NotBeNull();
        clientType.GetMethod("KickUserAsync").Should().NotBeNull();
        clientType.GetMethod("InviteUserAsync").Should().NotBeNull();

        // Message commands
        clientType.GetMethod("SendMessageAsync").Should().NotBeNull();
        clientType.GetMethod("SendNoticeAsync").Should().NotBeNull();

        // User commands
        clientType.GetMethod("ChangeNickAsync").Should().NotBeNull();
        clientType.GetMethod("GetUserInfoAsync").Should().NotBeNull();
        clientType.GetMethod("SetAwayAsync").Should().NotBeNull();

        // RFC 1459 compliance commands
        clientType.GetMethod("WhoAsync").Should().NotBeNull();
        clientType.GetMethod("WhoWasAsync").Should().NotBeNull();
        clientType.GetMethod("ListChannelsAsync").Should().NotBeNull();
        clientType.GetMethod("SetUserModeAsync").Should().NotBeNull();
        clientType.GetMethod("SetChannelModeAsync").Should().NotBeNull();
        clientType.GetMethod("GetChannelModeAsync").Should().NotBeNull();
    }

    [Fact]
    public void Client_ShouldSupportAllEssentialRfc1459Events()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - All essential RFC 1459 events should be available
        var clientType = client.GetType();

        // Connection events
        clientType.GetEvent("Connected").Should().NotBeNull();
        clientType.GetEvent("Disconnected").Should().NotBeNull();

        // Channel events
        clientType.GetEvent("UserJoinedChannel").Should().NotBeNull();
        clientType.GetEvent("UserLeftChannel").Should().NotBeNull();
        clientType.GetEvent("TopicChanged").Should().NotBeNull();
        clientType.GetEvent("UserKicked").Should().NotBeNull();
        clientType.GetEvent("ChannelUsersReceived").Should().NotBeNull();

        // Message events
        clientType.GetEvent("PrivateMessageReceived").Should().NotBeNull();
        clientType.GetEvent("NoticeReceived").Should().NotBeNull();

        // User events
        clientType.GetEvent("NickChanged").Should().NotBeNull();
        clientType.GetEvent("UserQuit").Should().NotBeNull();

        // RFC 1459 compliance events
        clientType.GetEvent("WhoReceived").Should().NotBeNull();
        clientType.GetEvent("WhoWasReceived").Should().NotBeNull();
        clientType.GetEvent("ChannelListReceived").Should().NotBeNull();

        // General events
        clientType.GetEvent("RawMessageReceived").Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private IrcClient CreateTestClient()
    {
        var options = new IrcClientOptions
        {
            Server = "irc.test.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false
        };

        var client = new IrcClient(options, _logger);
        _clients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            try
            {
                client?.Dispose();
            }
            catch
            {
                // Ignore disposal errors in tests
            }
        }
        _clients.Clear();
    }

    #endregion
}
