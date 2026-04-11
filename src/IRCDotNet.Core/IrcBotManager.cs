using System.Collections.Concurrent;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core;

/// <summary>
/// Manages multiple IRC bot instances with centralized lifecycle management.
/// </summary>
public class IrcBotManager : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly ILogger<IrcBotManager>? _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, IrcClient> _bots = new();
    private volatile bool _isRunning;
    private volatile bool _disposed;

    /// <summary>
    /// Gets whether the bot manager is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets a read-only dictionary of all managed bots.
    /// </summary>
    public IReadOnlyDictionary<string, IrcClient> Bots => _bots;

    /// <summary>
    /// Initializes a new instance of the IrcBotManager.
    /// </summary>
    public IrcBotManager() : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the IrcBotManager with logging.
    /// </summary>
    /// <param name="logger">The logger for this instance.</param>
    /// <param name="loggerFactory">The logger factory for creating client loggers.</param>
    public IrcBotManager(ILogger<IrcBotManager>? logger, ILoggerFactory? loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Adds a new bot with the specified configuration.
    /// </summary>
    /// <param name="name">The unique name for the bot.</param>
    /// <param name="configuration">The IRC client configuration.</param>
    /// <returns>The created IRC client</returns>
    /// <exception cref="ArgumentException">Thrown when name is null or whitespace</exception>
    /// <exception cref="ArgumentNullException">Thrown when configuration is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when a bot with the same name already exists</exception>
    public async Task<IrcClient> AddBotAsync(string name, IrcClientOptions configuration)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Bot name cannot be null or whitespace", nameof(name));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (_bots.ContainsKey(name))
            throw new InvalidOperationException($"Bot with name '{name}' already exists");

        var clientLogger = _loggerFactory?.CreateLogger<IrcClient>();
        var client = new IrcClient(configuration, clientLogger);

        if (!_bots.TryAdd(name, client))
            throw new InvalidOperationException($"Bot with name '{name}' already exists");

        _logger?.LogInformation("Added bot '{BotName}' with server '{Server}'", name, configuration.Server);

        return await Task.FromResult(client).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a new bot using a configuration builder.
    /// </summary>
    /// <param name="name">The unique name for the bot.</param>
    /// <param name="configureAction">Action to configure the bot options.</param>
    /// <returns>The created IRC client</returns>
    public async Task<IrcClient> AddBotAsync(string name, Action<IrcClientOptionsBuilder> configureAction)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Bot name cannot be null or whitespace", nameof(name));

        if (configureAction == null)
            throw new ArgumentNullException(nameof(configureAction));

        var builder = new IrcClientOptionsBuilder();
        configureAction(builder);
        var configuration = builder.Build();

        return await AddBotAsync(name, configuration).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a bot by name.
    /// </summary>
    /// <param name="name">The name of the bot to remove.</param>
    /// <returns>True if the bot was removed, false if not found</returns>
    public async Task<bool> RemoveBotAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (_bots.TryRemove(name, out var client))
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync("Bot manager shutdown").ConfigureAwait(false);
                }
                client.Dispose();
                _logger?.LogInformation("Removed bot '{BotName}'", name);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error while removing bot '{BotName}'", name);
                // Still return true since the bot was removed from the collection
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a bot by name.
    /// </summary>
    /// <param name="name">The name of the bot.</param>
    /// <returns>The IRC client or null if not found</returns>
    public IrcClient? GetBot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _bots.TryGetValue(name, out var client) ? client : null;
    }    /// <summary>
         /// Adds multiple bots from a template configuration.
         /// </summary>
         /// <param name="template">The template configuration.</param>
         /// <param name="servers">The server configurations (name, host, port, useSsl)</param>
    public async Task AddBotsFromTemplateAsync(
        IrcClientOptions template,
        IEnumerable<(string name, string host, int port, bool useSsl)> servers)
    {
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        if (servers == null)
            throw new ArgumentNullException(nameof(servers));

        var tasks = new List<Task>();

        foreach (var (name, host, port, useSsl) in servers)
        {
            var config = new IrcClientOptions
            {
                Server = host,
                Port = port,
                UseSsl = useSsl,
                Nick = name, // Set nick to the bot name
                UserName = template.UserName,
                RealName = template.RealName,
                Password = template.Password,
                AutoReconnect = template.AutoReconnect,
                MaxReconnectAttempts = template.MaxReconnectAttempts,
                ConnectionTimeoutMs = template.ConnectionTimeoutMs,
                PingIntervalMs = template.PingIntervalMs,
                PingTimeoutMs = template.PingTimeoutMs,
                RequestedCapabilities = template.RequestedCapabilities,
                Sasl = template.Sasl
            };

            tasks.Add(AddBotAsync(name, config));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds multiple bots from a template configuration (params version).
    /// </summary>
    /// <param name="template">The template configuration.</param>
    /// <param name="servers">The server configurations (name, host, port, useSsl)</param>
    public async Task AddBotsFromTemplateAsync(
        IrcClientOptions template,
        params (string name, string host, int port, bool useSsl)[] servers)
    {
        await AddBotsFromTemplateAsync(template, (IEnumerable<(string, string, int, bool)>)servers).ConfigureAwait(false);
    }    /// <summary>
         /// Gets the status of all bots.
         /// </summary>
         /// <returns>A dictionary of bot names and their status information</returns>
    public Dictionary<string, BotStatus> GetAllBotStatus()
    {
        var statuses = new Dictionary<string, BotStatus>();

        foreach (var kvp in _bots)
        {
            statuses[kvp.Key] = new BotStatus
            {
                Name = kvp.Key,
                IsConnected = kvp.Value.IsConnected,
                IsRegistered = kvp.Value.IsRegistered,
                CurrentNick = kvp.Value.CurrentNick,
                Server = "unknown", // Cannot access options directly
                Port = 0, // Cannot access options directly
                EnabledCapabilities = kvp.Value.EnabledCapabilities,
                ChannelCount = kvp.Value.Channels.Count
            };
        }

        return statuses;
    }

    /// <summary>
    /// Status information for a managed bot.
    /// </summary>
    public class BotStatus
    {
        /// <summary>The bot's registered name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Whether the bot is currently connected.</summary>
        public bool IsConnected { get; set; }
        /// <summary>Whether the bot has completed registration.</summary>
        public bool IsRegistered { get; set; }
        /// <summary>The bot's current nickname.</summary>
        public string CurrentNick { get; set; } = string.Empty;
        /// <summary>Server hostname the bot is connected to.</summary>
        public string Server { get; set; } = string.Empty;
        /// <summary>Server port number.</summary>
        public int Port { get; set; }
        /// <summary>IRCv3 capabilities enabled on this connection.</summary>
        /// <returns>A new HashSet snapshot.</returns>
        public IReadOnlySet<string> EnabledCapabilities { get; set; } = new HashSet<string>();
        /// <summary>Number of channels the bot is currently in.</summary>
        public int ChannelCount { get; set; }
    }

    /// <summary>
    /// Broadcasts a message to a specific channel on all connected bots.
    /// </summary>
    /// <param name="channel">The channel to send the message to.</param>
    /// <param name="message">The message to send.</param>
    public async Task BroadcastToChannelAsync(string channel, string message)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel cannot be null or whitespace", nameof(channel));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null or whitespace", nameof(message));

        var tasks = new List<Task>();

        foreach (var kvp in _bots)
        {
            if (kvp.Value.IsConnected && kvp.Value.Channels.ContainsKey(channel))
            {
                tasks.Add(kvp.Value.SendMessageAsync(channel, message));
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _logger?.LogInformation("Broadcasted message to channel {Channel} on {BotCount} bots", channel, tasks.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Some bots failed to broadcast message to channel {Channel}", channel);
            }
        }
    }/// <summary>
     /// Starts all managed bots.
     /// </summary>
     /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task StartAllBotsAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        foreach (var kvp in _bots)
        {
            if (!kvp.Value.IsConnected)
            {
                tasks.Add(kvp.Value.ConnectAsync(cancellationToken));
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _logger?.LogInformation("Started {BotCount} bots", tasks.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Some bots failed to start");
            }
        }
    }

    /// <summary>
    /// Stops all managed bots.
    /// </summary>
    /// <param name="reason">The disconnect reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAllBotsAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();

        foreach (var kvp in _bots)
        {
            if (kvp.Value.IsConnected)
            {
                tasks.Add(kvp.Value.DisconnectAsync(reason ?? "Bot manager shutdown"));
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _logger?.LogInformation("Stopped {BotCount} bots", tasks.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Some bots failed to stop gracefully");
            }
        }
    }

    /// <summary>
    /// Starts the bot manager (IHostedService implementation).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IrcBotManager));

        _isRunning = true;
        _logger?.LogInformation("IRC Bot Manager started");        // Start all existing bots
        await StartAllBotsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the bot manager (IHostedService implementation).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        _isRunning = false;
        _logger?.LogInformation("IRC Bot Manager stopping");        // Stop all bots
        await StopAllBotsAsync("Service shutdown", cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("IRC Bot Manager stopped");
    }

    /// <summary>
    /// Disposes the bot manager and all managed bots.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isRunning = false;

        // Dispose all bots synchronously
        foreach (var kvp in _bots)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing bot '{BotName}'", kvp.Key);
            }
        }

        _bots.Clear();
        _logger?.LogInformation("IRC Bot Manager disposed");
    }

    /// <summary>
    /// Asynchronously disposes the bot manager and all managed bots.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isRunning = false;        // Stop all bots first
        await StopAllBotsAsync("Manager disposal").ConfigureAwait(false);

        // Dispose all bots
        var disposeTasks = new List<ValueTask>();
        foreach (var kvp in _bots)
        {
            try
            {
                disposeTasks.Add(kvp.Value.DisposeAsync());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing bot '{BotName}'", kvp.Key);
            }
        }

        await Task.WhenAll(disposeTasks.Select(vt => vt.AsTask())).ConfigureAwait(false);

        _bots.Clear();
        _logger?.LogInformation("IRC Bot Manager disposed");
    }

    /// <summary>
    /// Gets server information for a specific bot using protocol enhancements.
    /// </summary>
    /// <param name="botName">The name of the bot.</param>
    /// <returns>Server information if the bot exists and is connected</returns>
    public ServerInfo? GetServerInfo(string botName)
    {
        if (_bots.TryGetValue(botName, out var bot) && bot.IsConnected)
        {
            return new ServerInfo
            {
                NetworkName = bot.GetServerNetworkName(),
                MaxNicknameLength = bot.GetServerMaxNicknameLength(),
                MaxChannelLength = bot.GetServerMaxChannelLength(),
                ChannelTypes = bot.GetServerChannelTypes()
            };
        }
        return null;
    }

    /// <summary>
    /// Compares nicknames across all bots using their respective server case mappings.
    /// </summary>
    /// <param name="nick1">First nickname.</param>
    /// <param name="nick2">Second nickname.</param>
    /// <returns>Dictionary of bot names and whether the nicknames are equal on each server</returns>
    public Dictionary<string, bool> CompareNicknames(string nick1, string nick2)
    {
        var results = new Dictionary<string, bool>();
        foreach (var kvp in _bots)
        {
            if (kvp.Value.IsConnected)
            {
                results[kvp.Key] = kvp.Value.NicknamesEqual(nick1, nick2);
            }
        }
        return results;
    }

    /// <summary>
    /// Server information extracted from ISUPPORT (005) parameters.
    /// </summary>
    public class ServerInfo
    {
        /// <summary>Network name (e.g. "Libera.Chat").</summary>
        public string? NetworkName { get; set; }
        /// <summary>Maximum allowed nickname length.</summary>
        public int MaxNicknameLength { get; set; }
        /// <summary>Maximum allowed channel name length.</summary>
        public int MaxChannelLength { get; set; }
        /// <summary>Supported channel prefix characters (e.g. "#&amp;").</summary>
        public string ChannelTypes { get; set; } = string.Empty;
    }
}
