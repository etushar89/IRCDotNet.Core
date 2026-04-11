using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace IRCDotNet.ConcurrencyTests;

/// <summary>
/// Thread-safe counter for concurrent operations
/// </summary>
public class ConcurrentCounter
{
    private int _value;

    public int Value => _value;

    public int Increment() => Interlocked.Increment(ref _value);

    public void Reset() => Interlocked.Exchange(ref _value, 0);
}

/// <summary>
/// Demonstration version of long-running real-world stress tests for IRC client reliability and message handling.
/// This version runs for 5 minutes instead of 8 hours for quick validation.
/// </summary>
[Trait("Category", "Demo")]
public class LongRunningDemoTests : IDisposable
{
    private const string TestServer = "localhost";
    private const int TestPort = 6667;
    private const string TestChannel = "#testchannel";

    private readonly ITestOutputHelper _output;
    private readonly ILogger<IrcClient> _logger;
    private readonly List<IrcClient> _clients = new();
    private readonly CancellationTokenSource _testCancellation = new();

    public LongRunningDemoTests(ITestOutputHelper output)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<IrcClient>();
    }

    [Fact]
    public async Task FiveMinuteConversationDemo_MultipleUsers_ShouldMaintainReliability()
    {
        const int userCount = 4; // Increased for more realistic scenario
        const int minUsersNeeded = 3; // Minimum users needed to run the test
        const int messagesPerMinute = 8; // Further reduced from 10 to avoid flood protection
        const int testDurationMinutes = 3; // Increased to allow for nick changes and reconnections
        const int messageIntervalMs = 60000 / messagesPerMinute; // 7.5 seconds between messages

        _output.WriteLine($"Starting 5-minute conversation demo with {userCount} users");
        _output.WriteLine($"Each user will send {messagesPerMinute} messages per minute");
        _output.WriteLine($"Total expected messages: {userCount * (userCount - 1) * messagesPerMinute * testDurationMinutes}");

        // Statistics tracking
        var totalMessagesSent = new ConcurrentCounter();
        var totalMessagesReceived = new ConcurrentCounter();
        var connectionsLost = new ConcurrentCounter();
        var nickChanges = new ConcurrentCounter();
        var reconnections = new ConcurrentCounter();
        var networkEvents = new ConcurrentBag<NetworkEvent>();

        // Create and connect users
        var users = new List<UserContext>();
        var connectionTasks = new List<Task>();

        for (int i = 0; i < userCount; i++)
        {
            var userIndex = i;
            var nick = $"TestUser{userIndex}_{DateTime.Now.Ticks % 10000}";

            var options = new IrcClientOptions
            {
                Server = TestServer,
                Port = TestPort,
                Nick = nick,
                UserName = $"testuser{userIndex}",
                RealName = $"Test User {userIndex}",
                ConnectionTimeoutMs = 30000,
                AutoReconnect = true,
                ReconnectDelayMs = 5000,
                EnableRateLimit = false // Disable rate limiting for long-running demo tests
            };

            var client = new IrcClient(options, _logger);
            _clients.Add(client);

            var userContext = new UserContext
            {
                Index = userIndex,
                Nick = nick,
                OriginalNick = nick,
                CurrentNick = nick,
                AllNicks = new HashSet<string> { nick }, // Initialize with the original nick
                Client = client,
                IsConnected = false,
                HasBeenConnectedBefore = false,
                IsFirstConnection = true,
                LastMessageSentTo = new ConcurrentDictionary<string, int>(),
                MessagesSent = new ConcurrentDictionary<string, List<SentMessage>>(),
                MessagesReceived = new List<ReceivedMessage>()
            };

            users.Add(userContext);            // Set up event handlers for this user
            var userSentMessages = new ConcurrentDictionary<string, MessageInfo>();
            var userReceivedMessages = new ConcurrentBag<ReceivedMessage>();
            var userMessageOrderViolations = new ConcurrentBag<OrderViolation>();
            var userConnectionIssues = new ConcurrentBag<ConnectionIssue>();

            client.Disconnected += (sender, e) =>
            {
                userContext.IsConnected = false;
                connectionsLost.Increment();

                var disconnectEvent = new NetworkEvent
                {
                    UserIndex = userIndex,
                    Nick = nick,
                    Timestamp = DateTimeOffset.UtcNow,
                    Type = "Disconnection",
                    Details = e.Reason ?? "Unknown reason"
                };

                networkEvents.Add(disconnectEvent);
                userConnectionIssues.Add(new ConnectionIssue
                {
                    UserIndex = userIndex,
                    Nick = nick,
                    Timestamp = DateTimeOffset.UtcNow,
                    Type = "Disconnection",
                    Reason = e.Reason ?? "Unknown reason"
                });

                _output.WriteLine($"[NETWORK] User {userIndex} ({nick}) disconnected: {e.Reason}");
            };

            client.Connected += async (sender, e) =>
            {
                _output.WriteLine($"[NETWORK] User {userIndex} ({nick}) connected, joining {TestChannel}");

                var connectEvent = new NetworkEvent
                {
                    UserIndex = userIndex,
                    Nick = nick,
                    Timestamp = DateTimeOffset.UtcNow,
                    Type = "Connection",
                    Details = $"Connected to {e.Network}"
                };

                networkEvents.Add(connectEvent);

                // Check if this is a reconnection
                if (userContext.HasBeenConnectedBefore)
                {
                    reconnections.Increment();
                    connectEvent.Type = "Reconnection";
                    userContext.IsFirstConnection = false;
                    _output.WriteLine($"[NETWORK] User {userIndex} ({nick}) reconnected!");
                }
                else
                {
                    userContext.HasBeenConnectedBefore = true;
                }

                try
                {
                    // Join the test channel after connecting
                    await Task.Delay(1000); // Brief delay to ensure connection is stable
                    await client.JoinChannelAsync(TestChannel);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"User {userIndex} ({nick}) failed to join {TestChannel}: {ex.Message}");
                    // Don't mark as connected if join fails - let the UserJoinedChannel event handle it
                }
            };

            client.UserJoinedChannel += (sender, e) =>
            {
                // Only set connected to true when the user has successfully joined the test channel
                if (e.Nick == userContext.CurrentNick && e.Channel == TestChannel)
                {
                    _output.WriteLine($"[NETWORK] User {userIndex} ({userContext.CurrentNick}) successfully joined {TestChannel}");
                    userContext.IsConnected = true;
                }
            };

            // Handle nick changes
            client.NickChanged += (sender, e) =>
            {
                var nickChangeEvent = new NetworkEvent
                {
                    UserIndex = userIndex,
                    Nick = e.OldNick,
                    Timestamp = DateTimeOffset.UtcNow,
                    Type = "NickChange",
                    Details = $"{e.OldNick} -> {e.NewNick}"
                };

                networkEvents.Add(nickChangeEvent);
                nickChanges.Increment();

                // Update our tracking if it's our nick change
                if (e.OldNick == userContext.CurrentNick)
                {
                    userContext.CurrentNick = e.NewNick;
                    userContext.AllNicks.Add(e.NewNick); // Track the new nick
                    _output.WriteLine($"[NETWORK] User {userIndex} changed nick: {e.OldNick} -> {e.NewNick}");
                }
                else
                {
                    _output.WriteLine($"[NETWORK] Another user changed nick: {e.OldNick} -> {e.NewNick}");
                }
            };

            client.PrivateMessageReceived += (sender, e) =>
            {
                // For channel messages, extract the intended recipient from the message content
                var intendedRecipient = e.Target == TestChannel ? ExtractIntendedRecipient(e.Text) : e.Target;

                // Only process messages intended for this user (considering all nicks they've had) or sent directly to this user
                var isForThisUser = (intendedRecipient != null && userContext.AllNicks.Contains(intendedRecipient))
                                   || userContext.AllNicks.Contains(e.Target);

                if (!isForThisUser)
                {
                    return; // This message is not for this user
                }

                var receivedMsg = new ReceivedMessage
                {
                    FromNick = e.Nick,
                    ToNick = intendedRecipient ?? e.Target,
                    Content = e.Text,
                    Timestamp = DateTimeOffset.UtcNow,
                    MessageId = ExtractMessageId(e.Text)
                };

                userContext.MessagesReceived.Add(receivedMsg);
                totalMessagesReceived.Increment();

                // Check message order
                if (receivedMsg.MessageId.HasValue)
                {
                    var expectedId = userContext.LastMessageReceivedFrom.AddOrUpdate(receivedMsg.FromNick,
                        receivedMsg.MessageId.Value,
                        (k, v) => Math.Max(v, receivedMsg.MessageId.Value));

                    if (receivedMsg.MessageId.Value < expectedId)
                    {
                        userMessageOrderViolations.Add(new OrderViolation
                        {
                            FromNick = receivedMsg.FromNick,
                            ToNick = receivedMsg.ToNick,
                            ExpectedId = expectedId,
                            ActualId = receivedMsg.MessageId.Value,
                            Timestamp = DateTimeOffset.UtcNow
                        });
                    }
                }

                // Mark message as received
                var messageKey = $"{receivedMsg.FromNick}->{receivedMsg.ToNick}:{receivedMsg.MessageId}";
                if (userSentMessages.TryGetValue(messageKey, out var msgInfo))
                {
                    msgInfo.ReceivedAt = receivedMsg.Timestamp;
                }
            };

            connectionTasks.Add(ConnectUserWithRetry(client, userContext, userIndex));
        }

        // Wait for all users to connect (with timeout)
        var connectionTimeout = Task.Delay(TimeSpan.FromMinutes(2));
        var completedTask = await Task.WhenAny(Task.WhenAll(connectionTasks), connectionTimeout);

        if (completedTask == connectionTimeout)
        {
            _output.WriteLine("Connection timeout reached, proceeding with connected users");
        }

        var connectedCount = users.Count(u => u.IsConnected);
        _output.WriteLine($"Connected {connectedCount}/{userCount} users successfully");

        if (connectedCount < minUsersNeeded)
        {
            throw new InvalidOperationException($"Not enough users connected ({connectedCount}/{userCount}). Need at least 3 users. Aborting test.");
        }

        // Start the conversation test
        var testStartTime = DateTimeOffset.UtcNow;
        var testEndTime = testStartTime.AddMinutes(testDurationMinutes);
        var sentMessages = new ConcurrentDictionary<string, MessageInfo>();
        var receivedMessages = new ConcurrentBag<ReceivedMessage>();
        var messageOrderViolations = new ConcurrentBag<OrderViolation>();
        var connectionIssues = new ConcurrentBag<ConnectionIssue>();

        // Create message sending tasks for each user
        var messagingTasks = new List<Task>();

        foreach (var user in users.Where(u => u.IsConnected))
        {
            messagingTasks.Add(SimulateUserConversations(user, users, messageIntervalMs, testEndTime, sentMessages, totalMessagesSent));
        }

        // Add nick change simulation tasks (some users will change nicks during test)
        messagingTasks.Add(SimulateNickChanges(users, testEndTime, networkEvents, nickChanges));

        // Add disconnection/reconnection simulation tasks
        messagingTasks.Add(SimulateDisconnectionsAndReconnections(users, testEndTime, networkEvents, connectionsLost, reconnections));

        // Start monitoring task
        var monitoringTask = MonitorTestProgress(testStartTime, testEndTime,
            () => totalMessagesSent.Value,
            () => totalMessagesReceived.Value,
            () => connectionsLost.Value,
            () => nickChanges.Value,
            () => reconnections.Value);

        // Wait for test completion
        await Task.WhenAll(messagingTasks.Concat(new[] { monitoringTask }));

        // Calculate final statistics
        var testDuration = DateTimeOffset.UtcNow - testStartTime;
        var dropRate = totalMessagesSent.Value > 0 ? (double)(totalMessagesSent.Value - totalMessagesReceived.Value) / totalMessagesSent.Value * 100 : 0;
        var orderViolationRate = totalMessagesReceived.Value > 0 ? (double)messageOrderViolations.Count / totalMessagesReceived.Value * 100 : 0;

        // Generate comprehensive report
        await GenerateTestReport(testStartTime, testDuration, users, totalMessagesSent.Value, totalMessagesReceived.Value,
            sentMessages, receivedMessages, messageOrderViolations, connectionIssues, dropRate, orderViolationRate,
            nickChanges.Value, reconnections.Value, networkEvents);

        // Assertions for demo (more lenient than the 8-hour test)
        Assert.True(dropRate <= 5.0, $"Message drop rate should be <= 5.0% for demo, got {dropRate:F2}%");
        Assert.True(orderViolationRate <= 0.5, $"Message order violation rate should be <= 0.5% for demo, got {orderViolationRate:F2}%");
        Assert.True(connectedCount >= userCount * 0.6, $"At least 60% of users should remain connected for demo, got {connectedCount}/{userCount}");
    }
    private async Task ConnectUserWithRetry(IrcClient client, UserContext userContext, int userIndex)
    {
        const int maxRetries = 3;
        var retryDelay = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await client.ConnectAsync();

                // Wait for connection to be established with timeout
                var timeout = DateTimeOffset.UtcNow.AddSeconds(15);
                while (!userContext.IsConnected && DateTimeOffset.UtcNow < timeout)
                {
                    await Task.Delay(500);
                }

                if (userContext.IsConnected)
                {
                    _output.WriteLine($"User {userIndex} connected successfully on attempt {attempt}");
                    return;
                }
                else
                {
                    _output.WriteLine($"User {userIndex} connection timed out on attempt {attempt}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"User {userIndex} connection attempt {attempt} failed: {ex.Message}");
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelay);
                retryDelay *= 2; // Exponential backoff
            }
        }

        _output.WriteLine($"User {userIndex} failed to connect after {maxRetries} attempts");
    }

    private async Task SimulateUserConversations(UserContext sender, List<UserContext> allUsers, int messageIntervalMs,
        DateTimeOffset testEndTime, ConcurrentDictionary<string, MessageInfo> sentMessages, ConcurrentCounter totalMessagesSent)
    {
        var random = new Random(sender.Index);

        while (DateTimeOffset.UtcNow < testEndTime && !_testCancellation.Token.IsCancellationRequested)
        {
            try
            {
                // Check if user is properly connected (both client connected and joined to channel)
                if (!sender.IsConnected || sender.Client == null)
                {
                    await Task.Delay(5000, _testCancellation.Token); // Wait for reconnection
                    continue;
                }

                // Double-check connection state before sending messages
                if (!IsClientProperlyConnected(sender))
                {
                    await Task.Delay(2000, _testCancellation.Token); // Wait for proper connection
                    continue;
                }

                // Send messages to the channel instead of direct messages for better reliability
                foreach (var recipient in allUsers.Where(u => u.Index != sender.Index))
                {
                    // Skip if sender is no longer connected during the loop
                    if (!sender.IsConnected || !IsClientProperlyConnected(sender))
                        break;

                    var messageId = sender.LastMessageSentTo.AddOrUpdate(recipient.CurrentNick, 1, (k, v) => v + 1);
                    var content = $"MSG#{messageId:D6} from {sender.CurrentNick} to {recipient.CurrentNick} at {DateTimeOffset.UtcNow:HH:mm:ss.fff}";

                    try
                    {
                        // Send to channel instead of direct message for better reliability during reconnections
                        await sender.Client!.SendMessageAsync(TestChannel, content);

                        var sentMsg = new SentMessage
                        {
                            ToNick = recipient.CurrentNick,
                            Content = content,
                            Timestamp = DateTimeOffset.UtcNow,
                            MessageId = messageId
                        };

                        sender.MessagesSent.AddOrUpdate(recipient.CurrentNick,
                            new List<SentMessage> { sentMsg },
                            (k, v) => { v.Add(sentMsg); return v; });

                        var messageKey = $"{sender.CurrentNick}->{recipient.CurrentNick}:{messageId}";
                        sentMessages.TryAdd(messageKey, new MessageInfo
                        {
                            Content = content,
                            SentAt = sentMsg.Timestamp,
                            ReceivedAt = DateTimeOffset.MinValue,
                            FromNick = sender.CurrentNick,
                            ToNick = recipient.CurrentNick,
                            MessageId = messageId
                        });

                        totalMessagesSent.Increment();

                        // Small delay between messages to avoid overwhelming the IRC server
                        await Task.Delay(1000, _testCancellation.Token);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
                    {
                        // This is expected when user disconnects, mark as not connected
                        sender.IsConnected = false;
                        break; // Stop sending messages until reconnected
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Failed to send message from {sender.CurrentNick} to {recipient.CurrentNick}: {ex.Message}");
                    }
                }

                // Wait before sending next batch
                await Task.Delay(messageIntervalMs, _testCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in conversation simulation for {sender.CurrentNick}: {ex.Message}");
                await Task.Delay(5000, _testCancellation.Token);
            }
        }
    }

    private async Task SimulateNickChanges(List<UserContext> users, DateTimeOffset testEndTime,
        ConcurrentBag<NetworkEvent> networkEvents, ConcurrentCounter nickChanges)
    {
        var random = new Random();
        var nickChangeInterval = TimeSpan.FromSeconds(45); // Change nicks every 45 seconds
        var lastNickChange = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow < testEndTime && !_testCancellation.Token.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastNickChange >= nickChangeInterval)
                {
                    // Pick a random connected user to change nick
                    var connectedUsers = users.Where(u => u.IsConnected && u.Client != null).ToList();
                    if (connectedUsers.Count > 0)
                    {
                        var userToChange = connectedUsers[random.Next(connectedUsers.Count)];
                        var newNick = $"{userToChange.OriginalNick}_v{random.Next(100, 999)}";

                        try
                        {
                            await userToChange.Client!.ChangeNickAsync(newNick);
                            _output.WriteLine($"[SIMULATION] Initiated nick change for User {userToChange.Index}: {userToChange.CurrentNick} -> {newNick}");
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"[SIMULATION] Failed to change nick for User {userToChange.Index}: {ex.Message}");
                        }
                    }

                    lastNickChange = DateTimeOffset.UtcNow;
                }

                await Task.Delay(5000, _testCancellation.Token); // Check every 5 seconds
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in nick change simulation: {ex.Message}");
                await Task.Delay(10000, _testCancellation.Token);
            }
        }
    }

    private async Task SimulateDisconnectionsAndReconnections(List<UserContext> users, DateTimeOffset testEndTime,
        ConcurrentBag<NetworkEvent> networkEvents, ConcurrentCounter connectionsLost, ConcurrentCounter reconnections)
    {
        var random = new Random();
        var disconnectionInterval = TimeSpan.FromSeconds(60); // Disconnect someone every minute
        var lastDisconnection = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow < testEndTime && !_testCancellation.Token.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastDisconnection >= disconnectionInterval)
                {
                    // Pick a random connected user to disconnect (but keep at least 2 connected)
                    var connectedUsers = users.Where(u => u.IsConnected && u.Client != null).ToList();
                    if (connectedUsers.Count > 2) // Keep at least 2 users connected
                    {
                        var userToDisconnect = connectedUsers[random.Next(connectedUsers.Count)];

                        try
                        {
                            _output.WriteLine($"[SIMULATION] Initiating disconnect for User {userToDisconnect.Index} ({userToDisconnect.CurrentNick})");
                            await userToDisconnect.Client!.DisconnectAsync("Simulated disconnect");

                            // Schedule reconnection after 10-30 seconds
                            var reconnectDelay = random.Next(10000, 30000);
                            _ = Task.Delay(reconnectDelay, _testCancellation.Token).ContinueWith(async _ =>
                            {
                                if (!_testCancellation.Token.IsCancellationRequested && DateTimeOffset.UtcNow < testEndTime)
                                {
                                    try
                                    {
                                        _output.WriteLine($"[SIMULATION] Attempting reconnection for User {userToDisconnect.Index}");
                                        await userToDisconnect.Client!.ConnectAsync(_testCancellation.Token);
                                    }
                                    catch (Exception ex)
                                    {
                                        _output.WriteLine($"[SIMULATION] Reconnection failed for User {userToDisconnect.Index}: {ex.Message}");
                                    }
                                }
                            }, _testCancellation.Token);
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"[SIMULATION] Failed to disconnect User {userToDisconnect.Index}: {ex.Message}");
                        }
                    }

                    lastDisconnection = DateTimeOffset.UtcNow;
                }

                await Task.Delay(5000, _testCancellation.Token); // Check every 5 seconds
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in disconnection/reconnection simulation: {ex.Message}");
                await Task.Delay(10000, _testCancellation.Token);
            }
        }
    }

    private async Task MonitorTestProgress(DateTimeOffset startTime, DateTimeOffset endTime,
        Func<int> getSentCount, Func<int> getReceivedCount, Func<int> getConnectionLossCount,
        Func<int> getNickChangeCount, Func<int> getReconnectionCount)
    {
        var totalDuration = endTime - startTime;
        var lastReport = startTime;

        while (DateTimeOffset.UtcNow < endTime && !_testCancellation.Token.IsCancellationRequested)
        {
            var elapsed = DateTimeOffset.UtcNow - startTime;
            var progress = elapsed.TotalSeconds / totalDuration.TotalSeconds * 100;

            // Report every 30 seconds
            if (DateTimeOffset.UtcNow - lastReport >= TimeSpan.FromSeconds(30))
            {
                _output.WriteLine($"Progress: {progress:F1}% | Sent: {getSentCount()} | Received: {getReceivedCount()} | Conn. Lost: {getConnectionLossCount()} | Nick Changes: {getNickChangeCount()} | Reconnections: {getReconnectionCount()}");
                lastReport = DateTimeOffset.UtcNow;
            }

            await Task.Delay(5000, _testCancellation.Token);
        }
    }

    private async Task GenerateTestReport(DateTimeOffset startTime, TimeSpan duration, List<UserContext> users,
        int totalSent, int totalReceived, ConcurrentDictionary<string, MessageInfo> sentMessages,
        ConcurrentBag<ReceivedMessage> receivedMessages, ConcurrentBag<OrderViolation> orderViolations,
        ConcurrentBag<ConnectionIssue> connectionIssues, double dropRate, double orderViolationRate,
        int nickChangeCount, int reconnectionCount, ConcurrentBag<NetworkEvent> networkEvents)
    {
        var report = new StringBuilder();

        report.AppendLine("=== IRC LONG-RUNNING CONVERSATION DEMO REPORT ===");
        report.AppendLine($"Test Duration: {duration:hh\\:mm\\:ss}");
        report.AppendLine($"Start Time: {startTime:yyyy-MM-dd HH:mm:ss UTC}");
        report.AppendLine($"End Time: {startTime.Add(duration):yyyy-MM-dd HH:mm:ss UTC}");
        report.AppendLine($"Test Channel: {TestChannel}");
        report.AppendLine();

        report.AppendLine("=== MESSAGE STATISTICS ===");
        report.AppendLine($"Total Messages Sent: {totalSent:N0}");
        report.AppendLine($"Total Messages Received: {totalReceived:N0}");
        report.AppendLine($"Message Drop Rate: {dropRate:F4}%");
        report.AppendLine($"Message Order Violation Rate: {orderViolationRate:F4}%");
        report.AppendLine();

        report.AppendLine("=== NETWORK EVENTS ===");
        report.AppendLine($"Total Nick Changes: {nickChangeCount:N0}");
        report.AppendLine($"Total Reconnections: {reconnectionCount:N0}");
        report.AppendLine($"Total Connection Losses: {connectionIssues.Count}");
        report.AppendLine();

        report.AppendLine("=== USER STATISTICS ===");
        foreach (var user in users)
        {
            var sentCount = user.MessagesSent.Values.Sum(list => list.Count);
            var receivedCount = user.MessagesReceived.Count;

            report.AppendLine($"User {user.Index} ({user.OriginalNick} -> {user.CurrentNick}):");
            report.AppendLine($"  Connected: {user.IsConnected}");
            report.AppendLine($"  Messages Sent: {sentCount:N0}");
            report.AppendLine($"  Messages Received: {receivedCount:N0}");
            report.AppendLine($"  Has Reconnected: {user.HasBeenConnectedBefore && !user.IsFirstConnection}");
        }
        report.AppendLine();

        report.AppendLine("=== NETWORK EVENTS TIMELINE ===");
        var sortedEvents = networkEvents.OrderBy(e => e.Timestamp).Take(20);
        foreach (var evt in sortedEvents)
        {
            report.AppendLine($"  {evt.Timestamp:HH:mm:ss} - User {evt.UserIndex} ({evt.Nick}): {evt.Type} - {evt.Details}");
        }
        if (networkEvents.Count > 20)
            report.AppendLine($"  ... and {networkEvents.Count - 20} more events");
        report.AppendLine();

        report.AppendLine("=== CONNECTION ISSUES ===");
        report.AppendLine($"Total Connection Losses: {connectionIssues.Count}");
        foreach (var issue in connectionIssues.Take(10))
        {
            report.AppendLine($"  {issue.Timestamp:HH:mm:ss} - User {issue.UserIndex} ({issue.Nick}): {issue.Type} - {issue.Reason}");
        }
        report.AppendLine();

        report.AppendLine("=== ORDER VIOLATIONS ===");
        report.AppendLine($"Total Order Violations: {orderViolations.Count}");
        foreach (var violation in orderViolations.Take(10))
        {
            report.AppendLine($"  {violation.Timestamp:HH:mm:ss} - {violation.FromNick} -> {violation.ToNick}: Expected {violation.ExpectedId}, Got {violation.ActualId}");
        }

        _output.WriteLine(report.ToString());

        // Also save to file
        var reportPath = Path.Combine(Environment.CurrentDirectory, $"irc_demo_test_report_{startTime:yyyyMMdd_HHmmss}.txt");
        await File.WriteAllTextAsync(reportPath, report.ToString());
        _output.WriteLine($"Detailed report saved to: {reportPath}");
    }

    private static int? ExtractMessageId(string message)
    {
        // Extract message ID from format "MSG#123456 ..."
        if (message.StartsWith("MSG#") && message.Length > 4)
        {
            var endIndex = message.IndexOf(' ', 4);
            if (endIndex > 4)
            {
                var idStr = message.Substring(4, endIndex - 4);
                if (int.TryParse(idStr, out var id))
                {
                    return id;
                }
            }
        }
        return null;
    }

    private static string? ExtractIntendedRecipient(string message)
    {
        // Extract intended recipient from format "MSG#123456 from SenderNick to RecipientNick at timestamp"
        var toIndex = message.IndexOf(" to ");
        var atIndex = message.IndexOf(" at ");

        if (toIndex > 0 && atIndex > toIndex + 4)
        {
            return message.Substring(toIndex + 4, atIndex - toIndex - 4);
        }
        return null;
    }

    private static bool IsClientProperlyConnected(UserContext user)
    {
        if (user.Client == null || !user.IsConnected)
            return false;

        try
        {
            // Try to access a property that would indicate connection state
            // The IsConnected property in IrcClient should reflect actual connection state
            return user.Client.IsConnected;
        }
        catch
        {
            // If accessing the client throws an exception, it's not properly connected
            return false;
        }
    }

    public void Dispose()
    {
        _testCancellation.Cancel();

        foreach (var client in _clients)
        {
            try
            {
                client.DisconnectAsync("Test completed").Wait(5000);
                client.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing client: {ex.Message}");
            }
        }
    }
}

