using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// Starts the Avalonia application in <see cref="StartedAsync"/>, i.e. only after every hosted service has started successfully
/// (like ASP.NET Core starts serving requests): if any hosted service fails to start, the UI is never created. Stops it in
/// <see cref="StoppingAsync"/>, i.e. before any hosted service's StopAsync, and waits for the UI thread to end, so application
/// cleanup runs while the other hosted services and all registered services still work.
/// </summary>
/// <remarks>
/// Registered first, so constructing it (with the UI thread, which claims Avalonia's UI dispatcher) comes before constructing
/// any other hosted service: none of them can take Avalonia's UI thread, even in its constructor.
/// </remarks>
internal sealed class AvaloniaHostedService(ILogger<AvaloniaHostedService> logger, AvaloniaThread thread) : IHostedLifecycleService
{
    // The UI thread can't run (its dispatcher couldn't be claimed): fail host startup before any hosted service starts.
    public Task StartingAsync(CancellationToken cancellationToken) =>
        thread.ClaimError is { } error ? Task.FromException(error) : Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        logger.AvaloniaStarting();
        thread.Start();

        try
        {
            // Wait until the application is set up and its main loop runs, so failures until then fail host startup instead of
            // crashing the process.
            await thread.Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            logger.AvaloniaRunning();
        }
        catch
        {
            // Startup failed or was cancelled: the host will be disposed without stopping its services. End the UI thread now,
            // while the host's services still work.
            thread.StopAndWait();
            throw;
        }
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        if (thread.RequestExit())
        {
            logger.AvaloniaStopping();
        }

        // Exiting only queues the exit. Wait until the UI thread has ended, i.e. windows were closed, OnExit ran and the components
        // were disposed, while the other hosted services still run and no service has been disposed yet.
        try
        {
            await thread.Completed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host's shutdown timeout elapsed: the host goes on stopping without waiting for the UI thread any longer.
            thread.OnStopTimedOut();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
