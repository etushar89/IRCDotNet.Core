using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IRCDotNet.Tests.Disconnect
{
    /// <summary>
    /// Tests for improved disconnect cleanup functionality.
    /// Ensures all resources, state, and events are properly cleaned up on disconnect/dispose.
    /// </summary>
    public class DisconnectCleanupTests
    {
        private readonly Mock<ILogger<IrcClient>> _mockLogger;
        private readonly IrcClientOptions _options;

        public DisconnectCleanupTests()
        {
            _mockLogger = new Mock<ILogger<IrcClient>>();
            _options = new IrcClientOptions
            {
                Server = "irc.test.com",
                Port = 6667,
                Nick = "TestBot",
                UserName = "testuser",
                RealName = "Test User",
                PingIntervalMs = 30000,
                PingTimeoutMs = 60000
            };
        }

        [Fact]
        public async Task DisconnectAsync_WhenNotConnected_RaisesDisconnectEventWithReason()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            DisconnectedEvent? receivedEvent = null;
            var eventReceived = new TaskCompletionSource<bool>();

            client.Disconnected += (_, e) =>
            {
                receivedEvent = e;
                eventReceived.SetResult(true);
            };

            var reason = "Manual disconnect test";

            // Act
            await client.DisconnectAsync(reason);

            // Wait for event to be raised (with timeout)
            await Task.WhenAny(eventReceived.Task, Task.Delay(100));

            // Assert
            Assert.NotNull(receivedEvent);
            Assert.Equal(reason, receivedEvent.Reason);
        }

        [Fact]
        public async Task DisconnectAsync_WithReason_PassesReasonToEvent()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            DisconnectedEvent? receivedEvent = null;
            client.Disconnected += (_, e) => receivedEvent = e;

            var customReason = "Custom disconnect reason";

            // Act
            await client.DisconnectAsync(customReason);
            await Task.Delay(10); // Allow event to be processed

            // Assert
            Assert.NotNull(receivedEvent);
            Assert.Equal(customReason, receivedEvent.Reason);
        }

        [Fact]
        public async Task DisconnectAsync_WithoutReason_UsesDefaultReason()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            DisconnectedEvent? receivedEvent = null;
            client.Disconnected += (_, e) => receivedEvent = e;

            // Act
            await client.DisconnectAsync();
            await Task.Delay(10); // Allow event to be processed

            // Assert
            Assert.NotNull(receivedEvent);
            Assert.Equal("Client disconnected", receivedEvent.Reason);
        }

        [Fact]
        public async Task DisposeAsync_WhenNotConnected_DoesNotRaiseEvent()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            DisconnectedEvent? receivedEvent = null;
            client.Disconnected += (_, e) => receivedEvent = e;

            // Act
            await client.DisposeAsync();
            await Task.Delay(10); // Allow dispose to be processed

            // Assert - No event should be raised when not connected
            Assert.Null(receivedEvent);
        }

        [Fact]
        public void DisconnectCleanup_ClearsAllCollections()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Simulate some state by accessing properties (this will initialize collections)
            var initialChannels = client.Channels;
            var initialCapabilities = client.EnabledCapabilities;

            // Act
            client.Dispose();

            // Assert - After dispose, properties should return empty collections
            Assert.Empty(client.Channels);
            Assert.Empty(client.EnabledCapabilities);
        }

        [Fact]
        public void DisconnectCleanup_ResetsConnectionState()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act
            client.Dispose();

            // Assert
            Assert.False(client.IsConnected);
            Assert.False(client.IsRegistered);
            Assert.Equal(_options.Nick, client.CurrentNick); // Should reset to original nick
        }

        [Fact]
        public async Task DisconnectCleanup_HandlesDisposalExceptionsGracefully()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act & Assert - Should not throw even if internal disposals fail
            await client.DisposeAsync();
            client.Dispose(); // Should handle being called multiple times
        }

        [Fact]
        public async Task DisconnectCleanup_IsThreadSafe()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act - Simulate concurrent disconnect/dispose calls
            var tasks = new Task[10];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = client.DisconnectAsync($"Reason {i}");
            }
            for (int i = 5; i < 10; i++)
            {
                tasks[i] = client.DisposeAsync().AsTask();
            }

            // Assert - Should complete without deadlocks or exceptions
            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task DisconnectCleanup_RaisesEnhancedDisconnectEvent()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            EnhancedDisconnectedEvent? receivedEvent = null;
            client.OnEnhancedDisconnected += (_, e) => receivedEvent = e;

            var reason = "Enhanced event test";

            // Act
            await client.DisconnectAsync(reason);
            await Task.Delay(10); // Allow event to be processed

            // Assert
            Assert.NotNull(receivedEvent);
            Assert.Equal(reason, receivedEvent.Reason);
            Assert.Equal(_options.Server, receivedEvent.Server);
            Assert.False(receivedEvent.WasExpected);
        }

        [Fact]
        public async Task DisconnectCleanup_HandlesNullReason()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);
            DisconnectedEvent? receivedEvent = null;
            client.Disconnected += (_, e) => receivedEvent = e;

            // Act
            await client.DisconnectAsync(null);
            await Task.Delay(10); // Allow event to be processed

            // Assert
            Assert.NotNull(receivedEvent);
            Assert.Equal("Client disconnected", receivedEvent.Reason); // Should use default
        }

        [Fact]
        public void DisconnectCleanup_PreventsDuplicateDisposal()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act - Multiple dispose calls
            client.Dispose();
            client.Dispose();
            client.Dispose();

            // Assert - Should not throw or cause issues
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task DisconnectCleanup_PreventsDuplicateAsyncDisposal()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act - Multiple async dispose calls
            await client.DisposeAsync();
            await client.DisposeAsync();
            await client.DisposeAsync();

            // Assert - Should not throw or cause issues
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task DisconnectCleanup_ExecutesCleanupSteps()
        {
            // Arrange
            var client = new IrcClient(_options, _mockLogger.Object);

            // Act
            await client.DisconnectAsync("Test disconnect");
            await Task.Delay(10); // Allow disconnect to be processed

            // Assert - Verify client is in correct state after disconnect
            Assert.False(client.IsConnected);
            Assert.False(client.IsRegistered);
            Assert.Equal(_options.Nick, client.CurrentNick); // Should reset to original nick
        }
    }
}
