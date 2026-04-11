using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Configuration;

public class IrcClientOptionsBuilderRateLimitTests
{
    [Fact]
    public void WithRateLimit_Enabled_SetsProperties()
    {
        // Arrange
        var config = new IRCDotNet.Core.Protocol.RateLimitConfig(2.0, 10);

        // Act
        var options = new IrcClientOptionsBuilder()
            .WithNick("TestBot")
            .WithUserName("testbot")
            .WithRealName("Test Bot")
            .AddServer("irc.libera.chat", 6667)
            .WithRateLimit(true, config)
            .Build();

        // Assert
        Assert.True(options.EnableRateLimit);
        Assert.Equal(config, options.RateLimitConfig);
    }

    [Fact]
    public void WithoutRateLimit_DisablesRateLimit()
    {
        // Act
        var options = new IrcClientOptionsBuilder()
            .WithNick("TestBot")
            .WithUserName("testbot")
            .WithRealName("Test Bot")
            .AddServer("irc.libera.chat", 6667)
            .WithoutRateLimit()
            .Build();

        // Assert
        Assert.False(options.EnableRateLimit);
        Assert.Null(options.RateLimitConfig);
    }

    [Fact]
    public void FromTemplate_CopiesRateLimitSettings()
    {
        // Arrange
        var config = new IRCDotNet.Core.Protocol.RateLimitConfig(1.5, 8);
        var template = new IrcClientOptions
        {
            EnableRateLimit = false,
            RateLimitConfig = config,
            Nick = "TemplateBot",
            UserName = "template",
            RealName = "Template Bot",
            Server = "irc.example.com",
            Port = 6667
        };

        // Act
        var options = IrcClientOptionsBuilder.FromTemplate(template)
            .AddServer("irc.libera.chat", 6667)
            .Build();

        // Assert
        Assert.False(options.EnableRateLimit);
        Assert.Equal(config, options.RateLimitConfig);
    }

    [Fact]
    public void DefaultBuilder_UsesDefaultRateLimitSettings()
    {
        // Act
        var options = new IrcClientOptionsBuilder()
            .WithNick("TestBot")
            .WithUserName("testbot")
            .WithRealName("Test Bot")
            .AddServer("irc.libera.chat", 6667)
            .Build();

        // Assert
        Assert.True(options.EnableRateLimit); // Default should be true
        Assert.Null(options.RateLimitConfig); // Default should be null (use system defaults)
    }
}
