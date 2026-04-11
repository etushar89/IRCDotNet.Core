namespace IRCDotNet.Core.Protocol;

/// <summary>
/// Token-bucket rate limiter for IRC outgoing messages to prevent flood protection kicks.
/// </summary>
public class IrcRateLimiter
{
    private readonly Dictionary<string, RateLimitBucket> _buckets = new();
    private readonly object _lock = new object();

    /// <summary>
    /// Default rate limit: 1 message per second with burst of 5.
    /// </summary>
    public static readonly RateLimitConfig DefaultConfig = new RateLimitConfig(1.0, 5);

    /// <summary>
    /// Conservative rate limit for public channels: 1 message per 2 seconds.
    /// </summary>
    public static readonly RateLimitConfig ConservativeConfig = new RateLimitConfig(0.5, 3);

    /// <summary>
    /// Rate limit for private messages: 2 messages per second.
    /// </summary>
    public static readonly RateLimitConfig PrivateMessageConfig = new RateLimitConfig(2.0, 10);

    /// <summary>
    /// Rate limit for channel operations: 1 operation per 3 seconds.
    /// </summary>
    public static readonly RateLimitConfig ChannelOperationConfig = new RateLimitConfig(0.33, 2);

    /// <summary>
    /// Checks if an action is allowed under the rate limit.
    /// </summary>
    /// <param name="identifier">Key identifying the rate-limited action.</param>
    /// <param name="config">Rate limit configuration to use, or <c>null</c> for default.</param>
    /// <returns><c>true</c> if the action is allowed.</returns>
    public bool IsAllowed(string identifier, RateLimitConfig? config = null)
    {
        config ??= DefaultConfig;

        lock (_lock)
        {
            if (!_buckets.TryGetValue(identifier, out var bucket))
            {
                bucket = new RateLimitBucket(config);
                _buckets[identifier] = bucket;
            }

            return bucket.TryConsume();
        }
    }

    /// <summary>
    /// Gets the time until the next action would be allowed.
    /// </summary>
    /// <param name="identifier">Key identifying the rate-limited action.</param>
    /// <param name="config">Rate limit configuration to use, or <c>null</c> for default.</param>
    /// <returns>Time to wait, or <see cref="TimeSpan.Zero"/> if allowed immediately.</returns>
    public TimeSpan GetDelayUntilAllowed(string identifier, RateLimitConfig? config = null)
    {
        config ??= DefaultConfig;

        lock (_lock)
        {
            if (!_buckets.TryGetValue(identifier, out var bucket))
            {
                return TimeSpan.Zero;
            }

            return bucket.GetDelayUntilRefill();
        }
    }

    /// <summary>
    /// Waits asynchronously until an action is allowed.
    /// </summary>
    /// <param name="identifier">Key identifying the rate-limited action.</param>
    /// <param name="config">Rate limit configuration to use, or <c>null</c> for default.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    public async Task WaitForAllowedAsync(string identifier, RateLimitConfig? config = null, CancellationToken cancellationToken = default)
    {
        config ??= DefaultConfig;

        // Keep trying until we can consume a token or the cancellation is requested
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsAllowed(identifier, config))
            {
                return; // Successfully consumed a token
            }

            // Get the delay and wait, but check again after waiting in case conditions changed
            var delay = GetDelayUntilAllowed(identifier, config);
            if (delay > TimeSpan.Zero)
            {
                // Use a minimum delay to prevent busy waiting
                var actualDelay = delay < TimeSpan.FromMilliseconds(10)
                    ? TimeSpan.FromMilliseconds(10)
                    : delay;

                await Task.Delay(actualDelay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Small delay to prevent tight loop if delay calculation is incorrect
                await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Resets the rate limit for a specific identifier.
    /// </summary>
    /// <param name="identifier">Key to reset.</param>
    public void Reset(string identifier)
    {
        lock (_lock)
        {
            _buckets.Remove(identifier);
        }
    }

    /// <summary>
    /// Removes all rate limit buckets.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _buckets.Clear();
        }
    }