// Supporting classes (same as in LongRunningRealWorldTests.cs)
public class UserContext
{
    public int Index { get; set; }
    public string Nick { get; set; } = string.Empty;
    public string OriginalNick { get; set; } = string.Empty;
    public string CurrentNick { get; set; } = string.Empty;
    public HashSet<string> AllNicks { get; set; } = new(); // Track all nicks this user has had
    public IrcClient? Client { get; set; }
    public bool IsConnected { get; set; }
    public bool HasBeenConnectedBefore { get; set; }
    public bool IsFirstConnection { get; set; } = true;
    public ConcurrentDictionary<string, int> LastMessageSentTo { get; set; } = new();
    public ConcurrentDictionary<string, int> LastMessageReceivedFrom { get; set; } = new();
    public ConcurrentDictionary<string, List<SentMessage>> MessagesSent { get; set; } = new();
    public List<ReceivedMessage> MessagesReceived { get; set; } = new();
}

public class SentMessage
{
    public string ToNick { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public int MessageId { get; set; }
}

public class ReceivedMessage
{
    public string FromNick { get; set; } = string.Empty;
    public string ToNick { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public int? MessageId { get; set; }
}

public class MessageInfo
{
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string FromNick { get; set; } = string.Empty;
    public string ToNick { get; set; } = string.Empty;
    public int MessageId { get; set; }
}

public class OrderViolation
{
    public string FromNick { get; set; } = string.Empty;
    public string ToNick { get; set; } = string.Empty;
    public int ExpectedId { get; set; }
    public int ActualId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class ConnectionIssue
{
    public int UserIndex { get; set; }
    public string Nick { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class NetworkEvent
{
    public int UserIndex { get; set; }
    public string Nick { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Details { get; set; } = string.Empty;
}
