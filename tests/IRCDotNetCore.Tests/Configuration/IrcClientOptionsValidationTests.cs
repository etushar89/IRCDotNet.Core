using System;
using System.Collections.Generic;
using IRCDotNet.Core.Configuration;
using Xunit;

namespace IRCDotNet.Tests.Configuration;

/// <summary>
/// Comprehensive tests for IRC client configuration validation and edge cases
/// </summary>
public class IrcClientOptionsValidationTests
{
    [Fact]
    public void Validate_WithValidConfiguration_ShouldNotThrow()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidServer_ShouldThrowArgumentException(string server)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = server,
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("Server", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void Validate_WithInvalidPort_ShouldThrowArgumentOutOfRangeException(int port)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = port,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Port", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidNick_ShouldThrowArgumentException(string nick)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = nick,
            UserName = "testuser",
            RealName = "Test Bot"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("Nick", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidUserName_ShouldThrowArgumentException(string userName)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = userName,
            RealName = "Test Bot"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("UserName", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidRealName_ShouldThrowArgumentException(string realName)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = realName
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("RealName", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5000)]
    public void Validate_WithInvalidConnectionTimeout_ShouldThrowArgumentOutOfRangeException(int timeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ConnectionTimeoutMs = timeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30000)]
    public void Validate_WithInvalidReadTimeout_ShouldThrowArgumentOutOfRangeException(int timeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ReadTimeoutMs = timeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60000)]
    public void Validate_WithInvalidPingTimeout_ShouldThrowArgumentOutOfRangeException(int timeout)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            PingTimeoutMs = timeout
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30000)]
    public void Validate_WithInvalidPingInterval_ShouldThrowArgumentOutOfRangeException(int interval)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            PingIntervalMs = interval
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("interval", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WithPingTimeoutLessOrEqualToPingInterval_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            PingIntervalMs = 60000,
            PingTimeoutMs = 60000 // Equal to interval
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("greater", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WithPingTimeoutLessThanPingInterval_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            PingIntervalMs = 60000,
            PingTimeoutMs = 30000 // Less than interval
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("greater", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5000)]
    public void Validate_WithNegativeReconnectDelay_ShouldThrowArgumentOutOfRangeException(int delay)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ReconnectDelayMs = delay
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Reconnect", ex.Message);
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WithMaxReconnectDelayLessThanReconnectDelay_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ReconnectDelayMs = 10000,
            MaxReconnectDelayMs = 5000 // Less than reconnect delay
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Contains("Max", ex.Message);
    }

    [Fact]
    public void Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            AlternativeNicks = { "testbot2", "testbot3" },
            RequestedCapabilities = { "cap1", "cap2" },
            BlacklistedCapabilities = { "badcap" },
            Sasl = new SaslOptions
            {
                Mechanism = "PLAIN",
                Username = "user",
                Password = "pass"
            }
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.Server, clone.Server);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Nick, clone.Nick);
        Assert.Equal(original.UserName, clone.UserName);
        Assert.Equal(original.RealName, clone.RealName);

        // Verify collections are deep copied
        Assert.NotSame(original.AlternativeNicks, clone.AlternativeNicks);
        Assert.Equal(original.AlternativeNicks, clone.AlternativeNicks);

        Assert.NotSame(original.RequestedCapabilities, clone.RequestedCapabilities);
        Assert.Equal(original.RequestedCapabilities, clone.RequestedCapabilities);

        Assert.NotSame(original.BlacklistedCapabilities, clone.BlacklistedCapabilities);
        Assert.Equal(original.BlacklistedCapabilities, clone.BlacklistedCapabilities);

        // Verify SASL is deep copied
        Assert.NotSame(original.Sasl, clone.Sasl);
        Assert.Equal(original.Sasl?.Mechanism, clone.Sasl?.Mechanism);
        Assert.Equal(original.Sasl?.Username, clone.Sasl?.Username);
        Assert.Equal(original.Sasl?.Password, clone.Sasl?.Password);
    }

    [Fact]
    public void Clone_WithNullSasl_ShouldCloneCorrectly()
    {
        // Arrange
        var original = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            Sasl = null
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Null(clone.Sasl);
    }

    [Fact]
    public void Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            AlternativeNicks = { "testbot2" }
        };

        // Act
        var clone = original.Clone();
        clone.Server = "irc.modified.com";
        clone.Port = 6697;
        clone.AlternativeNicks.Add("testbot3");

        // Assert
        Assert.Equal("irc.example.com", original.Server);
        Assert.Equal(6667, original.Port);
        Assert.Single(original.AlternativeNicks);
        Assert.Equal("testbot2", original.AlternativeNicks[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(6667)]
    [InlineData(6697)]
    [InlineData(9999)]
    [InlineData(65535)]
    public void Validate_WithValidPort_ShouldNotThrow(int port)
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = port,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithValidPingConfiguration_ShouldNotThrow()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            PingIntervalMs = 30000,
            PingTimeoutMs = 90000 // Greater than interval
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithReconnectDelayZero_ShouldNotThrow()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ReconnectDelayMs = 0 // Zero is valid
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithEqualReconnectDelays_ShouldNotThrow()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot",
            ReconnectDelayMs = 5000,
            MaxReconnectDelayMs = 5000 // Equal is valid
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void DefaultValues_ShouldBeValid()
    {
        // Arrange
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Nick = "testbot",
            UserName = "testuser",
            RealName = "Test Bot"
            // All other values use defaults
        };

        // Act & Assert
        options.Validate(); // Should not throw with default values
    }

    [Fact]
    public void DefaultCapabilities_ShouldNotBeEmpty()
    {
        // Arrange & Act
        var options = new IrcClientOptions();

        // Assert
        Assert.NotEmpty(options.RequestedCapabilities);
        Assert.Contains("multi-prefix", options.RequestedCapabilities);
        Assert.Contains("away-notify", options.RequestedCapabilities);
        Assert.Contains("account-notify", options.RequestedCapabilities);
    }

    [Fact]
    public void BlacklistedCapabilities_ShouldStartEmpty()
    {
        // Arrange & Act
        var options = new IrcClientOptions();

        // Assert
        Assert.Empty(options.BlacklistedCapabilities);
    }
}
