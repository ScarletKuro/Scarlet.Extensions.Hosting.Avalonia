using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scarlet.Extensions.Hosting.Avalonia.Internal;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// <see cref="IHostLifetime"/> for a windowed Avalonia application, used instead of the default ConsoleLifetime: it does not
/// handle Ctrl+C or print console shutdown hints, logs status messages (see <see cref="AvaloniaLifetimeOptions"/>), and waits for
/// the host to shut down gracefully when the process exits. Stopping the host when the Avalonia application exits does not
/// depend on it.
/// </summary>
public class AvaloniaLifetime : IHostLifetime, IDisposable
{
#pragma warning disable CA2213 // Set (not disposed) in Dispose: OnProcessExit may still be waiting on it.
    private readonly ManualResetEvent _shutdownBlock = new(false);
#pragma warning restore CA2213
    private CancellationTokenRegistration _applicationStartedRegistration;
    private CancellationTokenRegistration _applicationStoppingRegistration;
    private CancellationTokenRegistration _applicationStoppedRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaLifetime"/> class.
    /// </summary>
    /// <param name="options">The lifetime options.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="applicationLifetime">The application lifetime.</param>
    /// <param name="hostOptions">The host options.</param>
    public AvaloniaLifetime(
        IOptions<AvaloniaLifetimeOptions> options,
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        IOptions<HostOptions> hostOptions)
        : this(options, environment, applicationLifetime, hostOptions, NullLoggerFactory.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaLifetime"/> class.
    /// </summary>
    /// <param name="options">The lifetime options.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="applicationLifetime">The application lifetime.</param>
    /// <param name="hostOptions">The host options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public AvaloniaLifetime(
        IOptions<AvaloniaLifetimeOptions> options,
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        IOptions<HostOptions> hostOptions,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value, nameof(options));
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(hostOptions.Value, nameof(hostOptions));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Options = options.Value;
        Environment = environment;
        ApplicationLifetime = applicationLifetime;
        HostOptions = hostOptions.Value;
        Logger = loggerFactory.CreateLogger("Microsoft.Hosting.Lifetime");
    }

    private AvaloniaLifetimeOptions Options { get; }

    private IHostEnvironment Environment { get; }

    private IHostApplicationLifetime ApplicationLifetime { get; }

    private HostOptions HostOptions { get; }

    private ILogger Logger { get; }

    /// <inheritdoc />
    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        _applicationStartedRegistration = ApplicationLifetime.ApplicationStarted
            .Register(static state => ((AvaloniaLifetime)state!).OnApplicationStarted(), this);
        _applicationStoppingRegistration = ApplicationLifetime.ApplicationStopping
            .Register(static state => ((AvaloniaLifetime)state!).OnApplicationStopping(), this);
        _applicationStoppedRegistration = ApplicationLifetime.ApplicationStopped
            .Register(static state => ((AvaloniaLifetime)state!).OnApplicationStopped(), this);

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // Applications start immediately.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnApplicationStarted()
    {
        if (!Options.SuppressStatusMessages)
        {
            Logger.AvaloniaApplicationStarted();
            Logger.LifeTime(nameof(AvaloniaLifetime));
            Logger.HostingEnvironment(Environment.EnvironmentName);
            Logger.ContentRootPath(Environment.ContentRootPath);
        }
    }

    private void OnApplicationStopping()
    {
        if (!Options.SuppressStatusMessages)
        {
            Logger.AvaloniaApplicationShuttingDown();
        }
    }

    private void OnApplicationStopped()
    {
        if (!Options.SuppressStatusMessages)
        {
            Logger.AvaloniaApplicationStopped();
        }
    }

    internal void OnProcessExit(object? sender, EventArgs e)
    {
        ApplicationLifetime.StopApplication();
        if (!_shutdownBlock.WaitOne(HostOptions.ShutdownTimeout))
        {
            Logger.WaitingHost();
        }

        // Wait one more time after the above log message, but only for ShutdownTimeout, so it doesn't hang forever.
        _shutdownBlock.WaitOne(HostOptions.ShutdownTimeout);

        // Unlike ConsoleLifetime, do not reset Environment.ExitCode: there is no SIGTERM (143) to suppress for a windowed app,
        // and it would discard an exit code the application set.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the registrations and unblocks a pending process exit.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdownBlock.Set();

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            _applicationStartedRegistration.Dispose();
            _applicationStoppingRegistration.Dispose();
            _applicationStoppedRegistration.Dispose();
        }
    }
}
