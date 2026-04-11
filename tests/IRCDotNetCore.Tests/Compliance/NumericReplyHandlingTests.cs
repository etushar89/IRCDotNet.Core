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
/// Tests for enhanced numeric reply handling and message processing improvements
/// added for RFC 1459 and Modern IRC compliance
/// </summary>
public class NumericReplyHandlingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public NumericReplyHandlingTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    #region Server Information Processing Tests

    [Theory]
    [InlineData("002", "testnick", "Your host is irc.example.com, running version ircd-seven-1.1.9")]
    [InlineData("003", "testnick", "This server was created Mon Jan 1 2024 at 12:00:00 UTC")]
    [InlineData("004", "testnick", "irc.example.com ircd-seven-1.1.9 DOQRSZaghilopsuwz CFILMPQSbcefgijklmnopqrstvz bkloveqjfI")]
    public void ServerInfo_Messages_ShouldParseCorrectly(string numeric, string target, string info)
    {
        // Arrange
        var rawMessage = $":{ServerName} {numeric} {target} :{info}";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be(numeric);
        message.Parameters.Should().HaveCountGreaterOrEqualTo(2);
        message.Parameters[0].Should().Be(target);
        message.Parameters[1].Should().Be(info);
    }

    [Fact]
    public void ISupportMessage_ShouldParseFeatureTokens()
    {
        // Arrange
        var rawMessage = $":{ServerName} 005 testnick CHANTYPES=# PREFIX=(ov)@+ NETWORK=TestNet NICKLEN=16 CHANNELLEN=50 :are supported by this server";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("005");
        message.Parameters.Should().HaveCountGreaterOrEqualTo(6);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters.Should().Contain("CHANTYPES=#");
        message.Parameters.Should().Contain("PREFIX=(ov)@+");
        message.Parameters.Should().Contain("NETWORK=TestNet");
    }

    #endregion

    #region MOTD Processing Tests

    [Theory]
    [InlineData("375", "testnick", "- irc.example.com Message of the Day -")]
    [InlineData("372", "testnick", "- Welcome to the test IRC network")]
    [InlineData("372", "testnick", "- Please follow our community guidelines")]
    [InlineData("376", "testnick", "End of MOTD command")]
    public void MotdMessages_ShouldParseCorrectly(string numeric, string target, string motdLine)
    {
        // Arrange
        var rawMessage = $":{ServerName} {numeric} {target} :{motdLine}";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be(numeric);
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be(target);
        message.Parameters[1].Should().Be(motdLine);
    }

    [Fact]
    public void NoMotdError_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 422 testnick :MOTD File is missing";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("422");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("MOTD File is missing");
    }

    #endregion

    #region WHO Reply Processing Tests

    [Fact]
    public void WhoReply_ShouldParseAllFields()
    {
        // Arrange
        var rawMessage = $":{ServerName} 352 testnick #channel user host server nick H :0 Real Name";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("352");
        message.Parameters.Should().HaveCount(8);
        message.Parameters[0].Should().Be("testnick");  // target
        message.Parameters[1].Should().Be("#channel");  // channel
        message.Parameters[2].Should().Be("user");      // username
        message.Parameters[3].Should().Be("host");      // hostname
        message.Parameters[4].Should().Be("server");    // server
        message.Parameters[5].Should().Be("nick");      // nick
        message.Parameters[6].Should().Be("H");         // flags (H=here, G=away)
        message.Parameters[7].Should().Be("0 Real Name"); // hopcount + real name
    }

    [Fact]
    public void WhoReply_WithAwayUser_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 352 testnick #channel user host server nick G* :0 Real Name";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("352");
        message.Parameters[6].Should().Be("G*"); // G = away, * = oper
    }

    [Fact]
    public void EndOfWho_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 315 testnick #channel :End of WHO list";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("315");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("End of WHO list");
    }

    #endregion

    #region WHOWAS Reply Processing Tests

    [Fact]
    public void WhoWasReply_ShouldParseAllFields()
    {
        // Arrange
        var rawMessage = $":{ServerName} 314 testnick oldnick user host * :Real Name";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("314");
        message.Parameters.Should().HaveCount(6);
        message.Parameters[0].Should().Be("testnick");  // target
        message.Parameters[1].Should().Be("oldnick");   // nick
        message.Parameters[2].Should().Be("user");      // username
        message.Parameters[3].Should().Be("host");      // hostname
        message.Parameters[4].Should().Be("*");         // unused
        message.Parameters[5].Should().Be("Real Name"); // real name
    }

    [Fact]
    public void EndOfWhoWas_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 369 testnick oldnick :End of WHOWAS";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("369");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("oldnick");
        message.Parameters[2].Should().Be("End of WHOWAS");
    }

    #endregion

    #region LIST Reply Processing Tests

    [Fact]
    public void ListReply_ShouldParseAllFields()
    {
        // Arrange
        var rawMessage = $":{ServerName} 322 testnick #channel 42 :This is a test channel topic";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("322");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[0].Should().Be("testnick");    // target
        message.Parameters[1].Should().Be("#channel");    // channel
        message.Parameters[2].Should().Be("42");          // user count
        message.Parameters[3].Should().Be("This is a test channel topic"); // topic
    }

    [Fact]
    public void ListReply_WithEmptyTopic_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 322 testnick #channel 15 :";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("322");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[3].Should().Be(""); // empty topic
    }

    [Fact]
    public void EndOfList_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 323 testnick :End of LIST";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("323");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("End of LIST");
    }

    #endregion

    #region WHOIS Reply Processing Tests

    [Fact]
    public void WhoisUser_ShouldParseAllFields()
    {
        // Arrange
        var rawMessage = $":{ServerName} 311 testnick user username hostname * :Real Name";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("311");
        message.Parameters.Should().HaveCount(6);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("user");
        message.Parameters[2].Should().Be("username");
        message.Parameters[3].Should().Be("hostname");
        message.Parameters[4].Should().Be("*");
        message.Parameters[5].Should().Be("Real Name");
    }

    [Fact]
    public void WhoisServer_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 312 testnick user irc.example.com :Test IRC Server";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("312");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[2].Should().Be("irc.example.com");
        message.Parameters[3].Should().Be("Test IRC Server");
    }

    [Fact]
    public void WhoisOperator_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 313 testnick user :is an IRC operator";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("313");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[2].Should().Be("is an IRC operator");
    }

    [Fact]
    public void WhoisIdle_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 317 testnick user 300 1640995200 :seconds idle, signon time";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("317");
        message.Parameters.Should().HaveCount(5);
        message.Parameters[2].Should().Be("300");       // idle seconds
        message.Parameters[3].Should().Be("1640995200"); // signon time
        message.Parameters[4].Should().Be("seconds idle, signon time");
    }

    [Fact]
    public void WhoisChannels_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 319 testnick user :@#channel1 +#channel2 #channel3";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("319");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[2].Should().Be("@#channel1 +#channel2 #channel3");
    }

    [Fact]
    public void EndOfWhois_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 318 testnick user :End of WHOIS list";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("318");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[2].Should().Be("End of WHOIS list");
    }

    #endregion

    #region Error Reply Processing Tests

    [Theory]
    [InlineData("401", "No such nick/channel")]
    [InlineData("402", "No such server")]
    [InlineData("403", "No such channel")]
    [InlineData("404", "Cannot send to channel")]
    [InlineData("421", "Unknown command")]
    public void ErrorReplies_ShouldParseCorrectly(string numeric, string errorText)
    {
        // Arrange
        var rawMessage = $":{ServerName} {numeric} testnick target :{errorText}";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be(numeric);
        message.Parameters.Should().HaveCountGreaterOrEqualTo(2);
        message.Parameters[0].Should().Be("testnick");
    }

    [Fact]
    public void NoSuchNick_WithTargetNick_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 401 testnick badnick :No such nick/channel";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("401");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[1].Should().Be("badnick");  // target that doesn't exist
        message.Parameters[2].Should().Be("No such nick/channel");
    }

    #endregion

    #region Away Reply Processing Tests

    [Fact]
    public void AwayReply_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 301 testnick user :Away message here";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("301");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[0].Should().Be("testnick");
        message.Parameters[1].Should().Be("user");
        message.Parameters[2].Should().Be("Away message here");
    }

    #endregion

    #region Mode Reply Processing Tests

    [Fact]
    public void ChannelModeIs_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 324 testnick #channel +nt";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("324");
        message.Parameters.Should().HaveCount(3);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("+nt");
    }

    [Fact]
    public void ChannelModeIs_WithParameters_ShouldParseCorrectly()
    {
        // Arrange
        var rawMessage = $":{ServerName} 324 testnick #channel +lk 50 password";

        // Act
        var message = IrcMessage.Parse(rawMessage);

        // Assert
        message.Should().NotBeNull();
        message.Command.Should().Be("324");
        message.Parameters.Should().HaveCount(5);
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("+lk");
        message.Parameters[3].Should().Be("50");        // limit
        message.Parameters[4].Should().Be("password");  // key
    }

    #endregion

    #region Message Format Edge Cases

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void EmptyOrWhitespaceMessages_ShouldThrowException(string invalidMessage)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IrcMessage.Parse(invalidMessage));
    }

    [Fact]
    public void MessageWithoutCommand_ShouldThrowException()
    {
        // Arrange
        var invalidMessage = ":source.example.com";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => IrcMessage.Parse(invalidMessage));
    }

    [Fact]
    public void MessageWithTagsButNoCommand_ShouldThrowException()
    {
        // Arrange
        var invalidMessage = "@tag=value :source.example.com";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => IrcMessage.Parse(invalidMessage));
    }

    #endregion

    #region Constants and Helper Properties

    private const string ServerName = "irc.test.com";

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
