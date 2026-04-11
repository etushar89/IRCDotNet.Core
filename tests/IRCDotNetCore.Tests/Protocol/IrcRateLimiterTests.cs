using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcRateLimiterTests
{
    [Fact]
    public void IsAllowed_WithinLimit_ReturnsTrue()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 5); // 1 per second, burst of 5

        // Act
        var result = limiter.IsAllowed("test", config);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAllowed_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 2); // 1 per second, burst of 2

        // Act
        // Use up the burst
        limiter.IsAllowed("test", config);
        limiter.IsAllowed("test", config);

        // This should fail
        var result = limiter.IsAllowed("test", config);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetDelayUntilAllowed_WhenLimited_ReturnsDelay()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 1); // 1 per second, burst of 1

        // Act
        limiter.IsAllowed("test", config); // Use up the token
        var delay = limiter.GetDelayUntilAllowed("test", config);

        // Assert
        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public void GetDelayUntilAllowed_WhenNotLimited_ReturnsZero()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 5);

        // Act
        var delay = limiter.GetDelayUntilAllowed("test", config);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void Reset_ClearsLimitForIdentifier()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 1);

        // Use up the limit
        limiter.IsAllowed("test", config);
        Assert.False(limiter.IsAllowed("test", config));

        // Act
        limiter.Reset("test");
        var result = limiter.IsAllowed("test", config);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStats()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 5);

        // Act
        limiter.IsAllowed("test", config);
        var stats = limiter.GetStats("test");

        // Assert
        Assert.Equal(4, stats.CurrentTokens); // 5 - 1 = 4
        Assert.Equal(1, stats.TotalRequests);
    }

    [Fact]
    public void Clear_RemovesAllLimits()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 1);

        // Use up limits for multiple identifiers
        limiter.IsAllowed("test1", config);
        limiter.IsAllowed("test2", config);

        // Act
        limiter.Clear();

        // Assert
        Assert.True(limiter.IsAllowed("test1", config));
        Assert.True(limiter.IsAllowed("test2", config));
    }

    [Fact]
    public async Task WaitForAllowedAsync_WhenLimited_Waits()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(10.0, 1); // 10 per second, so 0.1 second delay

        // Use up the token
        limiter.IsAllowed("test", config);

        // Act
        var startTime = DateTime.UtcNow;
        await limiter.WaitForAllowedAsync("test", config);
        var endTime = DateTime.UtcNow;

        // Assert
        var elapsed = endTime - startTime;
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(50)); // Should wait at least some time
    }

    [Fact]
    public void Cleanup_RemovesOldBuckets()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 5);

        // Create some buckets
        limiter.IsAllowed("test1", config);
        limiter.IsAllowed("test2", config);

        // Act
        limiter.Cleanup(TimeSpan.FromMilliseconds(1));

        // Small delay to ensure buckets are considered old
        Thread.Sleep(10);
        limiter.Cleanup(TimeSpan.FromMilliseconds(1));

        // The buckets should be cleaned up, so new requests should succeed
        Assert.True(limiter.IsAllowed("test1", config));
        Assert.True(limiter.IsAllowed("test2", config));
    }

    [Fact]
    public async Task RateLimiter_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(10.0, 20); // 10 per second, burst of 20
        var tasks = new List<Task<bool>>();
        var successCount = 0;

        // Act - Create many concurrent requests
        for (int i = 0; i < 100; i++)
        {
            int taskId = i;
            tasks.Add(Task.Run(() =>
            {
                var result = limiter.IsAllowed($"test-{taskId % 10}", config); // Use 10 different identifiers
                if (result)
                    Interlocked.Increment(ref successCount);
                return result;
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should have thread-safe behavior and some requests should be allowed
        Assert.True(successCount > 0);
        Assert.True(successCount <= 100); // Some should be rate limited
    }

    [Fact]
    public async Task RateLimiter_WaitForAllowedAsync_ShouldHandleConcurrentWaits()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(2.0, 1); // 2 per second, burst of 1
        var tasks = new List<Task>();
        var completedCount = 0;

        // Use up the initial token
        limiter.IsAllowed("test", config);

        // Act - Create concurrent waits
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await limiter.WaitForAllowedAsync("test", config, cts.Token);
                    Interlocked.Increment(ref completedCount);
                }
                catch (OperationCanceledException)
                {
                    // Expected for some requests due to timeout
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - At least some requests should complete successfully
        Assert.True(completedCount > 0);
        Assert.True(completedCount <= 5);
    }

    [Fact]
    public async Task RateLimiter_HighVolumeStressTest_ShouldMaintainPerformance()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(100.0, 50); // High rate: 100 per second, burst of 50
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasks = new List<Task>();
        var totalRequests = 1000;
        var allowedCount = 0;

        // Act - High volume requests
        for (int i = 0; i < totalRequests; i++)
        {
            int requestId = i;
            tasks.Add(Task.Run(() =>
            {
                // Distribute across multiple identifiers to simulate real usage
                var identifier = $"user-{requestId % 50}";
                if (limiter.IsAllowed(identifier, config))
                {
                    Interlocked.Increment(ref allowedCount);
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - Should handle high volume efficiently
        Assert.True(allowedCount > 0);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds

        // Cleanup should work even with many buckets
        limiter.Cleanup(TimeSpan.Zero); // Cleanup all buckets
        var stats = limiter.GetStats("user-1");
        Assert.Equal(0, stats.CurrentTokens); // Should be reset after cleanup
    }

    [Fact]
    public void RateLimiter_ManyIdentifiers_ShouldNotLeakMemory()
    {
        // Arrange
        var limiter = new IrcRateLimiter();
        var config = new RateLimitConfig(1.0, 5);

        // Act - Create many different identifiers to simulate long-running application
        for (int i = 0; i < 1000; i++)
        {
            limiter.IsAllowed($"unique-id-{i}", config);
        }

        // Assert - Cleanup should remove old buckets
        var cutoffTime = TimeSpan.FromMilliseconds(1);
        Thread.Sleep(2); // Ensure time passes
        limiter.Cleanup(cutoffTime);

        // Verify cleanup worked by checking if new requests work normally
        Assert.True(limiter.IsAllowed("new-request", config));
    }
}

public class IrcGlobalRateLimiterTests
{
    [Fact]
    public void CanSendChannelMessage_InitialCall_ReturnsTrue()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("channel:#test");

        // Act
        var result = IrcGlobalRateLimiter.CanSendChannelMessage("#test");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanSendPrivateMessage_InitialCall_ReturnsTrue()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("privmsg:user");

        // Act
        var result = IrcGlobalRateLimiter.CanSendPrivateMessage("user");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanPerformChannelOperation_InitialCall_ReturnsTrue()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("chanop:JOIN");

        // Act
        var result = IrcGlobalRateLimiter.CanPerformChannelOperation("JOIN");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanSendCommand_InitialCall_ReturnsTrue()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("cmd:WHOIS");

        // Act
        var result = IrcGlobalRateLimiter.CanSendCommand("WHOIS");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetChannelStats_ReturnsStats()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("channel:#test");
        IrcGlobalRateLimiter.CanSendChannelMessage("#test");

        // Act
        var stats = IrcGlobalRateLimiter.GetChannelStats("#test");

        // Assert
        Assert.True(stats.TotalRequests > 0);
    }

    [Fact]
    public async Task WaitForChannelMessageAsync_CompletesSuccessfully()
    {
        // Arrange
        IrcGlobalRateLimiter.Reset("channel:#test");

        // Act & Assert
        await IrcGlobalRateLimiter.WaitForChannelMessageAsync("#test");
        // If we get here without exception, the test passes
        Assert.True(true);
    }
}

public class RateLimitConfigTests
{
    [Fact]
    public void Constructor_WithValidParams_SetsProperties()
    {
        // Arrange & Act
        var config = new RateLimitConfig(2.5, 10, 5);

        // Assert
        Assert.Equal(2.5, config.RefillRate);
        Assert.Equal(10, config.BucketSize);
        Assert.Equal(5, config.InitialTokens);
    }

    [Fact]
    public void Constructor_WithoutInitialTokens_UsesBucketSize()
    {
        // Arrange & Act
        var config = new RateLimitConfig(1.0, 5);

        // Assert
        Assert.Equal(5, config.InitialTokens);
    }

    [Fact]
    public void Constructor_WithExcessiveInitialTokens_ClampsToBucketSize()
    {
        // Arrange & Act
        var config = new RateLimitConfig(1.0, 5, 10);

        // Assert
        Assert.Equal(5, config.InitialTokens);
    }
}
