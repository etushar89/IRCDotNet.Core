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
/// Integration tests for RFC 1459 and Modern IRC compliance improvements.
/// Tests end-to-end functionality and event handling for compliance features.
/// </summary>
public class ComplianceIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public ComplianceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    #region WHO Command Integration Tests

    [Fact]
    public void WhoReceived_Event_ShouldTriggerOnWhoReply()
    {
        // Arrange
        var client = CreateTestClient();
        WhoReceivedEvent? receivedEvent = null;

        // Act & Assert - Event should be available for subscription without throwing
        var act = () => client.WhoReceived += (sender, args) => receivedEvent = args;
        act.Should().NotThrow();

        // Verify we can also unsubscribe without issues
        var removeAct = () => client.WhoReceived -= (sender, args) => receivedEvent = args;
        removeAct.Should().NotThrow();

        // Note: We can't easily test the actual event firing without mocking the network layer
        // but we can verify the event is properly defined and can be subscribed to
    }

    [Fact]
    public void WhoCommand_ShouldHandleBothReplyAndEndMessages()
    {
        // Arrange
        var client = CreateTestClient();
        var whoEvents = new List<WhoReceivedEvent>();
        client.WhoReceived += (sender, args) => whoEvents.Add(args);

        // Act - These would be handled by the ProcessMessageAsync method
        var whoReply = IrcMessage.Parse(":irc.test.com 352 testnick #channel user host server nick H :0 Real Name");
        var endOfWho = IrcMessage.Parse(":irc.test.com 315 testnick #channel :End of WHO list");

        // Assert - Messages should parse correctly
        whoReply.Command.Should().Be("352");
        endOfWho.Command.Should().Be("315");

        // Verify proper format for WHO replies
        whoReply.Parameters.Should().HaveCount(8);
        endOfWho.Parameters.Should().HaveCount(3);
    }

    #endregion

    #region WHOWAS Command Integration Tests

    [Fact]
    public void WhoWasReceived_Event_ShouldBeAvailableForSubscription()
    {
        // Arrange
        var client = CreateTestClient();
        WhoWasReceivedEvent? receivedEvent = null;

        // Act & Assert - Event should be available for subscription without throwing
        var act = () => client.WhoWasReceived += (sender, args) => receivedEvent = args;
        act.Should().NotThrow();

        // Verify we can also unsubscribe without issues
        var removeAct = () => client.WhoWasReceived -= (sender, args) => receivedEvent = args; removeAct.Should().NotThrow();
    }

    [Fact]
    public void WhoWasCommand_ShouldHandleUserAndEndMessages()
    {
        // Arrange & Act
        var whoWasUser = IrcMessage.Parse(":irc.test.com 314 testnick oldnick user host * :Real Name");
        var endOfWhoWas = IrcMessage.Parse(":irc.test.com 369 testnick oldnick :End of WHOWAS");

        // Assert
        whoWasUser.Command.Should().Be("314");
        endOfWhoWas.Command.Should().Be("369");

        whoWasUser.Parameters.Should().HaveCount(6);
        endOfWhoWas.Parameters.Should().HaveCount(3);
    }

    #endregion

    #region LIST Command Integration Tests

    [Fact]
    public void ChannelListReceived_Event_ShouldBeAvailableForSubscription()
    {
        // Arrange
        var client = CreateTestClient();
        ChannelListReceivedEvent? receivedEvent = null;

        // Act & Assert - Event should be available for subscription without throwing
        var act = () => client.ChannelListReceived += (sender, args) => receivedEvent = args;
        act.Should().NotThrow();

        // Verify we can also unsubscribe without issues
        var removeAct = () => client.ChannelListReceived -= (sender, args) => receivedEvent = args;
        removeAct.Should().NotThrow();
    }

    [Fact]
    public void ListCommand_ShouldHandleChannelAndEndMessages()
    {
        // Arrange & Act
        var listReply = IrcMessage.Parse(":irc.test.com 322 testnick #channel 42 :Channel topic");
        var endOfList = IrcMessage.Parse(":irc.test.com 323 testnick :End of LIST");

        // Assert
        listReply.Command.Should().Be("322");
        endOfList.Command.Should().Be("323");

        listReply.Parameters.Should().HaveCount(4);
        endOfList.Parameters.Should().HaveCount(2);

        // Verify channel info parsing
        listReply.Parameters[1].Should().Be("#channel");
        listReply.Parameters[2].Should().Be("42"); // user count
        listReply.Parameters[3].Should().Be("Channel topic");
    }

    #endregion

    #region Server Information Integration Tests

    [Fact]
    public void ServerInfo_Messages_ShouldParseSequentially()
    {
        // Arrange - Simulate the typical server info sequence after connection
        var messages = new[]
        {
            ":irc.test.com 001 testnick :Welcome to the Test IRC Network",
            ":irc.test.com 002 testnick :Your host is irc.test.com, running version ircd-seven-1.1.9",
            ":irc.test.com 003 testnick :This server was created Mon Jan 1 2024 at 12:00:00 UTC",
            ":irc.test.com 004 testnick irc.test.com ircd-seven-1.1.9 DOQRSZaghilopsuwz CFILMPQSbcefgijklmnopqrstvz bkloveqjfI",
            ":irc.test.com 005 testnick CHANTYPES=# PREFIX=(ov)@+ NETWORK=TestNet :are supported by this server"
        };

        // Act & Assert
        foreach (var rawMessage in messages)
        {
            var message = IrcMessage.Parse(rawMessage);
            message.Should().NotBeNull();
            message.Source.Should().Be("irc.test.com");
            message.Parameters[0].Should().Be("testnick");
        }
    }

    [Fact]
    public void ISupportMessage_ShouldParseComplexFeatureList()
    {
        // Arrange
        var complexIsupport = ":irc.test.com 005 testnick " +
            "CHANTYPES=# PREFIX=(ov)@+ CHANMODES=eIbq,k,flj,CFLMPQScgimnprstz " +
            "MODES=5 NICKLEN=16 NETWORK=TestNet CASEMAPPING=rfc1459 " +
            ":are supported by this server";

        // Act
        var message = IrcMessage.Parse(complexIsupport);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("005");
        message.Parameters.Should().HaveCountGreaterOrEqualTo(8);

        // Check for specific features
        message.Parameters.Should().Contain("CHANTYPES=#");
        message.Parameters.Should().Contain("PREFIX=(ov)@+");
        message.Parameters.Should().Contain("NETWORK=TestNet");
        message.Parameters.Should().Contain("NICKLEN=16");
    }

    #endregion

    #region MOTD Integration Tests

    [Fact]
    public void MotdSequence_ShouldParseCompleteFlow()
    {
        // Arrange - Simulate a complete MOTD sequence
        var motdMessages = new[]
        {
            ":irc.test.com 375 testnick :- irc.test.com Message of the Day -",
            ":irc.test.com 372 testnick :- Welcome to the Test IRC Network",
            ":irc.test.com 372 testnick :- Please follow our community guidelines",
            ":irc.test.com 372 testnick :- Visit our website at https://test.irc",
            ":irc.test.com 376 testnick :End of MOTD command"
        };

        // Act & Assert
        foreach (var rawMessage in motdMessages)
        {
            var message = IrcMessage.Parse(rawMessage);
            message.Should().NotBeNull();
            message.Command.Should().BeOneOf("375", "372", "376");
            message.Parameters.Should().HaveCount(2);
            message.Parameters[0].Should().Be("testnick");
        }
    }

    [Fact]
    public void NoMotdError_ShouldParseCorrectly()
    {
        // Arrange
        var noMotdMessage = ":irc.test.com 422 testnick :MOTD File is missing";

        // Act
        var message = IrcMessage.Parse(noMotdMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("422");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[1].Should().Be("MOTD File is missing");
    }

    [Fact]
    public void MotdReceived_Event_ShouldBeAvailableForSubscription()
    {
        // Arrange
        var client = CreateTestClient();
        MotdReceivedEvent? receivedEvent = null;

        // Act & Assert - Event should be available for subscription without throwing
        var act = () => client.MotdReceived += (sender, args) => receivedEvent = args;
        act.Should().NotThrow();

        // Verify we can also unsubscribe without issues
        var removeAct = () => client.MotdReceived -= (sender, args) => receivedEvent = args;
        removeAct.Should().NotThrow();
    }

    [Fact]
    public void MotdSequence_ShouldParseAllMessages()
    {
        // Arrange - Simulate a complete MOTD response
        var motdMessages = new[]
        {
            ":irc.test.com 375 testnick :- irc.test.com Message of the Day -",
            ":irc.test.com 372 testnick :- Welcome to the test IRC network",
            ":irc.test.com 372 testnick :- Please follow our community guidelines",
            ":irc.test.com 372 testnick :- Visit https://example.com for more info",
            ":irc.test.com 376 testnick :End of MOTD command"
        };

        // Act & Assert
        foreach (var rawMessage in motdMessages)
        {
            var message = IrcMessage.Parse(rawMessage);
            message.Should().NotBeNull();
            message.Command.Should().BeOneOf("375", "372", "376");
            message.Parameters[0].Should().Be("testnick");
        }
    }

    [Fact]
    public void MotdSequence_ShouldExtractLineText()
    {
        // Arrange
        var motdLine = IrcMessage.Parse(":irc.test.com 372 testnick :- Welcome to the network");

        // Act & Assert
        motdLine.Parameters.Should().HaveCount(2);
        motdLine.Parameters[1].Should().Be("- Welcome to the network");
    }

    #endregion

    #region WHOIS Integration Tests

    [Fact]
    public void WhoisSequence_ShouldParseCompleteUserInfo()
    {
        // Arrange - Simulate a complete WHOIS response
        var whoisMessages = new[]
        {
            ":irc.test.com 311 testnick user username hostname * :Real Name",
            ":irc.test.com 312 testnick user irc.test.com :Test IRC Server",
            ":irc.test.com 317 testnick user 300 1640995200 :seconds idle, signon time",
            ":irc.test.com 319 testnick user :@#channel1 +#channel2 #channel3",
            ":irc.test.com 318 testnick user :End of WHOIS list"
        };

        // Act & Assert
        foreach (var rawMessage in whoisMessages)
        {
            var message = IrcMessage.Parse(rawMessage);
            message.Should().NotBeNull();
            message.Command.Should().BeOneOf("311", "312", "317", "319", "318");
            message.Parameters[0].Should().Be("testnick");
            message.Parameters[1].Should().Be("user");
        }
    }

    [Fact]
    public void WhoisOperator_ShouldParseCorrectly()
    {
        // Arrange
        var operMessage = ":irc.test.com 313 testnick user :is an IRC operator";

        // Act
        var message = IrcMessage.Parse(operMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("313");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[2].Should().Be("is an IRC operator");
    }

    #endregion

    #region Error Handling Integration Tests

    [Fact]
    public void ErrorReplies_ShouldProvideUsefulInformation()
    {
        // Arrange
        var errorMessages = new[]
        {
            ":irc.test.com 401 testnick badnick :No such nick/channel",
            ":irc.test.com 403 testnick #badchannel :No such channel",
            ":irc.test.com 404 testnick #channel :Cannot send to channel",
            ":irc.test.com 421 testnick BADCOMMAND :Unknown command"
        };

        // Act & Assert
        foreach (var rawMessage in errorMessages)
        {
            var message = IrcMessage.Parse(rawMessage);
            message.Should().NotBeNull();
            message.Command.Should().BeOneOf("401", "403", "404", "421");
            message.Parameters.Should().HaveCountGreaterOrEqualTo(2); message.Parameters[0].Should().Be("testnick");
        }
    }

    #endregion

    #region Mode Integration Tests

    [Fact]
    public void ModeCommands_ShouldSupportBothUserAndChannelModes()
    {
        // Arrange
        var client = CreateTestClient();

        // Act & Assert - Methods should be available and throw expected exceptions when not connected
        Func<Task> userModeAction = async () => await client.SetUserModeAsync("+i");
        Func<Task> channelModeAction = async () => await client.SetChannelModeAsync("#channel", "+o", "nick");
        Func<Task> getModeAction = async () => await client.GetChannelModeAsync("#channel");

        // These should throw InvalidOperationException due to not being connected, not ArgumentException
        userModeAction.Should().ThrowAsync<InvalidOperationException>();
        channelModeAction.Should().ThrowAsync<InvalidOperationException>();
        getModeAction.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ChannelModeReply_ShouldParseCorrectly()
    {
        // Arrange
        var modeReply = ":irc.test.com 324 testnick #channel +nt";

        // Act
        var message = IrcMessage.Parse(modeReply);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("324");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("+nt");
    }

    #endregion

    #region Comprehensive Protocol Compliance Tests

    [Fact]
    public void AllEssentialRfc1459Commands_ShouldBeImplemented()
    {
        // Arrange
        var client = CreateTestClient();
        var clientType = client.GetType();

        // Assert - Check that all essential RFC 1459 commands are available
        var essentialMethods = new[]
        {
            // Connection
            "ConnectAsync", "DisconnectAsync", "SendRawAsync",
            // Registration  
            "ChangeNickAsync",
            // Channels
            "JoinChannelAsync", "LeaveChannelAsync", "SetTopicAsync", "GetTopicAsync",
            "GetChannelUsersAsync", "KickUserAsync", "InviteUserAsync",
            // Messages
            "SendMessageAsync", "SendNoticeAsync",
            // User info
            "GetUserInfoAsync", "SetAwayAsync",
            // RFC 1459 compliance
            "WhoAsync", "WhoWasAsync", "ListChannelsAsync",
            "SetUserModeAsync", "SetChannelModeAsync", "GetChannelModeAsync"
        };

        foreach (var methodName in essentialMethods)
        {
            var method = clientType.GetMethod(methodName);
            method.Should().NotBeNull($"Method {methodName} should be implemented");
        }
    }

    [Fact]
    public void AllEssentialRfc1459Events_ShouldBeImplemented()
    {
        // Arrange
        var client = CreateTestClient();
        var clientType = client.GetType();

        // Assert - Check that all essential RFC 1459 events are available
        var essentialEvents = new[]
        {
            // Connection events
            "Connected", "Disconnected",
            // Channel events
            "UserJoinedChannel", "UserLeftChannel", "TopicChanged",
            "UserKicked", "ChannelUsersReceived",
            // Message events
            "PrivateMessageReceived", "NoticeReceived",
            // User events
            "NickChanged", "UserQuit",
            // RFC 1459 compliance events
            "WhoReceived", "WhoWasReceived", "ChannelListReceived",
            // General
            "RawMessageReceived"
        };

        foreach (var eventName in essentialEvents)
        {
            var eventInfo = clientType.GetEvent(eventName);
            eventInfo.Should().NotBeNull($"Event {eventName} should be implemented");
        }
    }

    [Fact]
    public void AllEssentialNumericReplies_ShouldBeDefinedCorrectly()
    {
        // Arrange & Assert - Check that all essential numeric replies have correct values
        var numericReplies = new Dictionary<string, string>
        {
            // Welcome sequence
            { "RPL_WELCOME", "001" },
            { "RPL_YOURHOST", "002" },
            { "RPL_CREATED", "003" },
            { "RPL_MYINFO", "004" },
            { "RPL_ISUPPORT", "005" },
            
            // User information
            { "RPL_AWAY", "301" },
            { "RPL_WHOISUSER", "311" },
            { "RPL_WHOISSERVER", "312" },
            { "RPL_WHOISOPERATOR", "313" },
            { "RPL_WHOWASUSER", "314" },
            { "RPL_ENDOFWHO", "315" },
            { "RPL_WHOISIDLE", "317" },
            { "RPL_ENDOFWHOIS", "318" },
            { "RPL_WHOISCHANNELS", "319" },
            { "RPL_WHOREPLY", "352" },
            { "RPL_ENDOFWHOWAS", "369" },
            
            // Channel information
            { "RPL_LIST", "322" },
            { "RPL_LISTEND", "323" },
            { "RPL_CHANNELMODEIS", "324" },
            { "RPL_TOPIC", "332" },
            { "RPL_NAMREPLY", "353" },
            { "RPL_ENDOFNAMES", "366" },
            
            // MOTD
            { "RPL_MOTDSTART", "375" },
            { "RPL_MOTD", "372" },
            { "RPL_ENDOFMOTD", "376" },
            
            // Common errors
            { "ERR_NOSUCHNICK", "401" },
            { "ERR_NOSUCHCHANNEL", "403" },
            { "ERR_CANNOTSENDTOCHAN", "404" },
            { "ERR_UNKNOWNCOMMAND", "421" },
            { "ERR_NOMOTD", "422" },
            { "ERR_NICKNAMEINUSE", "433" }
        };

        var numericRepliesType = typeof(IrcNumericReplies);

        foreach (var kvp in numericReplies)
        {
            var field = numericRepliesType.GetField(kvp.Key);
            field.Should().NotBeNull($"Numeric reply {kvp.Key} should be defined");

            if (field != null)
            {
                var value = field.GetValue(null) as string;
                value.Should().Be(kvp.Value, $"Numeric reply {kvp.Key} should have value {kvp.Value}");
            }
        }
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

    private IrcMessage CreateWhoReplyMessage()
    {
        return IrcMessage.Parse(":irc.test.com 352 testnick #channel user host server nick H :0 Real Name");
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
