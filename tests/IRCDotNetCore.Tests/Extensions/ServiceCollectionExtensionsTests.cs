using FluentAssertions;
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;
using IRCDotNet.Core.Events;
using IRCDotNet.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace IRCDotNet.Tests.Extensions;

/// <summary>
/// Tests for ServiceCollectionExtensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIrcClient_WithAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddIrcClient(builder => builder
            .WithNick("TestBot")
            .WithUserName("testbot")
            .WithRealName("Test Bot")
            .AddServer("test.server.com", 6667));

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetService<IrcClientOptions>().Should().NotBeNull();
        serviceProvider.GetService<IrcClient>().Should().NotBeNull();
        serviceProvider.GetService<IEventDispatcher>().Should().NotBeNull();

        var options = serviceProvider.GetRequiredService<IrcClientOptions>();
        options.Nick.Should().Be("TestBot");
        options.Server.Should().Be("test.server.com");
    }

    [Fact]
    public void AddIrcClient_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        services.Invoking(s => s.AddIrcClient((Action<IrcClientOptionsBuilder>)null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddIrcClient_WithOptions_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "TestBot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act
        services.AddIrcClient(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetService<IrcClientOptions>().Should().BeSameAs(options);
        serviceProvider.GetService<IrcClient>().Should().NotBeNull();
        serviceProvider.GetService<IEventDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public void AddIrcClient_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        services.Invoking(s => s.AddIrcClient((IrcClientOptions)null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void AddIrcClient_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationData = new Dictionary<string, string?>
        {
            ["IrcClient:Server"] = "test.server.com",
            ["IrcClient:Nick"] = "ConfigBot",
            ["IrcClient:UserName"] = "configbot",
            ["IrcClient:RealName"] = "Config Bot"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        // Act
        services.AddIrcClient(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IrcClientOptions>();
        options.Server.Should().Be("test.server.com");
        options.Nick.Should().Be("ConfigBot");
        options.UserName.Should().Be("configbot");
        options.RealName.Should().Be("Config Bot");
    }

    [Fact]
    public void AddIrcClient_WithConfigurationAndCustomSection_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationData = new Dictionary<string, string?>
        {
            ["CustomSection:Server"] = "custom.server.com",
            ["CustomSection:Nick"] = "CustomBot",
            ["CustomSection:UserName"] = "custombot",
            ["CustomSection:RealName"] = "Custom Bot"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        // Act
        services.AddIrcClient(configuration, "CustomSection");

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IrcClientOptions>();
        options.Server.Should().Be("custom.server.com");
        options.Nick.Should().Be("CustomBot");
    }

    [Fact]
    public void AddIrcBotManager_ShouldRegisterBotManager()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddIrcBotManager();

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetService<IrcBotManager>().Should().NotBeNull();
        serviceProvider.GetService<IHostedService>().Should().NotBeNull();

        var hostedService = serviceProvider.GetRequiredService<IHostedService>();
        hostedService.Should().BeOfType<IrcBotManager>();
    }

    [Fact]
    public void AddIrcBotManager_WithConfiguration_ShouldCallConfigureAction()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureWasCalled = false;

        // Act
        services.AddIrcBotManager(builder =>
        {
            configureWasCalled = true;
        });

        // Assert
        configureWasCalled.Should().BeTrue();

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetService<IrcBotManager>().Should().NotBeNull();
    }

    [Fact]
    public void AddIrcEventDispatcher_ShouldRegisterCustomDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddIrcEventDispatcher<MockEventDispatcher>();

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IEventDispatcher>();
        dispatcher.Should().BeOfType<MockEventDispatcher>();
    }

    [Fact]
    public void AddThreadedEventDispatcher_ShouldRegisterThreadedDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddThreadedEventDispatcher();

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IEventDispatcher>();
        dispatcher.Should().BeOfType<ThreadedEventDispatcher>();
    }

    [Fact]
    public void AddIrcClient_ShouldRegisterSingletonServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "TestBot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act
        services.AddIrcClient(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var options1 = serviceProvider.GetRequiredService<IrcClientOptions>();
        var options2 = serviceProvider.GetRequiredService<IrcClientOptions>();
        options1.Should().BeSameAs(options2);

        var client1 = serviceProvider.GetRequiredService<IrcClient>();
        var client2 = serviceProvider.GetRequiredService<IrcClient>();
        client1.Should().BeSameAs(client2);

        var dispatcher1 = serviceProvider.GetRequiredService<IEventDispatcher>();
        var dispatcher2 = serviceProvider.GetRequiredService<IEventDispatcher>();
        dispatcher1.Should().BeSameAs(dispatcher2);
    }

    [Fact]
    public void AddIrcClient_WithInvalidOptions_ShouldThrowDuringValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        var invalidOptions = new IrcClientOptions(); // Missing required fields

        // Act & Assert
        services.Invoking(s => s.AddIrcClient(invalidOptions))
            .Should().Throw<ArgumentException>(); // From options.Validate()
    }

    [Fact]
    public void ServiceRegistration_ShouldAllowMultipleEventDispatchers()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "TestBot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act
        services.AddIrcClient(options);
        services.AddIrcEventDispatcher<MockEventDispatcher>(); // This should override the default

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IEventDispatcher>();
        dispatcher.Should().BeOfType<MockEventDispatcher>();
    }

    [Fact]
    public void ServiceCollection_ShouldReturnSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new IrcClientOptions
        {
            Server = "test.server.com",
            Nick = "TestBot",
            UserName = "testbot",
            RealName = "Test Bot"
        };

        // Act
        var result = services.AddIrcClient(options);

        // Assert
        result.Should().BeSameAs(services); // Fluent interface
    }
}

/// <summary>
/// Mock event dispatcher for testing
/// </summary>
public class MockEventDispatcher : IEventDispatcher
{
    public int HandlerCount => 0;

    public void AddHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        // Mock implementation
    }

    public void RemoveHandler<T>(Func<T, Task> handler) where T : IrcEvent
    {
        // Mock implementation
    }

    public Task DispatchAsync<T>(T eventArgs, CancellationToken cancellationToken = default) where T : IrcEvent
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Mock implementation
    }
}
