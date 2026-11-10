using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Scarlet.Extensions.Hosting.Avalonia.Tests;

/// <summary>
/// <see cref="AvaloniaLifetime"/> and the host extensions, without the UI thread.
/// </summary>
public class LifetimeTests
{
    [Fact]
    public void UseAvaloniaLifetimeReplacesTheConsoleLifetime()
    {
        using var host = Host.CreateDefaultBuilder().UseAvaloniaLifetime().Build();

        Assert.IsType<AvaloniaLifetime>(host.Services.GetRequiredService<IHostLifetime>());
    }

    [Fact]
    public void UseAvaloniaLifetimeConfiguresItsOptions()
    {
        using var host = Host.CreateDefaultBuilder().UseAvaloniaLifetime(static o => o.SuppressStatusMessages = true).Build();

        Assert.IsType<AvaloniaLifetime>(host.Services.GetRequiredService<IHostLifetime>());
        Assert.True(host.Services.GetRequiredService<IOptions<AvaloniaLifetimeOptions>>().Value.SuppressStatusMessages);
    }

    [Fact]
    public void UseAvaloniaLifetimeRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(static () => HostBuilderExtensions.UseAvaloniaLifetime(null!));
        Assert.Throws<ArgumentNullException>(static () => HostBuilderExtensions.UseAvaloniaLifetime(null!, static _ => { }));
        Assert.Throws<ArgumentNullException>(static () => Host.CreateDefaultBuilder().UseAvaloniaLifetime(null!));
    }

    [Fact]
    public async Task ProcessExitStopsTheHostAndWaitsForItToBeDisposed()
    {
        var applicationLifetime = new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance);
        var logger = new CapturingLoggerFactory();
        var lifetime = CreateLifetime(applicationLifetime, TimeSpan.FromSeconds(10), logger);
        var stopping = false;
        applicationLifetime.ApplicationStopping.Register(() => stopping = true);

        // Disposing the lifetime (the host being disposed) releases the process exit at once.
        var cancellationToken = TestContext.Current.CancellationToken;
        var disposeSoon = Task.Run(
            async () =>
            {
                await Task.Delay(100, cancellationToken);
                lifetime.Dispose();
            },
            cancellationToken);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        lifetime.OnProcessExit(null, EventArgs.Empty);

        Assert.True(stopping);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Took {watch.Elapsed}.");
        Assert.DoesNotContain(logger.Messages, static m => m.Contains("Waiting for the host", StringComparison.Ordinal));
        await disposeSoon;
    }

    [Fact]
    public void ProcessExitGivesUpOnAHostThatIsNeverDisposedAfterTwiceTheShutdownTimeout()
    {
        var logger = new CapturingLoggerFactory();
        using var lifetime = CreateLifetime(new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance), TimeSpan.FromMilliseconds(200), logger);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        lifetime.OnProcessExit(null, EventArgs.Empty);

        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(350), TimeSpan.FromSeconds(5));
        Assert.Contains(logger.Messages, static m => m.Contains("Waiting for the host", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLifetimeWorksWithoutALoggerFactory()
    {
        using var lifetime = new AvaloniaLifetime(
            Options.Create(new AvaloniaLifetimeOptions()),
            new HostingEnvironment { EnvironmentName = Environments.Production },
            new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
            Options.Create(new HostOptions { ShutdownTimeout = TimeSpan.Zero }));

        lifetime.OnProcessExit(null, EventArgs.Empty);
    }

    [Fact]
    public void RunAvaloniaRequiresAnStaThreadOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Apartments only exist on Windows.");
            return;
        }

        using var host = Host.CreateApplicationBuilder().Build();

        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(host.RunAvalonia));
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();

        var exception = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("STA thread", exception.Message, StringComparison.Ordinal);
        Assert.Contains("An async Main is never STA", exception.Message, StringComparison.Ordinal);
    }

    private static AvaloniaLifetime CreateLifetime(IHostApplicationLifetime applicationLifetime, TimeSpan shutdownTimeout, ILoggerFactory loggerFactory) =>
        new(
            Options.Create(new AvaloniaLifetimeOptions()),
            new HostingEnvironment { EnvironmentName = Environments.Production },
            applicationLifetime,
            Options.Create(new HostOptions { ShutdownTimeout = shutdownTimeout }),
            loggerFactory);

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
