using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;

namespace ScenarioApp;

/// <summary>
/// Reports its lifecycle and on which thread it happens.
/// </summary>
internal sealed class ProbeComponent : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent() => Program.Report("ComponentInit", Program.Where());

    public void Dispose() => Program.Report("ComponentDispose", Program.Where());
}

/// <summary>
/// Fails to initialize.
/// </summary>
internal sealed class FailingComponent : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent()
    {
        Program.Report("FailingComponentInit", Program.Where());
        throw new InvalidOperationException("boom");
    }

    public void Dispose() => Program.Report("FailingComponentDispose", Program.Where());
}

/// <summary>
/// Shows an Avalonia window; optionally one whose Closing handler vetoes every close.
/// </summary>
internal sealed class WindowComponent(bool vetoClose = false) : IAvaloniaComponent
{
    public static Window? Window { get; private set; }

    public void InitializeComponent()
    {
        Window = new Window { Title = "Scarlet scenario window", Width = 240, Height = 120 };
        if (vetoClose)
        {
            Window.Closing += (_, e) =>
            {
                Program.Report("CloseVetoed", Program.Where());
                e.Cancel = true;
            };
        }

        Window.Show();
        Program.Report("WindowCreated", Program.Where());
    }
}

/// <summary>
/// Exits the application while it initializes, through the lifetime's Shutdown.
/// </summary>
internal sealed class ShuttingDownComponent(IAvaloniaContext context) : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent()
    {
        Program.Report("ShuttingDownComponentInit", Program.Where());
        context.Lifetime!.Shutdown();
    }

    public void Dispose() => Program.Report("ShuttingDownComponentDispose", Program.Where());
}

/// <summary>
/// Must never be created: it comes after a component that exits the application.
/// </summary>
internal sealed class NeverCreatedComponent : IAvaloniaComponent, IDisposable
{
    public NeverCreatedComponent() => Program.Report("NeverCreatedComponentCreated", Program.Where());

    public void InitializeComponent() => Program.Report("NeverCreatedComponentInit", Program.Where());

    public void Dispose() => Program.Report("NeverCreatedComponentDispose", Program.Where());
}

/// <summary>
/// Initializes slower than the host is willing to wait for startup.
/// </summary>
internal sealed class SlowComponent : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent()
    {
        Program.Report("SlowInitStarted", Program.Where());
        Thread.Sleep(1500);
        Program.Report("SlowInitFinished", Program.Where());
    }

    public void Dispose() => Program.Report("SlowComponentDispose", Program.Where());
}

/// <summary>
/// Throws from Dispose.
/// </summary>
internal sealed class ThrowingDisposeComponent : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent()
    {
    }

    public void Dispose()
    {
        Program.Report("ThrowingDispose", Program.Where());
        throw new InvalidOperationException("component dispose failure");
    }
}

/// <summary>
/// A service a component uses until it is disposed.
/// </summary>
internal sealed class GuardedDependency : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose()
    {
        Disposed = true;
        Program.Report("DependencyDisposed", Program.Where());
    }
}

/// <summary>
/// Reports whether its dependency is still usable when the UI thread disposes it.
/// </summary>
internal sealed class DependentComponent(GuardedDependency dependency) : IAvaloniaComponent, IDisposable
{
    public void InitializeComponent() => Program.Report("DependentComponentInit", Program.Where());

    public void Dispose() => Program.Report("DependentComponentDispose", $"{Program.Where()}:{(dependency.Disposed ? "dependency-disposed" : "dependency-alive")}");
}

/// <summary>
/// Runs an action once the host has started, off the UI thread.
/// </summary>
internal sealed class OnStartedService(IHostApplicationLifetime lifetime, IServiceProvider services, Action<IServiceProvider> action) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => Task.Run(() => action(services)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Posts and invokes to the UI thread from StartAsync, i.e. before the application runs, with an AsyncLocal set.
/// </summary>
internal sealed class PrestartPostService(IAvaloniaThread thread) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Program.Report("PrestartPosting", thread.Context.IsRunning ? "running" : "not-running");
        ContextPostService.Request.Value = "prestart-caller";
        thread.SynchronizationContext.Post(_ => Program.Report("PrestartPostRan", $"{Program.Where()}:{ContextPostService.Request.Value}"), null);
        thread.Dispatcher.Post(() => Program.Report("PrestartDispatcherPostRan", $"{Program.Where()}:{ContextPostService.Request.Value}"));
        _ = thread.Dispatcher.InvokeAsync(() => Program.Report("PrestartInvocationRan", $"{Program.Where()}:{ContextPostService.Request.Value}"));
        ContextPostService.Request.Value = null;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Queues an invocation from StartAsync for an application that never starts.
/// </summary>
internal sealed class AbandonedInvocationService(IAvaloniaThread thread) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Scenarios.SetAbandonedInvocation(thread.Dispatcher.InvokeAsync(() => Program.Report("AbandonedInvocationRan", Program.Where())).GetTask());
        thread.SynchronizationContext.Post(_ => Program.Report("AbandonedPostRan", Program.Where()), null);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Fails in StartAsync, before the UI starts.
/// </summary>
internal sealed class FailingStartService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("start failure");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Fails after the UI has started: the host then disposes its services without stopping them.
/// </summary>
internal sealed class FailingStartedService : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("started failure");

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Sends to the UI thread from StartAsync, before the application runs: must throw instead of deadlocking the host.
/// </summary>
internal sealed class SendBeforeStartService(IAvaloniaThread thread) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Scenarios.SendAndReport(thread.SynchronizationContext, "SendBeforeStart");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Posts from StartAsync, before the application runs, with an AsyncLocal set: the work must still see it.
/// </summary>
internal sealed class ContextPostService(IAvaloniaThread thread) : IHostedService
{
    public static AsyncLocal<string?> Request { get; } = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Request.Value = "prestart-caller";
        thread.SynchronizationContext.Post(_ => Program.Report("PrestartPost", Request.Value), null);
        Request.Value = null;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Uses Avalonia's UI dispatcher from its constructor, on a thread pool thread, directly and through TrayIcon's static
/// constructor, which subscribes to Dispatcher.UIThread.
/// </summary>
internal sealed class UiThreadTouchingService : IHostedService
{
    public UiThreadTouchingService()
    {
        Task.Run(() =>
        {
            _ = Dispatcher.UIThread.CheckAccess();
            RuntimeHelpers.RunClassConstructor(typeof(TrayIcon).TypeHandle);
        }).GetAwaiter().GetResult();
        Program.Report("TouchedUiThread", "other");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Takes a while to stop, after the UI has ended; signals when it starts and finishes stopping.
/// </summary>
internal sealed class SlowStopService : IHostedService
{
    public static TaskCompletionSource Stopping { get; } = new();

    public static bool Stopped { get; private set; }

    /// <summary>
    /// Started on the UI thread: an operation that completes after the UI has ended, while the host still stops.
    /// </summary>
    public static async Task AwaitStopOnUiThread(IServiceProvider services)
    {
        var operation = Stopping.Task;
        services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await operation;
        Program.Report("ResumedAfterExit", Program.Where());
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Program.Report("OtherStopping", Program.Where());
        Stopping.TrySetResult();
        await Task.Delay(500, CancellationToken.None);
        Stopped = true;
        Program.Report("OtherStopped", Program.Where());
    }
}
