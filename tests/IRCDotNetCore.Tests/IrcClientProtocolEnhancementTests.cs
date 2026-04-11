using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IRCDotNet.Tests;

/// <summary>
/// Tests for protocol enhancement integrations in IrcClient
/// </summary>
public class IrcClientProtocolEnhancementTests : IDisposable
{
    private readonly IrcClient _client;
    private readonly IrcClientOptions _options;

    public IrcClientProtocolEnhancementTests()
    {
        _options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            UseSsl = false
        };

        _client = new IrcClient(_options, NullLogger<IrcClient>.Instance);
    }

    [Fact]
    public void IrcClient_ShouldHaveProtocolEnhancementsInitialized()
    {
        // Verify that protocol enhancements are available
        // These methods should exist and return default values before connection
        _client.GetServerMaxNicknameLength().Should().Be(9); // Default from IsupportParser
        _client.GetServerMaxChannelLength().Should().Be(50); // Default from IsupportParser
        _client.GetServerChannelTypes().Should().Be("#&+!"); // Default from IsupportParser
        _client.GetServerNetworkName().Should().BeNull(); // No network name before connection
    }

    [Fact]
    public void IrcClient_ShouldValidateNicknames()
    {
        // Test nickname validation (modern servers commonly support up to 30 characters)
        _client.IsValidNickname("validnick").Should().BeTrue();
        _client.IsValidNickname("TestBot1").Should().BeTrue();
        _client.IsValidNickname("LongerModernNickname").Should().BeTrue(); // 20 chars - valid on modern servers
        _client.IsValidNickname("").Should().BeFalse();
        _client.IsValidNickname("123invalid").Should().BeFalse(); // Can't start with number
        _client.IsValidNickname("ThisNicknameIsWayTooLongForIRC31").Should().BeFalse(); // 31 characters - too long
    }

    [Fact]
    public void IrcClient_ShouldValidateChannelNames()
    {
        // Test channel name validation
        _client.IsValidChannelName("#validchannel").Should().BeTrue();
        _client.IsValidChannelName("&anotherchannel").Should().BeTrue();
        _client.IsValidChannelName("invalidchannel").Should().BeFalse(); // No prefix
        _client.IsValidChannelName("").Should().BeFalse();
    }

    [Fact]
    public void IrcClient_ShouldCompareNicknamesWithCaseMapping()
    {
        // Test case-insensitive nickname comparison using default RFC1459 mapping
        _client.NicknamesEqual("TestBot", "testbot").Should().BeTrue();
        _client.NicknamesEqual("User[1]", "user{1}").Should().BeTrue(); // RFC1459 mapping
        _client.NicknamesEqual("DifferentUser", "AnotherUser").Should().BeFalse();
    }

    [Fact]
    public void IrcClient_ShouldCompareChannelNamesWithCaseMapping()
    {
        // Test case-insensitive channel comparison using default RFC1459 mapping
        _client.ChannelNamesEqual("#TestChannel", "#testchannel").Should().BeTrue();
        _client.ChannelNamesEqual("#Channel[1]", "#channel{1}").Should().BeTrue(); // RFC1459 mapping
        _client.ChannelNamesEqual("#DifferentChannel", "#AnotherChannel").Should().BeFalse();
    }

    [Fact]
    public void IrcClient_ShouldEncodeMessages()
    {
        // Test message encoding
        var message = "Hello, IRC!";
        var encoded = _client.EncodeMessage(message);

        encoded.Should().NotBeNull();
        encoded.Length.Should().BeGreaterThan(0);

        // Verify it can be decoded back
        var decoded = System.Text.Encoding.UTF8.GetString(encoded);
        decoded.Should().Be(message);
    }

    [Fact]
    public void SendRawAsync_WithInvalidMessage_ShouldThrowForLength()
    {
        // Create a very long message that exceeds IRC limits
        var longMessage = new string('x', 600); // Exceeds 510 character limit

        // Should throw for message length validation even when not connected
        Func<Task> act = async () => await _client.SendRawAsync(longMessage);
        act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*length*");
    }

    [Fact]
    public void SendRawAsync_WithNullOrEmptyMessage_ShouldThrow()
    {
        // Test null message
        Func<Task> act1 = async () => await _client.SendRawAsync(null!);
        act1.Should().ThrowAsync<ArgumentException>();

        // Test empty message
        Func<Task> act2 = async () => await _client.SendRawAsync("");
        act2.Should().ThrowAsync<ArgumentException>();

        // Test whitespace message
        Func<Task> act3 = async () => await _client.SendRawAsync("   ");
        act3.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
