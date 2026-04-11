using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.Tests.Internal;

/// <summary>
/// Tests for internal caching mechanisms and cache invalidation
/// These tests use reflection to access private methods for testing internal behavior
/// </summary>
public class CacheInvalidationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();

    public CacheInvalidationTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact]
    public void InvalidateChannelsCache_ShouldClearChannelsCache()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Get initial cached channels
        var initialChannels = client.Channels;
        var secondAccess = client.Channels;

        // Verify caching is working (same reference)
        Assert.Same(initialChannels, secondAccess);

        // Act - Call private InvalidateChannelsCache method
        var invalidateMethod = typeof(IrcClient).GetMethod("InvalidateChannelsCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidateMethod);

        invalidateMethod.Invoke(client, null);

        // Get channels after invalidation
        var channelsAfterInvalidation = client.Channels;

        // Assert - Should get a new instance (cache was invalidated)
        Assert.NotSame(initialChannels, channelsAfterInvalidation);
    }

    [Fact]
    public void InvalidateCapabilitiesCache_ShouldClearCapabilitiesCache()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Get initial cached capabilities
        var initialCapabilities = client.EnabledCapabilities;
        var secondAccess = client.EnabledCapabilities;

        // Verify caching is working (same reference)
        Assert.Same(initialCapabilities, secondAccess);

        // Act - Call private InvalidateCapabilitiesCache method
        var invalidateMethod = typeof(IrcClient).GetMethod("InvalidateCapabilitiesCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidateMethod);

        invalidateMethod.Invoke(client, null);

        // Get capabilities after invalidation
        var capabilitiesAfterInvalidation = client.EnabledCapabilities;

        // Assert - Should get a new instance (cache was invalidated)
        Assert.NotSame(initialCapabilities, capabilitiesAfterInvalidation);
    }

    [Fact]
    public void InvalidateAllCaches_ShouldClearAllCaches()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        // Get initial cached values
        var initialChannels = client.Channels;
        var initialCapabilities = client.EnabledCapabilities;

        // Verify caching is working
        Assert.Same(initialChannels, client.Channels);
        Assert.Same(initialCapabilities, client.EnabledCapabilities);

        // Act - Call private InvalidateAllCaches method
        var invalidateMethod = typeof(IrcClient).GetMethod("InvalidateAllCaches",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidateMethod);

        invalidateMethod.Invoke(client, null);

        // Get values after invalidation
        var channelsAfterInvalidation = client.Channels;
        var capabilitiesAfterInvalidation = client.EnabledCapabilities;

        // Assert - Should get new instances for both (all caches were invalidated)
        Assert.NotSame(initialChannels, channelsAfterInvalidation);
        Assert.NotSame(initialCapabilities, capabilitiesAfterInvalidation);
    }

    [Fact]
    public void CacheInvalidation_AfterMultipleInvalidations_ShouldAlwaysReturnFreshData()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var invalidateChannelsMethod = typeof(IrcClient).GetMethod("InvalidateChannelsCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var invalidateCapabilitiesMethod = typeof(IrcClient).GetMethod("InvalidateCapabilitiesCache",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(invalidateChannelsMethod);
        Assert.NotNull(invalidateCapabilitiesMethod);

        var channelInstances = new List<object>();
        var capabilityInstances = new List<object>();

        // Act - Repeatedly invalidate and access
        for (int i = 0; i < 5; i++)
        {
            invalidateChannelsMethod.Invoke(client, null);
            invalidateCapabilitiesMethod.Invoke(client, null);

            channelInstances.Add(client.Channels);
            capabilityInstances.Add(client.EnabledCapabilities);
        }

        // Assert - All instances should be different
        for (int i = 0; i < channelInstances.Count - 1; i++)
        {
            Assert.NotSame(channelInstances[i], channelInstances[i + 1]);
            Assert.NotSame(capabilityInstances[i], capabilityInstances[i + 1]);
        }
    }
    [Fact]
    public async Task Channels_PropertyAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var invalidateMethod = typeof(IrcClient).GetMethod("InvalidateChannelsCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidateMethod);

        // Act - Concurrent access and invalidation
        var accessTasks = new List<Task>();
        var exceptions = new List<Exception>();
        var lockObj = new object();

        for (int i = 0; i < 10; i++)
        {
            // Create tasks that access the property
            accessTasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 100; j++)
                    {
                        var channels = client.Channels;
                        Assert.NotNull(channels);

                        // Occasionally invalidate cache
                        if (j % 10 == 0)
                        {
                            invalidateMethod.Invoke(client, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(accessTasks.ToArray());

        // Assert - No exceptions should occur
        Assert.Empty(exceptions);
    }
    [Fact]
    public async Task EnabledCapabilities_PropertyAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var options = CreateTestOptions();
        var client = new IrcClient(options, _logger);
        _clients.Add(client);

        var invalidateMethod = typeof(IrcClient).GetMethod("InvalidateCapabilitiesCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(invalidateMethod);

        // Act - Concurrent access and invalidation
        var accessTasks = new List<Task>();
        var exceptions = new List<Exception>();
        var lockObj = new object();

        for (int i = 0; i < 10; i++)
        {
            // Create tasks that access the property
            accessTasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 100; j++)
                    {
                        var capabilities = client.EnabledCapabilities;
                        Assert.NotNull(capabilities);

                        // Occasionally invalidate cache
                        if (j % 10 == 0)
                        {
                            invalidateMethod.Invoke(client, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(accessTasks.ToArray());

        // Assert - No exceptions should occur
        Assert.Empty(exceptions);
    }

    private static IrcClientOptions CreateTestOptions()
    {
        return new IrcClientOptions
        {
            Server = "localhost",
            Port = 6667,
            Nick = "TestUser",
            UserName = "testuser",
            RealName = "Test User",
            ConnectionTimeoutMs = 1000
        };
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
    }
}
