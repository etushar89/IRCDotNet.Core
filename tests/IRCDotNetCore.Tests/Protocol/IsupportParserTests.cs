using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IsupportParserTests
{
    [Fact]
    public void ParseIsupport_ValidMessage_ParsesCorrectly()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "CHANTYPES=#&+!",
                "NICKLEN=9",
                "CHANNELLEN=50",
                "CASEMAPPING=rfc1459",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(message);

        // Assert
        Assert.Equal("#&+!", parser.ChannelTypes);
        Assert.Equal(9, parser.MaxNicknameLength);
        Assert.Equal(50, parser.MaxChannelLength);
        Assert.Equal(CaseMappingType.Rfc1459, parser.CaseMapping);
    }

    [Fact]
    public void ParseIsupport_BooleanFeature_ParsesCorrectly()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "NAMESX",
                "UHNAMES",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(message);

        // Assert
        Assert.True(parser.GetBoolValue("NAMESX"));
        Assert.True(parser.GetBoolValue("UHNAMES"));
        Assert.False(parser.GetBoolValue("NONEXISTENT"));
    }

    [Fact]
    public void ParseIsupport_NegatedFeature_RemovesFeature()
    {
        // Arrange
        var parser = new IsupportParser();

        // First add a feature
        var addMessage = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "NICKLEN=9",
                "are supported by this server"
            }
        };
        parser.ParseIsupport(addMessage);

        // Then negate it
        var negateMessage = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "-NICKLEN",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(negateMessage);

        // Assert
        Assert.Equal(9, parser.GetIntValue("NICKLEN", 9)); // Should return default
        Assert.False(parser.IsFeatureSupported("NICKLEN"));
    }

    [Fact]
    public void ParseIsupport_ChannelModes_ParsesCorrectly()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "CHANMODES=beI,kfL,lj,psmntirRcOAQKVCuzNSMTG",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(message);

        // Assert
        var modeInfo = parser.GetChannelModeInfo();
        Assert.NotNull(modeInfo);
        Assert.Equal("beI", modeInfo.ListModes);
        Assert.Equal("kfL", modeInfo.AlwaysParameterModes);
        Assert.Equal("lj", modeInfo.SetParameterModes);
        Assert.Equal("psmntirRcOAQKVCuzNSMTG", modeInfo.SimpleFlags);
    }

    [Fact]
    public void ParseIsupport_ChannelLimits_ParsesCorrectly()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "CHANLIMIT=#&:120,+:50",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(message);

        // Assert
        Assert.Equal(120, parser.GetChannelLimit('#'));
        Assert.Equal(120, parser.GetChannelLimit('&'));
        Assert.Equal(50, parser.GetChannelLimit('+'));
    }

    [Fact]
    public void ParseIsupport_Prefix_ParsesCorrectly()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "PREFIX=(ov)@+",
                "are supported by this server"
            }
        };

        // Act
        parser.ParseIsupport(message);

        // Assert
        var (modes, prefixes) = parser.ChannelModePrefix;
        Assert.Equal("ov", modes);
        Assert.Equal("@+", prefixes);
    }

    [Fact]
    public void CaseMapping_DifferentTypes_ReturnsCorrectEnum()
    {
        // Arrange & Act & Assert
        var parser = new IsupportParser();

        // Test ASCII
        var asciiMessage = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string> { "TestUser", "CASEMAPPING=ascii", "test" }
        };
        parser.ParseIsupport(asciiMessage);
        Assert.Equal(CaseMappingType.Ascii, parser.CaseMapping);

        // Test RFC1459-strict
        parser.Clear();
        var strictMessage = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string> { "TestUser", "CASEMAPPING=rfc1459-strict", "test" }
        };
        parser.ParseIsupport(strictMessage);
        Assert.Equal(CaseMappingType.Rfc1459Strict, parser.CaseMapping);

        // Test RFC1459 (default)
        parser.Clear();
        var rfc1459Message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string> { "TestUser", "CASEMAPPING=rfc1459", "test" }
        };
        parser.ParseIsupport(rfc1459Message);
        Assert.Equal(CaseMappingType.Rfc1459, parser.CaseMapping);
    }

    [Fact]
    public void GetStringValue_ExistingKey_ReturnsValue()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "NETWORK=TestNetwork",
                "are supported by this server"
            }
        };
        parser.ParseIsupport(message);

        // Act
        var result = parser.GetStringValue("NETWORK");

        // Assert
        Assert.Equal("TestNetwork", result);
    }

    [Fact]
    public void GetStringValue_NonExistentKey_ReturnsDefault()
    {
        // Arrange
        var parser = new IsupportParser();

        // Act
        var result = parser.GetStringValue("NONEXISTENT", "default");

        // Assert
        Assert.Equal("default", result);
    }

    [Fact]
    public void GetIntValue_ExistingKey_ReturnsValue()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "MAXTARGETS=4",
                "are supported by this server"
            }
        };
        parser.ParseIsupport(message);

        // Act
        var result = parser.GetIntValue("MAXTARGETS");

        // Assert
        Assert.Equal(4, result);
    }

    [Fact]
    public void IsFeatureSupported_ExistingFeature_ReturnsTrue()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "NAMESX",
                "are supported by this server"
            }
        };
        parser.ParseIsupport(message);

        // Act
        var result = parser.IsFeatureSupported("NAMESX");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Clear_RemovesAllFeatures()
    {
        // Arrange
        var parser = new IsupportParser();
        var message = new IrcMessage
        {
            Command = "005",
            Parameters = new List<string>
            {
                "TestUser",
                "NICKLEN=9",
                "are supported by this server"
            }
        };
        parser.ParseIsupport(message);

        // Act
        parser.Clear();

        // Assert
        Assert.False(parser.IsFeatureSupported("NICKLEN"));
        Assert.Empty(parser.Features);
    }
}
