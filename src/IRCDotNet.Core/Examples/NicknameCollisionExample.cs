using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Examples;

/// <summary>
/// Example demonstrating nickname collision handling (ERR_NICKCOLLISION - 436)
/// This example shows how the IRCDotNet.Core library handles nickname collisions
/// during connection registration and runtime.
/// </summary>
public class NicknameCollisionExample
{
    /// <summary>Example entry point demonstrating nickname collision handling.</summary>
    public static async Task Main(string[] args)
    {
        // Set up logging to see what's happening
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<NicknameCollisionExample>();

        // Configure IRC client with alternative nicknames for collision handling
        var options = new IrcClientOptions
        {
            Server = "irc.example.com",
            Port = 6667,
            Nick = "MyBot",
            UserName = "mybot",
            RealName = "Example Bot",
            UseSsl = false,
            AlternativeNicks = new List<string> { "MyBot", "MyBot2", "MyBot3", "MyBot_Backup" }
        };

        using var client = new IrcClient(options, loggerFactory.CreateLogger<IrcClient>());

        // Subscribe to nickname collision events
        client.NicknameCollision += OnNicknameCollision;
        client.NickChanged += OnNickChanged;
        client.Connected += OnConnected;
        client.Disconnected += OnDisconnected;

        Console.WriteLine("=== IRC Nickname Collision Handling Example ===");
        Console.WriteLine($"Primary nickname: {options.Nick}");
        Console.WriteLine($"Alternative nicknames: {string.Join(", ", options.AlternativeNicks)}");
        Console.WriteLine();

        // Demonstrate how to handle nickname collision events
        Console.WriteLine("This example demonstrates how IRCDotNet handles nickname collisions:");
        Console.WriteLine("1. During connection registration (ERR_NICKCOLLISION)");
        Console.WriteLine("2. During runtime when connected");
        Console.WriteLine("3. Automatic fallback to alternative nicknames");
        Console.WriteLine("4. Generation of unique nicknames when alternatives are exhausted");
        Console.WriteLine();

        // Simulate receiving ERR_NICKCOLLISION during registration
        Console.WriteLine("=== Simulating ERR_NICKCOLLISION during registration ===");
        await SimulateNicknameCollisionDuringRegistration(client, logger).ConfigureAwait(false);

        // Simulate receiving ERR_NICKCOLLISION for a registered client
        Console.WriteLine("\n=== Simulating ERR_NICKCOLLISION for registered client ===");
        await SimulateNicknameCollisionDuringRuntime(client, logger).ConfigureAwait(false);

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task SimulateNicknameCollisionDuringRegistration(IrcClient client, ILogger logger)
    {
        // This simulates what happens when the server sends ERR_NICKCOLLISION during registration
        // Format: ":server 436 * badnick :Nickname collision KILL from user@host"

        Console.WriteLine("Simulating collision for primary nickname 'MyBot'...");
        var collisionMessage1 = IrcMessage.Parse(":irc.example.com 436 * MyBot :Nickname collision KILL from user@host");

        // In a real scenario, this would be handled internally by the ProcessMessageAsync method
        // For demonstration, we'll create the event manually
        var event1 = new NicknameCollisionEvent(collisionMessage1, "MyBot", "MyBot", "MyBot2", false);

        logger.LogInformation("ERR_NICKCOLLISION received for '{CollidingNick}' - would attempt '{FallbackNick}'",
            event1.CollidingNick, event1.FallbackNick);

        // Simulate collision for the first alternative
        Console.WriteLine("Simulating collision for alternative nickname 'MyBot2'...");
        var collisionMessage2 = IrcMessage.Parse(":irc.example.com 436 * MyBot2 :Nickname collision KILL from user@host");
        var event2 = new NicknameCollisionEvent(collisionMessage2, "MyBot2", "MyBot2", "MyBot3", false);

        logger.LogInformation("ERR_NICKCOLLISION received for '{CollidingNick}' - would attempt '{FallbackNick}'",
            event2.CollidingNick, event2.FallbackNick);

        await Task.Delay(100).ConfigureAwait(false); // Simulate network delay
    }

    private static async Task SimulateNicknameCollisionDuringRuntime(IrcClient client, ILogger logger)
    {
        // This simulates what happens when the server sends ERR_NICKCOLLISION for a registered client
        // This is more serious as it means the server is forcing a nickname change

        Console.WriteLine("Simulating collision for registered client...");
        var runtimeCollisionMessage = IrcMessage.Parse(":irc.example.com 436 MyBot3 MyBot3 :Nickname collision KILL");

        // Create an event for a registered client collision
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 10000;
        var emergencyNick = $"Guest_{timestamp}_42";
        var runtimeEvent = new NicknameCollisionEvent(runtimeCollisionMessage, "MyBot3", "MyBot3", emergencyNick, true);

        logger.LogWarning("ERR_NICKCOLLISION received for registered client '{CollidingNick}' - emergency fallback to '{FallbackNick}'",
            runtimeEvent.CollidingNick, runtimeEvent.FallbackNick);

        await Task.Delay(100).ConfigureAwait(false); // Simulate network delay
    }

    private static void OnNicknameCollision(object? sender, NicknameCollisionEvent e)
    {
        Console.WriteLine($"🔥 NICKNAME COLLISION EVENT:");
        Console.WriteLine($"   Colliding Nick: {e.CollidingNick}");
        Console.WriteLine($"   Attempted Nick: {e.AttemptedNick}");
        Console.WriteLine($"   Fallback Nick: {e.FallbackNick}");
        Console.WriteLine($"   Is Registered: {e.IsRegistered}");
        Console.WriteLine($"   Server Message: {e.Message.Parameters.LastOrDefault()}");
        Console.WriteLine();
    }

    private static void OnNickChanged(object? sender, NickChangedEvent e)
    {
        Console.WriteLine($"📝 Nick changed from '{e.OldNick}' to '{e.NewNick}'");
    }

    private static void OnConnected(object? sender, ConnectedEvent e)
    {
        Console.WriteLine($"✅ Connected to {e.Network} as '{e.Nick}'");
    }

    private static void OnDisconnected(object? sender, DisconnectedEvent e)
    {
        Console.WriteLine($"❌ Disconnected: {e.Reason}");
    }
}
