using Avalonia.Threading;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// The Avalonia UI thread run by the host.
/// </summary>
public interface IAvaloniaThread
{
    /// <summary>
    /// Gets the UI thread: a dedicated thread with <c>host.Run()</c>, or the thread that called
    /// <see cref="HostExtensions.RunAvalonia"/>. It exists before the host starts, so it can be handed to e.g. a
    /// <c>JoinableTaskContext</c> while services are being constructed.
    /// </summary>
    Thread MainThread { get; }

    /// <summary>
    /// Gets Avalonia's UI dispatcher (<see cref="Dispatcher.UIThread"/>), which the host claims for <see cref="MainThread"/>
    /// before any hosted service starts. Work posted before the application has started runs once its main loop runs, or
    /// earlier if startup code pumps a nested dispatcher frame (e.g. <c>Dispatcher.PushFrame</c> in a component), while
    /// <see cref="IAvaloniaContext.IsRunning"/> is still <see langword="false"/>. Work still queued when it exits, or if it
    /// never starts, is aborted: <c>InvokeAsync</c> callers get a cancelled task.
    /// Do not await it inside a hosted service's StartAsync: the UI starts only after every StartAsync has returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">Avalonia's UI thread was already taken by another thread.</exception>
    Dispatcher Dispatcher { get; }

    /// <summary>
    /// Gets a <see cref="SynchronizationContext"/> that runs work on the UI thread. <c>Post</c> works at any time and never
    /// throws (work posted after the application has ended is dropped). <c>Send</c> throws before the application runs,
    /// instead of blocking: a Send from a hosted service's StartAsync would otherwise deadlock.
    /// </summary>
    SynchronizationContext SynchronizationContext { get; }

    /// <summary>
    /// Gets the state of the Avalonia application.
    /// </summary>
    IAvaloniaContext Context { get; }

    /// <summary>
    /// Stops the host, which exits the Avalonia application. Callable from any thread, e.g. from a tray icon's Exit item.
    /// Before the application has started (e.g. from <c>OnStartup</c>), the host is stopped once it has: stopping a host
    /// while it starts would fail its startup. Ignored when startup failed or the host is being disposed.
    /// </summary>
    void HandleApplicationExit();

    /// <summary>
    /// Raised on the UI thread when work dispatched to it throws (Avalonia's <see cref="Dispatcher.UnhandledException"/>).
    /// Set <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/> to keep the application running; otherwise the
    /// exception is logged and terminates the process, like WPF's <c>DispatcherUnhandledException</c>.
    /// </summary>
    event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException;
}