    /// <summary>
    /// Gets rate limit statistics for an identifier.
    /// </summary>
    /// <param name="identifier">Key to get stats for.</param>
    /// <returns>Current rate limit statistics.</returns>
    public RateLimitStats GetStats(string identifier)
    {
        lock (_lock)
        {
            if (_buckets.TryGetValue(identifier, out var bucket))
            {
                return bucket.GetStats();
            }

            return new RateLimitStats(0, TimeSpan.Zero, 0);
        }
    }

    /// <summary>
    /// Cleans up old rate limit buckets to prevent memory leaks.
    /// </summary>
    /// <param name="maxAge">Maximum age before a bucket is removed.</param>
    /// <returns>The time span value.</returns>
    public void Cleanup(TimeSpan maxAge)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var toRemove = new List<string>();

            foreach (var kvp in _buckets)
            {
                if (kvp.Value.LastAccess < cutoff)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _buckets.Remove(key);
            }
        }
    }
}

/// <summary>
/// Token-bucket configuration controlling message throughput.
/// </summary>
public class RateLimitConfig
{
    /// <summary>
    /// Number of tokens added per second (controls sustained throughput).
    /// </summary>
    public double RefillRate { get; }

    /// <summary>
    /// Maximum tokens in the bucket (controls burst capacity).
    /// </summary>
    public int BucketSize { get; }

    /// <summary>
    /// Starting token count. Defaults to <see cref="BucketSize"/> for full burst on first use.
    /// </summary>
    public int InitialTokens { get; }

    /// <summary>
    /// Initializes a new <see cref="RateLimitConfig"/>.
    /// </summary>
    /// <param name="refillRate">Number of tokens added per second.</param>
    /// <param name="bucketSize">Maximum number of tokens in the bucket.</param>
    /// <param name="initialTokens">Initial token count. Defaults to <paramref name="bucketSize"/> if negative.</param>
    public RateLimitConfig(double refillRate, int bucketSize, int initialTokens = -1)
    {
        RefillRate = refillRate;
        BucketSize = bucketSize;
        InitialTokens = initialTokens < 0 ? bucketSize : Math.Min(initialTokens, bucketSize);
    }
}

/// <summary>
/// Snapshot of a rate limit bucket's current state.
/// </summary>
public record RateLimitStats(int CurrentTokens, TimeSpan TimeUntilRefill, long TotalRequests);

/// <summary>
/// Token bucket implementation for rate limiting
/// </summary>
internal class RateLimitBucket
{
    private readonly RateLimitConfig _config;
    private readonly object _lock = new object();
    private double _tokens;
    private DateTime _lastRefill;
    private long _totalRequests;

    public DateTime LastAccess
    {
        get
        {
            lock (_lock)
            {
                return _lastRefill;
            }
        }
    }

    public RateLimitBucket(RateLimitConfig config)
    {
        _config = config;
        _tokens = config.InitialTokens;
        _lastRefill = DateTime.UtcNow;
        _totalRequests = 0;
    }

    public bool TryConsume(int tokens = 1)
    {
        lock (_lock)
        {
            RefillTokens();
            _totalRequests++;

            if (_tokens >= tokens)
            {
                _tokens -= tokens;
                return true;
            }

            return false;
        }
    }

    public TimeSpan GetDelayUntilRefill()
    {
        lock (_lock)
        {
            RefillTokens();

            if (_tokens >= 1)
                return TimeSpan.Zero;

            var tokensNeeded = 1 - _tokens;
            var secondsNeeded = tokensNeeded / _config.RefillRate;
            return TimeSpan.FromSeconds(secondsNeeded);
        }
    }

    public RateLimitStats GetStats()
    {
        lock (_lock)
        {
            RefillTokens();

            // Calculate delay without calling GetDelayUntilRefill() to avoid deadlock
            TimeSpan delayUntilRefill = TimeSpan.Zero;
            if (_tokens < 1)
            {
                var tokensNeeded = 1 - _tokens;
                var secondsNeeded = tokensNeeded / _config.RefillRate;
                delayUntilRefill = TimeSpan.FromSeconds(secondsNeeded);
            }

            return new RateLimitStats((int)Math.Floor(_tokens), delayUntilRefill, _totalRequests);
        }
    }

    private void RefillTokens()
    {
        // This method is called from within locked contexts, so no additional locking needed
        var now = DateTime.UtcNow;
        var elapsed = now - _lastRefill;
        var tokensToAdd = elapsed.TotalSeconds * _config.RefillRate;

        _tokens = Math.Min(_config.BucketSize, _tokens + tokensToAdd);
        _lastRefill = now;
    }
}

