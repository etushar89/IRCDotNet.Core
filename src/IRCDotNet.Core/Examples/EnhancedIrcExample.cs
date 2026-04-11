using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Extensions;
using IRCDotNet.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Examples;

/// <summary>
/// Enhanced example demonstrating modern IRCDotNet features
/// </summary>
public static class EnhancedIrcExample
{
    public static async Task Main(string[] args)
    {
        // Example 1: Simple bot with builder pattern
        await SimpleBuilderExample().ConfigureAwait(false);

        // Example 2: Dependency injection setup
        await DependencyInjectionExample().ConfigureAwait(false);

        // Example 3: Multi-bot manager
        await MultiBotManagerExample().ConfigureAwait(false);

        // Example 4: Conversation flows
        await ConversationFlowExample().ConfigureAwait(false);

        // Example 5: Event handling with enhanced events
        await EnhancedEventExample().ConfigureAwait(false);
    }

    /// <summary>
    /// Example 1: Simple bot using the builder pattern
    /// </summary>
    public static async Task SimpleBuilderExample()
    {
        Console.WriteLine("=== Simple Builder Example ===");

        // Create configuration using fluent builder
        var config = new IrcClientOptionsBuilder()
            .WithNick("EnhancedBot")
            .WithUserName("enhancedbot")
            .WithRealName("Enhanced IRC Bot")
            .AddServer("irc.libera.chat", 6667)
            .AddAutoJoinChannel("#test")
            .AddAutoJoinChannel("#bottest")
            .WithSaslAuthentication("myusername", "mypassword", required: false)
            .WithAutoReconnect(enabled: true, maxAttempts: 5,
                initialDelay: TimeSpan.FromSeconds(10))
            .WithTimeouts(
                connection: TimeSpan.FromSeconds(30),
                read: TimeSpan.FromMinutes(5),
                ping: TimeSpan.FromMinutes(3))
            .AddCapabilities("message-tags", "server-time", "batch")
            .Build();

        using var client = new IrcClient(config);

        // Enhanced event handling with respond capabilities
        client.OnEnhancedMessage += async (sender, e) =>
        {
            if (e.Text.StartsWith("!hello"))
            {
                await e.RespondAsync("Hello there! 👋").ConfigureAwait(false);
            }
            else if (e.Text.StartsWith("!time"))
            {
                await e.RespondToUserAsync($"Current time is {DateTime.Now:HH:mm:ss}").ConfigureAwait(false);
            }
            else if (e.Text.StartsWith("!pm"))
            {
                await e.ReplyPrivatelyAsync("This is a private response!").ConfigureAwait(false);
            }
        };

        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
            Console.WriteLine("Connected! Press any key to disconnect...");
            Console.ReadKey();
        }
        finally
        {
            await client.DisconnectAsync("Example finished").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Example 2: Using dependency injection
    /// </summary>
    public static async Task DependencyInjectionExample()
    {
        Console.WriteLine("\n=== Dependency Injection Example ===");

        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Add IRC client with configuration
                services.AddIrcClient(builder => builder
                    .WithNick("DIBot")
                    .WithUserName("dibot")
                    .WithRealName("Dependency Injection Bot")
                    .AddServer("irc.libera.chat", 6667)
                    .AddAutoJoinChannel("#test"));

                // Use threaded event dispatcher
                services.AddThreadedEventDispatcher();

                // Add our bot service
                services.AddHostedService<ExampleBotService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        // Run for a short time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await host.RunAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("DI Example completed");
        }
    }

    /// <summary>
    /// Example 3: Multi-bot manager
    /// </summary>
    public static async Task MultiBotManagerExample()
    {
        Console.WriteLine("\n=== Multi-Bot Manager Example ===");

        using var manager = new IrcBotManager();

        // Add multiple bots for different networks
        await manager.AddBotAsync("libera", builder => builder
            .WithNick("MultiBot1")
            .WithUserName("multibot")
            .WithRealName("Multi Bot on Libera")
            .AddServer("irc.libera.chat", 6667)
            .AddAutoJoinChannel("#test")).ConfigureAwait(false);

        await manager.AddBotAsync("oftc", builder => builder
            .WithNick("MultiBot2")
            .WithUserName("multibot")
            .WithRealName("Multi Bot on OFTC")
            .AddServer("irc.oftc.net", 6667)
            .AddAutoJoinChannel("#test")).ConfigureAwait(false);

        // Template-based bot creation
        var template = new IrcClientOptionsBuilder()
            .WithUserName("templatebot")
            .WithRealName("Template Bot")
            .WithAutoReconnect(true)
            .Build();

        await manager.AddBotsFromTemplateAsync(template,
            ("irc1", "irc.example1.com", 6667, false),
            ("irc2", "irc.example2.com", 6697, true)).ConfigureAwait(false);

        // Start all bots
        await manager.StartAllBotsAsync().ConfigureAwait(false);

        // Get status
        var statuses = manager.GetAllBotStatus();
        foreach (var (name, status) in statuses)
        {
            Console.WriteLine($"Bot {name}: Connected={status.IsConnected}, Channels={status.ChannelCount}");
        }

        // Broadcast message to all bots
        await manager.BroadcastToChannelAsync("#test", "Hello from multi-bot manager!").ConfigureAwait(false);

        Console.WriteLine("Multi-bot example running. Press any key to stop...");
        Console.ReadKey();

        await manager.StopAllBotsAsync("Example finished").ConfigureAwait(false);
    }

