using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Scarlet.Extensions.Hosting.Avalonia.Internal;

namespace Scarlet.Extensions.Hosting.Avalonia.Tests;

/// <summary>
/// Registrations that don't need the UI thread. Nothing here resolves <see cref="IAvaloniaThread"/>: that would claim Avalonia's
/// UI thread for this test process.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public void AddAvaloniaRegistersTheHostedServiceFirstSoItClaimsTheUiThreadBeforeOtherHostedServicesAreCreated()
    {
        var services = new ServiceCollection();
        services.AddHostedService<OtherHostedService>();
        services.AddAvalonia<TestApplication>(static builder => builder);

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        Assert.Equal(typeof(AvaloniaHostedService), hostedServices[0].ImplementationType);
        Assert.Equal(typeof(OtherHostedService), hostedServices[1].ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IAvaloniaThread));
        Assert.Single(services, d => d.ServiceType == typeof(IAvaloniaContext));
    }

    [Fact]
    public void AddingASecondAvaloniaApplicationIsRejected()
    {
        var services = new ServiceCollection().AddAvalonia<TestApplication>(static builder => builder);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddAvalonia<TestApplication>(static builder => builder));
        Assert.Contains("one application per process", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsAreConfiguredAndDefaultToClosingWithTheLastWindow()
    {
        using var defaults = new ServiceCollection().AddAvalonia<TestApplication>(static builder => builder).BuildServiceProvider();
        Assert.Equal(ShutdownMode.OnLastWindowClose, defaults.GetRequiredService<IOptions<AvaloniaOptions>>().Value.ShutdownMode);

        using var configured = new ServiceCollection()
            .AddAvalonia<TestApplication>(static builder => builder, static o => o.ShutdownMode = ShutdownMode.OnExplicitShutdown)
            .Configure<AvaloniaOptions>(static o => o.Args = ["--flag"])
            .BuildServiceProvider();
        var options = configured.GetRequiredService<IOptions<AvaloniaOptions>>().Value;
        Assert.Equal(ShutdownMode.OnExplicitShutdown, options.ShutdownMode);
        Assert.NotNull(options.Args);
        Assert.Equal(["--flag"], options.Args);
    }

    [Fact]
    public void TheContextExistsBeforeTheApplication()
    {
        using var provider = new ServiceCollection().AddAvalonia<TestApplication>(static builder => builder).BuildServiceProvider();

        var context = provider.GetRequiredService<IAvaloniaContext>();
        Assert.Same(provider.GetRequiredService<AvaloniaContext>(), context);
        Assert.False(context.IsRunning);
        Assert.Null(context.Application);
        Assert.Null(context.Lifetime);
    }

    [Fact]
    public void ComponentsAreRegisteredAsFactoriesInOrderAndNeverResolvedByTheContainer()
    {
        var created = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(created);
        services.AddAvaloniaComponent<FirstComponent>();
        services.AddAvaloniaComponent(sp => new SecondComponent(sp.GetRequiredService<List<string>>()));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAvaloniaComponent));
        using var provider = services.BuildServiceProvider();
        foreach (var factory in provider.GetServices<Func<IAvaloniaComponent>>())
        {
            factory().InitializeComponent();
        }

        Assert.Equal(["first", "second"], created);
    }

    [Fact]
    public void AvaloniaLifetimeReplacesTheHostLifetime()
    {
        var services = new ServiceCollection().AddAvaloniaLifetime(static o => o.SuppressStatusMessages = true);

        Assert.Equal(typeof(AvaloniaLifetime), services.Last(d => d.ServiceType == typeof(IHostLifetime)).ImplementationType);
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<AvaloniaLifetimeOptions>>().Value.SuppressStatusMessages);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddAvalonia<TestApplication>(null!));
        Assert.Throws<ArgumentNullException>(() => services.AddAvalonia<TestApplication>(static builder => builder, null!));
        Assert.Throws<ArgumentNullException>(() => services.AddAvaloniaComponent<FirstComponent>(null!));
        Assert.Throws<ArgumentNullException>(() => ((IHost)null!).RunAvalonia());
    }

    private sealed class TestApplication : Application;

    private sealed class OtherHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FirstComponent(List<string> created) : IAvaloniaComponent
    {
        public void InitializeComponent() => created.Add("first");
    }

    private sealed class SecondComponent(List<string> created) : IAvaloniaComponent
    {
        public void InitializeComponent() => created.Add("second");
    }
}
