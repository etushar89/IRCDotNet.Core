using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests.Performance;

/// <summary>
/// Tests for performance optimizations, particularly the static compiled regex
/// </summary>
public class RegexOptimizationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly IrcClient _client;

    public RegexOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IrcClient>();

        var options = new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User"
        };

        _client = new IrcClient(options, _logger);
    }

    [Fact]
    public void StaticRegex_ShouldBeCompiledAndCached()
    {
        // Arrange & Act - Get the static regex field using reflection
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(regexField);

        var regex = regexField.GetValue(null) as Regex;
        Assert.NotNull(regex);

        // Verify it's compiled
        Assert.True(regex.Options.HasFlag(RegexOptions.Compiled));
    }

    [Theory]
    [InlineData("nick!user@host.example.com", "nick", "user", "host.example.com")]
    [InlineData("test123!testuser@192.168.1.1", "test123", "testuser", "192.168.1.1")]
    [InlineData("bot!~bot@example.org", "bot", "~bot", "example.org")]
    [InlineData("user_name!real.user@subdomain.example.net", "user_name", "real.user", "subdomain.example.net")]
    [InlineData("ChanServ!ChanServ@services.", "ChanServ", "ChanServ", "services.")]
    public void ParseNickUserHost_WithValidInputs_ShouldParseCorrectly(string input, string expectedNick, string expectedUser, string expectedHost)
    {
        // We can't directly test the private method, but we can test the regex directly
        // Arrange
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var regex = regexField!.GetValue(null) as Regex;

        // Act
        var match = regex!.Match(input);

        // Assert
        Assert.True(match.Success);
        Assert.Equal(expectedNick, match.Groups[1].Value);
        Assert.Equal(expectedUser, match.Groups[2].Value);
        Assert.Equal(expectedHost, match.Groups[3].Value);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("nick@host")]  // Missing user part
    [InlineData("nick!user")]  // Missing host part
    [InlineData("!user@host")] // Missing nick part
    [InlineData("")]
    [InlineData("   ")]
    public void ParseNickUserHost_WithInvalidInputs_ShouldNotMatch(string input)
    {
        // Arrange
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var regex = regexField!.GetValue(null) as Regex;

        // Act
        var match = regex!.Match(input);

        // Assert
        Assert.False(match.Success);
    }

    [Fact]
    public void StaticRegex_PerformanceComparison_ShouldBeFast()
    {
        // Arrange
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var compiledRegex = regexField!.GetValue(null) as Regex;

        // Create a non-compiled regex for comparison
        var nonCompiledRegex = new Regex(@"^([^!]+)!([^@]+)@(.+)$", RegexOptions.None);

        var testInput = "testuser!~test@example.com";
        const int iterations = 10000;

        // Act & Measure compiled regex performance
        var compiledStopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var match = compiledRegex!.Match(testInput);
            Assert.True(match.Success); // Ensure it's working
        }
        compiledStopwatch.Stop();

        // Measure non-compiled regex performance
        var nonCompiledStopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var match = nonCompiledRegex.Match(testInput);
            Assert.True(match.Success); // Ensure it's working
        }
        nonCompiledStopwatch.Stop();

        // Assert - Compiled regex should be faster (or at least not significantly slower)
        _output.WriteLine($"Compiled regex time: {compiledStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Non-compiled regex time: {nonCompiledStopwatch.ElapsedMilliseconds}ms");

        // The compiled regex should be at least as fast as the non-compiled one
        // We allow for some variance due to test environment factors
        var performanceRatio = (double)compiledStopwatch.ElapsedMilliseconds / nonCompiledStopwatch.ElapsedMilliseconds;
        Assert.True(performanceRatio <= 2.0,
            $"Compiled regex should not be more than 2x slower than non-compiled. Ratio: {performanceRatio:F2}");
    }
    [Fact]
    public async System.Threading.Tasks.Task StaticRegex_ThreadSafety_ShouldHandleConcurrentAccess()
    {
        // Arrange
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var regex = regexField!.GetValue(null) as Regex;

        var testInputs = new[]
        {
            "user1!test1@host1.com",
            "user2!test2@host2.com",
            "user3!test3@host3.com",
            "user4!test4@host4.com",
            "user5!test5@host5.com"
        };

        var tasks = new System.Threading.Tasks.Task[20];
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act - Create multiple tasks that use the regex concurrently
        for (int i = 0; i < tasks.Length; i++)
        {
            var taskIndex = i;
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        var input = testInputs[j % testInputs.Length];
                        var match = regex!.Match(input);

                        if (!match.Success)
                        {
                            throw new InvalidOperationException($"Failed to match: {input}");
                        }

                        // Verify the groups are correctly captured
                        var nick = match.Groups[1].Value;
                        var user = match.Groups[2].Value;
                        var host = match.Groups[3].Value;

                        if (string.IsNullOrEmpty(nick) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(host))
                        {
                            throw new InvalidOperationException($"Invalid groups captured for: {input}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        // Wait for all tasks to complete
        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert - No exceptions should occur
        Assert.Empty(exceptions);
    }

    [Fact]
    public void RegexPattern_ShouldMatchIRCSpecification()
    {
        // Arrange
        var regexField = typeof(IrcClient).GetField("NickUserHostRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var regex = regexField!.GetValue(null) as Regex;

        // Test cases based on IRC specification examples
        var validCases = new[]
        {
            // Standard cases
            ("nick!user@host", "nick", "user", "host"),
            
            // Cases with special characters in nick
            ("nick123!user@host", "nick123", "user", "host"),
            ("nick_test!user@host", "nick_test", "user", "host"),
            ("nick-test!user@host", "nick-test", "user", "host"),
            
            // Cases with tilde in user (common in IRC)
            ("nick!~user@host", "nick", "~user", "host"),
            
            // Cases with IP addresses as hosts
            ("nick!user@192.168.1.1", "nick", "user", "192.168.1.1"),
            ("nick!user@::1", "nick", "user", "::1"),
            
            // Cases with complex hostnames
            ("nick!user@subdomain.example.com", "nick", "user", "subdomain.example.com"),
            ("nick!user@irc.freenode.net", "nick", "user", "irc.freenode.net"),
        };

        // Act & Assert
        foreach (var (input, expectedNick, expectedUser, expectedHost) in validCases)
        {
            var match = regex!.Match(input);
            Assert.True(match.Success, $"Should match: {input}");
            Assert.Equal(expectedNick, match.Groups[1].Value);
            Assert.Equal(expectedUser, match.Groups[2].Value);
            Assert.Equal(expectedHost, match.Groups[3].Value);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