/// <summary>
/// Static convenience wrapper providing pre-configured rate limiters for common IRC actions.
/// </summary>
public static class IrcGlobalRateLimiter
{
    private static readonly IrcRateLimiter _instance = new IrcRateLimiter();

    /// <summary>
    /// Checks whether a channel message can be sent without exceeding the rate limit.
    /// </summary>
    /// <param name="channel">The target channel name.</param>
    /// <returns><c>true</c> if the message is allowed under the current rate limit.</returns>
    public static bool CanSendChannelMessage(string channel)
    {
        return _instance.IsAllowed($"channel:{channel}", IrcRateLimiter.DefaultConfig);
    }

    /// <summary>
    /// Checks whether a private message can be sent without exceeding the rate limit.
    /// </summary>
    /// <param name="user">The target nickname.</param>
    /// <returns><c>true</c> if the message is allowed under the current rate limit.</returns>
    public static bool CanSendPrivateMessage(string user)
    {
        return _instance.IsAllowed($"privmsg:{user}", IrcRateLimiter.PrivateMessageConfig);
    }

    /// <summary>
    /// Checks whether a channel operation (JOIN, PART, KICK, etc.) can be performed.
    /// </summary>
    /// <param name="operation">The operation name (e.g. <c>"join"</c>, <c>"part"</c>).</param>
    /// <returns><c>true</c> if the operation is allowed under the current rate limit.</returns>
    public static bool CanPerformChannelOperation(string operation)
    {
        return _instance.IsAllowed($"chanop:{operation}", IrcRateLimiter.ChannelOperationConfig);
    }

    /// <summary>
    /// Checks whether a general IRC command can be sent.
    /// </summary>
    /// <param name="command">The IRC command name (e.g. <c>"WHOIS"</c>, <c>"LIST"</c>).</param>
    /// <returns><c>true</c> if the command is allowed under the current rate limit.</returns>
    public static bool CanSendCommand(string command)
    {
        return _instance.IsAllowed($"cmd:{command.ToUpperInvariant()}", IrcRateLimiter.ConservativeConfig);
    }

    /// <summary>
    /// Waits asynchronously until a channel message is allowed by the rate limiter.
    /// </summary>
    /// <param name="channel">The target channel name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task WaitForChannelMessageAsync(string channel, CancellationToken cancellationToken = default)
    {
        await _instance.WaitForAllowedAsync($"channel:{channel}", IrcRateLimiter.DefaultConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits asynchronously until a private message is allowed by the rate limiter.
    /// </summary>
    /// <param name="user">The target nickname.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task WaitForPrivateMessageAsync(string user, CancellationToken cancellationToken = default)
    {
        await _instance.WaitForAllowedAsync($"privmsg:{user}", IrcRateLimiter.PrivateMessageConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits asynchronously until a channel operation is allowed by the rate limiter.
    /// </summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task WaitForChannelOperationAsync(string operation, CancellationToken cancellationToken = default)
    {
        await _instance.WaitForAllowedAsync($"chanop:{operation}", IrcRateLimiter.ChannelOperationConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits asynchronously until a command is allowed by the rate limiter.
    /// </summary>
    /// <param name="command">The IRC command name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static async Task WaitForCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _instance.WaitForAllowedAsync($"cmd:{command.ToUpperInvariant()}", IrcRateLimiter.ConservativeConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets rate limit statistics for a specific channel.
    /// </summary>
    /// <param name="channel">The target channel name.</param>
    /// <returns>Current rate limit statistics for the channel.</returns>
    public static RateLimitStats GetChannelStats(string channel)
    {
        return _instance.GetStats($"channel:{channel}");
    }

    /// <summary>
    /// Resets rate limiting state for a specific target.
    /// </summary>
    /// <param name="identifier">Key identifying the rate-limited action.</param>
    public static void Reset(string identifier)
    {
        _instance.Reset(identifier);
    }

    /// <summary>
    /// Removes rate limit buckets older than 1 hour.
    /// </summary>
    public static void Cleanup()
    {
        _instance.Cleanup(TimeSpan.FromHours(1));
    }
}