    /// <summary>
    /// Example 4: Conversation flows
    /// </summary>
    public static async Task ConversationFlowExample()
    {
        Console.WriteLine("\n=== Conversation Flow Example ===");

        var config = new IrcClientOptionsBuilder()
            .WithNick("ConversationBot")
            .WithUserName("convbot")
            .WithRealName("Conversation Bot")
            .AddServer("irc.libera.chat", 6667)
            .AddAutoJoinChannel("#test")
            .Build();

        using var client = new IrcClient(config);

        client.OnEnhancedMessage += async (sender, e) =>
        {
            if (e.Text.StartsWith("!survey") && e.IsChannelMessage)
            {
                await RunSurveyConversation(client, e).ConfigureAwait(false);
            }
        };

        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
            Console.WriteLine("Conversation bot connected. Type '!survey' in #test channel");
            Console.WriteLine("Press any key to disconnect...");
            Console.ReadKey();
        }
        finally
        {
            await client.DisconnectAsync("Example finished").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Example 5: Enhanced event handling
    /// </summary>
    public static async Task EnhancedEventExample()
    {
        Console.WriteLine("\n=== Enhanced Event Example ===");

        var config = new IrcClientOptionsBuilder()
            .WithNick("EventBot")
            .WithUserName("eventbot")
            .WithRealName("Enhanced Event Bot")
            .AddServer("irc.libera.chat", 6667)
            .AddAutoJoinChannel("#test")
            .Build();

        using var client = new IrcClient(config);

        // Generic event handling
        client.OnGenericMessage += async (IMessageEvent e) =>
        {
            if (e.Message.Contains("hello"))
            {
                await e.RespondAsync("I heard you say hello!").ConfigureAwait(false);
            }
        };        // Cancellable pre-send events
        client.OnPreSendMessage += (PreSendMessageEvent e) =>
        {
            if (e.Message.Contains("badword"))
            {
                e.IsCancelled = true;
                e.Message = "[message filtered]";
            }
        };

        // Channel events
        client.OnEnhancedUserJoined += async (sender, e) =>
        {
            await e.WelcomeUserAsync("Welcome to the channel! 🎉").ConfigureAwait(false);
        };

        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
            Console.WriteLine("Enhanced event bot connected. Join/leave #test to see events");
            Console.WriteLine("Press any key to disconnect...");
            Console.ReadKey();
        }
        finally
        {
            await client.DisconnectAsync("Example finished").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Run a survey conversation using ConversationQueue
    /// </summary>
    private static async Task RunSurveyConversation(IrcClient client, EnhancedMessageEvent e)
    {
        using var conversation = new ConversationQueue(client);

        try
        {
            // Start the survey
            await e.RespondAsync($"{e.Nick}: Starting survey! Please answer in private message.").ConfigureAwait(false);

            // Build conversation flow
            var result = await conversation.StartConversation()
                .SendAndWait(e.Nick, "What's your favorite programming language?", TimeSpan.FromMinutes(1))
                .SendAndWait(e.Nick, "How many years of experience do you have?", TimeSpan.FromMinutes(1))
                .SendAndWait(e.Nick, "What's your favorite IDE?", TimeSpan.FromMinutes(1))
                .Execute(async () =>
                {
                    await client.SendMessageAsync(e.Nick, "Thank you for completing the survey!").ConfigureAwait(false);
                })
                .ExecuteAsync().ConfigureAwait(false);

            if (result.Success)
            {
                await e.RespondAsync($"{e.Nick}: Survey completed! Thanks for participating.").ConfigureAwait(false);

                // Process survey results
                var answers = result.Events.OfType<EnhancedMessageEvent>()
                    .Where(evt => evt.Nick == e.Nick && evt.IsPrivateMessage)
                    .Select(evt => evt.Text)
                    .ToList();

                if (answers.Count >= 3)
                {
                    await e.RespondAsync($"Results: Lang={answers[0]}, Experience={answers[1]}, IDE={answers[2]}").ConfigureAwait(false);
                }
            }
            else
            {
                await e.RespondAsync($"{e.Nick}: Survey timed out or was cancelled.").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await e.RespondAsync($"{e.Nick}: Survey error: {ex.Message}").ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Example hosted service for dependency injection
/// </summary>
public class ExampleBotService : IHostedService
{
    private readonly IrcClient _client;
    private readonly ILogger<ExampleBotService> _logger;

    public ExampleBotService(IrcClient client, ILogger<ExampleBotService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting example bot service");

        // Setup event handlers
        _client.OnEnhancedMessage += async (sender, e) =>
        {
            _logger.LogInformation("Received message from {Nick}: {Message}", e.Nick, e.Text);

            if (e.Text.StartsWith("!ping"))
            {
                await e.RespondAsync("Pong! 🏓").ConfigureAwait(false);
            }
        };

        _client.OnEnhancedConnected += async (sender, e) =>
        {
            _logger.LogInformation("Connected to {Server} as {Nick}", e.Server, e.Nick);

            // Auto-join configured channels
            if (_client.Configuration.AutoJoinChannels.Any())
            {
                await e.JoinChannelsAsync(_client.Configuration.AutoJoinChannels).ConfigureAwait(false);
            }
        };

        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping example bot service");
        await _client.DisconnectAsync("Service stopping").ConfigureAwait(false);
    }
}
