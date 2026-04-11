using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Extensions;

/// <summary>
/// Extension methods for registering IRC client services in a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IrcClient"/> configured via a fluent builder.
    /// Also registers <see cref="IrcClientOptions"/> and a <see cref="ThreadedEventDispatcher"/> as the default <see cref="IEventDispatcher"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configureOptions">Action that configures the <see cref="IrcClientOptionsBuilder"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIrcClient(
        this IServiceCollection services,
        Action<IrcClientOptionsBuilder> configureOptions)
    {
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        var builder = new IrcClientOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        return services.AddIrcClient(options);
    }

    /// <summary>
    /// Registers a singleton <see cref="IrcClient"/> with pre-built <see cref="IrcClientOptions"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="options">Pre-built and validated client configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIrcClient(
        this IServiceCollection services,
        IrcClientOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IrcClient>();
        services.AddSingleton<IEventDispatcher, ThreadedEventDispatcher>();

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="IrcClient"/> with configuration bound from <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configuration">The configuration root (e.g. from appsettings.json).</param>
    /// <param name="sectionName">The configuration section name to bind. Default: <c>"IrcClient"</c>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIrcClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "IrcClient")
    {
        var options = new IrcClientOptions();
        configuration.GetSection(sectionName).Bind(options);
        options.Validate();

        return services.AddIrcClient(options);
    }

    /// <summary>
    /// Registers <see cref="IrcBotManager"/> as a singleton <see cref="IHostedService"/> for managing multiple IRC client connections.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configure">Optional action to pre-configure client instances via <see cref="IrcBotManagerBuilder"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIrcBotManager(
        this IServiceCollection services,
        Action<IrcBotManagerBuilder>? configure = null)
    {
        services.AddSingleton<IrcBotManager>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IrcBotManager>());

        if (configure != null)
        {
            var builder = new IrcBotManagerBuilder(services);
            configure(builder);
        }

        return services;
    }

    /// <summary>
    /// Replaces the default event dispatcher with a custom <see cref="IEventDispatcher"/> implementation.
    /// </summary>
    /// <typeparam name="T">The dispatcher type (must implement <see cref="IEventDispatcher"/>).</typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIrcEventDispatcher<T>(this IServiceCollection services)
        where T : class, IEventDispatcher
    {
        services.AddSingleton<IEventDispatcher, T>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ThreadedEventDispatcher"/> — each handler runs in its own <see cref="Task"/>. This is the default.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddThreadedEventDispatcher(this IServiceCollection services)
    {
        return services.AddIrcEventDispatcher<ThreadedEventDispatcher>();
    }

    /// <summary>
    /// Registers <see cref="SequentialEventDispatcher"/> — handlers run one after another in registration order.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSequentialEventDispatcher(this IServiceCollection services)
    {
        return services.AddIrcEventDispatcher<SequentialEventDispatcher>();
    }

    /// <summary>
    /// Registers <see cref="BackgroundEventDispatcher"/> — specific handlers run in dedicated background processing queues.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBackgroundEventDispatcher(this IServiceCollection services)
    {
        return services.AddIrcEventDispatcher<BackgroundEventDispatcher>();
    }
}

/// <summary>
/// Fluent builder for configuring <see cref="IrcBotManager"/> instances and their client connections during DI registration.
/// </summary>
public class IrcBotManagerBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<BotConfiguration> _botConfigurations = new();

    internal IrcBotManagerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers a named IRC client connection configured via a fluent builder.
    /// </summary>
    /// <param name="name">Unique identifier for this connection (used to retrieve it from <see cref="IrcBotManager"/>).</param>
    /// <param name="configureOptions">Action that configures the <see cref="IrcClientOptionsBuilder"/> for this connection.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcBotManagerBuilder AddBot(
        string name,
        Action<IrcClientOptionsBuilder> configureOptions)
    {
        var builder = new IrcClientOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        _botConfigurations.Add(new BotConfiguration(name, options));
        return this;
    }

    /// <summary>
    /// Registers a named IRC client connection with pre-built configuration.
    /// </summary>
    /// <param name="name">Unique identifier for this connection.</param>
    /// <param name="options">Pre-built <see cref="IrcClientOptions"/> for this connection.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcBotManagerBuilder AddBot(string name, IrcClientOptions options)
    {
        _botConfigurations.Add(new BotConfiguration(name, options));
        return this;
    }

    /// <summary>
    /// Configures startup behavior for the <see cref="IrcBotManager"/> hosted service.
    /// </summary>
    /// <param name="configure">Action that configures <see cref="IrcBotManagerStartupOptions"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public IrcBotManagerBuilder ConfigureStartup(Action<IrcBotManagerStartupOptions> configure)
    {
        var options = new IrcBotManagerStartupOptions();
        configure(options);

        _services.AddSingleton(options);
        return this;
    }

    internal List<BotConfiguration> GetConfigurations() => _botConfigurations;
}

/// <summary>
/// Configuration for a single named client connection within <see cref="IrcBotManager"/>.
/// </summary>
internal record BotConfiguration(string Name, IrcClientOptions Options);

/// <summary>
/// Controls how <see cref="IrcBotManager"/> starts its managed connections when the hosted service starts.
/// </summary>
public class IrcBotManagerStartupOptions
{
    /// <summary>
    /// Whether to connect all registered clients automatically when the service starts. Default: <c>true</c>.
    /// </summary>
    public bool AutoStartBots { get; set; } = true;

    /// <summary>
    /// Delay between starting each client connection, to avoid overwhelming the network. Default: 1 second.
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to continue starting remaining clients if one fails to connect. Default: <c>true</c>.
    /// </summary>
    public bool ContinueOnStartupFailure { get; set; } = true;

    /// <summary>
    /// Maximum total time to wait for all clients to start before giving up. Default: 5 minutes.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
