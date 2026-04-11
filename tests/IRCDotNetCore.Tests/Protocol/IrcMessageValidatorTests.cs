using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcMessageValidatorTests
{
    [Fact]
    public void ValidateMessageLength_ValidMessage_ReturnsValid()
    {
        // Arrange
        var message = "PRIVMSG #test :Hello World";

        // Act
        var result = IrcMessageValidator.ValidateMessageLength(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessageLength_TooLong_ReturnsInvalid()
    {
        // Arrange
        var message = new string('a', 600);

        // Act
        var result = IrcMessageValidator.ValidateMessageLength(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum", result.ErrorMessage);
    }

    [Fact]
    public void ValidateNickname_ValidNickname_ReturnsValid()
    {
        // Arrange
        var nickname = "TestUser";

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateNickname_TooLong_ReturnsInvalid()
    {
        // Arrange
        var nickname = "ThisNicknameIsWayTooLongForIRC31";

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum", result.ErrorMessage);
    }

    [Fact]
    public void ValidateNickname_ThirtyChars_ReturnsValid()
    {
        // Arrange - exactly 30 characters, should be valid
        var nickname = "abcdefghijklmnopqrstuvwxyzABCD";

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateNickname_ModernLengthNick_ReturnsValid()
    {
        // Arrange - 15 char nick, common on modern servers
        var nickname = "LongNickname123";

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateNickname_InvalidCharacters_ReturnsInvalid()
    {
        // Arrange
        var nickname = "Test User"; // Space is invalid

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("invalid characters", result.ErrorMessage);
    }

    [Fact]
    public void ValidateNickname_StartsWithDigit_ReturnsInvalid()
    {
        // Arrange
        var nickname = "1TestUser";

        // Act
        var result = IrcMessageValidator.ValidateNickname(nickname);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("must start with", result.ErrorMessage);
    }

    [Fact]
    public void ValidateChannelName_ValidChannel_ReturnsValid()
    {
        // Arrange
        var channel = "#test";

        // Act
        var result = IrcMessageValidator.ValidateChannelName(channel);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateChannelName_InvalidPrefix_ReturnsInvalid()
    {
        // Arrange
        var channel = "test"; // Missing prefix

        // Act
        var result = IrcMessageValidator.ValidateChannelName(channel);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("invalid", result.ErrorMessage);
    }

    [Fact]
    public void ValidateChannelName_ContainsForbiddenCharacters_ReturnsInvalid()
    {
        // Arrange
        var channel = "#test channel"; // Space is forbidden

        // Act
        var result = IrcMessageValidator.ValidateChannelName(channel);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("forbidden characters", result.ErrorMessage);
    }

    [Fact]
    public void ValidateParameters_ValidParameters_ReturnsValid()
    {
        // Arrange
        var parameters = new List<string> { "#test", "Hello World" };

        // Act
        var result = IrcMessageValidator.ValidateParameters(parameters);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateParameters_TooManyParameters_ReturnsInvalid()
    {
        // Arrange
        var parameters = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            parameters.Add($"param{i}");
        }

        // Act
        var result = IrcMessageValidator.ValidateParameters(parameters);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum", result.ErrorMessage);
    }

    [Fact]
    public void ValidateParameters_MiddleParameterWithSpace_ReturnsInvalid()
    {
        // Arrange
        var parameters = new List<string> { "param with space", "lastparam" };

        // Act
        var result = IrcMessageValidator.ValidateParameters(parameters);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("contains spaces", result.ErrorMessage);
    }

    [Fact]
    public void ValidateMessage_ValidMessage_ReturnsValid()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "PRIVMSG",
            Parameters = new List<string> { "#test", "Hello World" }
        };

        // Act
        var result = IrcMessageValidator.ValidateMessage(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_EmptyCommand_ReturnsInvalid()
    {
        // Arrange
        var message = new IrcMessage
        {
            Command = "",
            Parameters = new List<string> { "#test", "Hello World" }
        };

        // Act
        var result = IrcMessageValidator.ValidateMessage(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("cannot be null or empty", result.ErrorMessage);
    }
}
