using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// The UI thread. It claims Avalonia's UI dispatcher as soon as it is created, then waits until the host starts it
/// (<see cref="Start"/>, after every hosted service has started) or gives up on it (<see cref="StopAndWait"/>). Started, it
/// sets up the application with Avalonia's classic desktop lifetime, initializes the components and runs the main loop,
/// reporting the application as started once the loop runs (<see cref="Started"/>). Failures until then fail host startup.
/// Whoever stops the application early waits for the UI thread to end, up to the host's shutdown timeout, so application
/// cleanup doesn't run against services the host has already disposed.
/// </summary>
/// <remarks>
/// Two thread models share this code. By default the UI thread is a dedicated thread (Windows, Linux). With
/// <see cref="HostExtensions.RunAvalonia"/> it is the calling thread, which Avalonia requires on macOS (the main thread).
/// </remarks>
internal sealed class AvaloniaThread : IAvaloniaThread, IDisposable
{
    // Used if HostOptions are not registered; matches the HostOptions.ShutdownTimeout default.
    private const int DefaultStopTimeoutSeconds = 30;

    // How long a process exit waits for the UI thread to answer before treating it as unable to run (see OnProcessExit).
    private static readonly TimeSpan ProcessExitResponseTimeout = TimeSpan.FromSeconds(2);

    // Set by HostExtensions.RunAvalonia while it resolves this service on the thread that becomes the UI thread.
    [ThreadStatic]
    private static bool t_useCurrentThread;

    private static AvaloniaThread? s_running;
    private static int s_processExitRegistered;

    // How long stopping the application early waits for the UI thread to end: the host's shutdown timeout. Read once, when the
    // thread is created, because the service provider may already be disposing when it is needed.
    private readonly TimeSpan _stopTimeout;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AvaloniaThread> _logger;
    private readonly AvaloniaOptions _options;
    private readonly AvaloniaContext _context;
    private readonly AvaloniaApplicationDefinition _definition;
    private readonly HostSynchronizationContext _synchronizationContext;

    // Guards _disposed, _stopRequested, the gate decision and IsRunning together, so a stopped thread never runs the main loop.
    private readonly Lock _stateLock = new();

    // Holds the claimed UI thread until the host starts it or gives up on it.
    private readonly ManualResetEventSlim _gate = new(false);
    private bool _gateReleased;
    private bool _gateRun;

    // The UI thread never runs (a dedicated thread on macOS, where Avalonia requires the main thread).
    private readonly bool _neverRuns;

    private readonly DispatcherClaim _claim;

    private volatile bool _disposed;
    private volatile bool _stopRequested;

    // Set when the process exits while the UI thread can't run any more (see OnProcessExit).
    private volatile bool _exitingProcess;

    // Set when the host's stop already waited its whole shutdown timeout for the UI thread to end.
    private volatile bool _stopTimedOut;

    // UI thread only.
    private ClassicDesktopStyleApplicationLifetime? _lifetime;
    private DisposableList<IAvaloniaComponent>? _components;
    private bool _exiting;
    private bool _cleanedUp;

    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AvaloniaThread(IServiceProvider serviceProvider, IOptions<AvaloniaOptions> options, AvaloniaContext context, AvaloniaApplicationDefinition definition)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _context = context;
        _definition = definition;
        _logger = serviceProvider.GetService<ILogger<AvaloniaThread>>() ?? NullLogger<AvaloniaThread>.Instance;
        _stopTimeout = serviceProvider.GetService<IOptions<HostOptions>>()?.Value.ShutdownTimeout ?? TimeSpan.FromSeconds(DefaultStopTimeoutSeconds);
        _synchronizationContext = new HostSynchronizationContext(this);

        if (t_useCurrentThread)
        {
            UsesCurrentThread = true;
            MainThread = Thread.CurrentThread;
            _claim = DispatcherClaim.ForCurrentThread();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MainThread = new Thread(static () => { });
            _neverRuns = true;
            _claim = new ClaimFailed(new PlatformNotSupportedException(
                "On macOS, Avalonia runs on the process's main thread: start the host with host.RunAvalonia() from Main " +
                "(marked [STAThread]) instead of host.Run() or host.RunAsync()."));
        }
        else
        {
            // Started now, without the creating thread's ExecutionContext: AsyncLocal values, logging scopes and Activity.Current of
            // whoever resolves the host's services would otherwise leak into every UI callback for the application's whole lifetime.
            // Claimed before this constructor returns, so no code running after it can take Avalonia's UI thread.
            using var claimTaken = new ManualResetEventSlim(false);
            DispatcherClaim? claim = null;
            MainThread = new Thread(() =>
            {
                try
                {
                    claim = DispatcherClaim.ForCurrentThread();
                }
                finally
                {
                    claimTaken.Set();
                }

                RunUiThread(WaitForGate());
            })
            {
                Name = "Avalonia UI Thread",
                IsBackground = true,
            };

            if (OperatingSystem.IsWindows())
            {
                MainThread.SetApartmentState(ApartmentState.STA);
            }

            MainThread.UnsafeStart();
            claimTaken.Wait();

            _claim = claim ?? throw new UnreachableException("The Avalonia UI thread failed to claim Avalonia's dispatcher.");
        }
    }

    public event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException;

    public Thread MainThread { get; }

    public Dispatcher Dispatcher => _claim.GetDispatcher();

    /// <summary>
    /// Why the UI thread can't own Avalonia's dispatcher, or <see langword="null"/> when it does. Known once constructed.
    /// </summary>
    internal Exception? ClaimError => (_claim as ClaimFailed)?.Error;

    public SynchronizationContext SynchronizationContext => _synchronizationContext;

    public IAvaloniaContext Context => _context;

    /// <summary>
    /// Gets a value indicating whether the UI thread is the thread that resolved this service (<see cref="HostExtensions.RunAvalonia"/>).
    /// </summary>
    internal bool UsesCurrentThread { get; }

    /// <summary>
    /// Completes once the application is set up, its components are initialized and the main loop runs.
    /// Faults with the original exception if setting up, initializing or starting it fails.
    /// </summary>
    internal Task Started => _started.Task;

    /// <summary>
    /// Completes when the UI thread has ended: the main loop exited, windows were closed, OnExit ran and the components were
    /// disposed. Already completed if the UI thread never runs.
    /// </summary>
    internal Task Completed => _neverRuns ? Task.CompletedTask : _completed.Task;

    /// <summary>
    /// Resolves the thread on the calling thread, which becomes the UI thread (<see cref="HostExtensions.RunAvalonia"/>).
    /// </summary>
    internal static AvaloniaThread ResolveOnCurrentThread(IServiceProvider services)
    {
        AvaloniaThread thread;
        t_useCurrentThread = true;
        try
        {
            thread = services.GetRequiredService<AvaloniaThread>();
        }
        finally
        {
            t_useCurrentThread = false;
        }

        if (!thread.UsesCurrentThread)
        {
            throw new InvalidOperationException(
                "The Avalonia UI thread was already created as a dedicated thread: IAvaloniaThread was resolved before RunAvalonia. " +
                "Call host.RunAvalonia() before resolving IAvaloniaThread or starting the host.");
        }

        return thread;
    }

    /// <summary>
    /// Starts the application on the UI thread. Called by <see cref="AvaloniaHostedService"/> at the right point of the host
    /// lifecycle.
    /// </summary>
    internal void Start()
    {
        if (_neverRuns)
        {
            _started.TrySetException(ClaimError ?? new UnreachableException("A UI thread that never runs has no claim error."));
            return;
        }

        ReleaseGate(run: true);
    }

    /// <summary>
    /// Runs the UI thread on the calling thread, until the application has ended or the host gave up on it
    /// (<see cref="HostExtensions.RunAvalonia"/>).
    /// </summary>
    internal void RunOnCurrentThread()
    {
        if (Thread.CurrentThread != MainThread)
        {
            throw new InvalidOperationException("The Avalonia UI thread must run on the thread that resolved it.");
        }

        RunUiThread(WaitForGate());
    }

    private void ReleaseGate(bool run)
    {
        lock (_stateLock)
        {
            if (_gateReleased)
            {
                return;
            }

            _gateReleased = true;
            _gateRun = run && !_disposed && !_stopRequested;
        }

        _gate.Set();
    }

    private bool WaitForGate()
    {
        _gate.Wait();
        lock (_stateLock)
        {
            return _gateRun;
        }
    }

    /// <inheritdoc />
    public void HandleApplicationExit() => StopHost();

    /// <summary>
    /// Stops the host. Until the host has started, the stop is deferred to then: stopping a host while it starts cancels its
    /// startup, which it reports as a startup failure. Its start ends after the application has started (the rest of
    /// StartedAsync), so not the application's start but the host's ApplicationStarted is waited for. That never comes once
    /// startup has failed, so the stop is then dropped, as it is while the host is being disposed.
    /// </summary>
    private void StopHost()
    {
        if (_disposed || _stopRequested)
        {
            return;
        }

        try
        {
            if (_serviceProvider.GetService<IHostApplicationLifetime>() is { } lifetime)
            {
                // Runs at once if the host has already started.
                lifetime.ApplicationStarted.Register(static state => ((IHostApplicationLifetime)state!).StopApplication(), lifetime);
            }
        }
        catch (ObjectDisposedException)
        {
            // The host is being disposed.
        }
    }

    /// <summary>
    /// Posts to the UI thread through Avalonia's dispatcher. Never throws: work posted after the application has ended is dropped.
    /// </summary>
    private void Post(SendOrPostCallback callback, object? state) => (_claim as Claimed)?.Dispatcher.Post(callback, state);

    /// <summary>
    /// Exits the running application. Used by the host's stop (<see cref="AvaloniaHostedService"/>).
    /// </summary>
    /// <returns><see langword="false"/> when the application wasn't running.</returns>
    internal bool RequestExit()
    {
        bool running;
        lock (_stateLock)
        {
            running = _context.IsRunning;
        }

        if (!running || _claim is not Claimed { Dispatcher: var dispatcher })
        {
            return false;
        }

        if (dispatcher.CheckAccess())
        {
            ExitRunningApplication();
        }
        else
        {
            // At the default priority, so work posted before the stop still runs first (first in, first out).
            dispatcher.Post(ExitRunningApplication);
        }

        return true;
    }

    /// <summary>
    /// Stops the application without the host's usual stop (host startup failed or was cancelled, or the host is being
    /// disposed), or prevents it from running if it hasn't started yet, and waits for the UI thread to end.
    /// </summary>
    internal void StopAndWait()
    {
        lock (_stateLock)
        {
            _stopRequested = true;
        }

        // Never started: the UI thread stops waiting and aborts the work queued for it.
        ReleaseGate(run: false);

        if (RequestExit() && Thread.CurrentThread == MainThread)
        {
            // Called on the UI thread itself: it cannot wait for its own end.
            return;
        }

        // Not when the host's stop already waited the shutdown timeout for it, so the host never waits for it longer than that,
        // nor while the process exits with a UI thread that can't run any more.
        if (Thread.CurrentThread != MainThread && !_exitingProcess && !_stopTimedOut)
        {
            Completed.Wait(_stopTimeout);
        }
    }

    /// <summary>
    /// Called when the host's stop gave up waiting for the UI thread (its shutdown timeout elapsed).
    /// </summary>
    internal void OnStopTimedOut()
    {
        _stopTimedOut = true;
        _logger.AvaloniaStopTimedOut(_stopTimeout);
    }

    /// <summary>
    /// Called by the DI container when the host (its service provider) is disposed; explicit so only the container calls it.
    /// Normally the UI thread has already ended: StoppingAsync waits for it, and <see cref="AvaloniaStartupGuard"/> stops it if
    /// the host never finished starting. This is the last resort, bounded by the shutdown timeout like the rest. It also releases
    /// a UI thread that never started.
    /// </summary>
    void IDisposable.Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAndWait();
    }

    private void ExitRunningApplication()
    {
        // The host is stopping: a window's Closing handler can't veto it, so the forced Shutdown, not TryShutdown. Avalonia sets
        // Environment.ExitCode to the code it is given, so it is given the current one (e.g. from Environment.Exit), not 0.
        if (_context.IsRunning && !_exiting && _lifetime is { } lifetime)
        {
            lifetime.Shutdown(Environment.ExitCode);
        }
    }

    /// <summary>
    /// Registers, once per process and before any host lifetime does (it is called while services are registered), the handler
    /// that keeps the host from waiting for a UI thread that can't run any more while the process exits. ProcessExit handlers run
    /// in registration order, and AvaloniaLifetime's then blocks until the host has stopped.
    /// </summary>
    internal static void RegisterProcessExitHandler()
    {
        if (Interlocked.Exchange(ref s_processExitRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Volatile.Read(ref s_running)?.OnProcessExit();
        }
    }

    private void OnProcessExit()
    {
        // Environment.Exit blocks its calling thread while another thread runs the ProcessExit handlers (and on macOS, quitting
        // the app ends the process from the main thread). If that thread is the UI thread, its loop never runs again: waiting for
        // it to end would only delay the exit by the shutdown timeout. Whether it still runs is only visible from here by asking
        // it. Otherwise the host's usual stop runs, cleanup included. Only while a host could still wait for it.
        if (_disposed || _completed.Task.IsCompleted || _claim is not Claimed { Dispatcher: var dispatcher } || !_context.IsRunning)
        {
            return;
        }

        using var responded = new ManualResetEventSlim(false);
        dispatcher.Post(responded.Set, DispatcherPriority.Send);
        if (responded.Wait(ProcessExitResponseTimeout) || _completed.Task.IsCompleted)
        {
            return;
        }

        _exitingProcess = true;
        _context.IsRunning = false;
        _started.TrySetCanceled();
        _completed.TrySetResult(true);
        try
        {
            _logger.AvaloniaNotRespondingOnProcessExit();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RunUiThread(bool run)
    {
        Volatile.Write(ref s_running, this);
        try
        {
            switch (_claim)
            {
                case ClaimFailed failed:
                    _started.TrySetException(failed.Error);
                    break;
                case Claimed claimed when run:
                    RunApplication(claimed.Dispatcher);
                    break;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref s_running, null, this);

            // Work still queued (or queued for an application that never started) is aborted: InvokeAsync callers get a
            // cancelled task instead of waiting forever, and later posts are rejected.
            ShutDownDispatcher();

            // Startup did not complete (e.g. the application exited before its loop ran): nothing left to wait for.
            _started.TrySetResult(true);
            _completed.TrySetResult(true);
        }
    }

    private void ShutDownDispatcher()
    {
        if (_claim is not Claimed { Dispatcher: var dispatcher } || !dispatcher.CheckAccess())
        {
            return;
        }

#pragma warning disable CA1031 // The UI thread must reach its end; failures are logged.
        try
        {
            dispatcher.InvokeShutdown();
        }
        catch (Exception exception)
        {
            _logger.AvaloniaCleanupFailed(exception);
        }
#pragma warning restore CA1031
    }

    private void RunApplication(Dispatcher dispatcher)
    {
        dispatcher.UnhandledException += OnDispatcherUnhandledException;
        ClassicDesktopStyleApplicationLifetime? lifetime = null;
        // Disposed when the application exits, also if a later component fails to initialize.
        var components = new DisposableList<IAvaloniaComponent>();
#pragma warning disable CA1031 // Not swallowed: rethrown on the host thread by AvaloniaHostedService.StartedAsync via Started, or logged.
        try
        {
            try
            {
                lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    ShutdownMode = _options.ShutdownMode,
                    Args = _options.Args ?? [.. Environment.GetCommandLineArgs().Skip(1)],
                };
                lifetime.Exit += OnLifetimeExit;
                _lifetime = lifetime;
                _components = components;

                var builder = _definition.CreateBuilder(_serviceProvider);
                builder.SetupWithLifetime(lifetime);
                _context.Application = builder.Instance;
                _context.Lifetime = lifetime;

                // The application can exit at any stage (the lifetime's Shutdown from App, OnStartup or a component): its
                // cleanup has run then, so nothing more may be created.
                if (!_exiting)
                {
                    _options.OnStartup?.Invoke(_serviceProvider, _context);
                }

                InitializeComponents(components);
            }
            catch (Exception exception)
            {
                // An exception escaping the UI thread would kill the process without being logged.
                _started.TrySetException(exception);
                return;
            }

            if (_exiting)
            {
                // Exited before its main loop ran, like an application closing its only window during startup: the host
                // starts as usual (Started completes when the UI thread ends) and then stops.
                StopHost();
                return;
            }

            lock (_stateLock)
            {
                // Stopped or disposed while initializing (e.g. startup was cancelled): never run the loop without a host.
                if (_disposed || _stopRequested)
                {
                    _started.TrySetCanceled();
                    return;
                }

                _context.IsRunning = true;
            }

            // The first work the loop runs, before anything posted until now: the application has started.
            dispatcher.Post(OnApplicationStarted, DispatcherPriority.Send);

            var startupFailed = false;
            try
            {
                // Shows MainWindow (if set) and blocks until the application exits.
                lifetime.Start();
            }
            catch (Exception exception) when (!_started.Task.IsCompleted)
            {
                startupFailed = _started.TrySetException(exception);
            }
            finally
            {
                _context.IsRunning = false;
            }

            // The application exited on its own (last window closed, the lifetime's Shutdown): stop the host. When the host is
            // the one stopping, it is already stopping and this does nothing. Not when startup failed: host startup reports that
            // failure itself, and stopping now would cancel it first, hiding the exception behind a cancellation.
            if (!startupFailed)
            {
                StopHost();
            }
        }
        finally
        {
            // The application didn't exit through its lifetime (startup failed or was cancelled): close its windows now. Like
            // WPF's Application.Shutdown, windows still open are closed before the exit callback runs.
            if (lifetime is not null && !_exiting)
            {
                try
                {
                    lifetime.Shutdown(Environment.ExitCode);
                }
                catch (Exception exception)
                {
                    _logger.AvaloniaCleanupFailed(exception);
                }
            }

            Cleanup();
            dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// The lifetime's Exit: its windows are closed and its main loop is about to end. Runs the application's cleanup while the
    /// dispatcher and every service still work. When the Windows session is ending, Avalonia runs this from WM_QUERYENDSESSION:
    /// the process may be ended soon after it returns, so it also stops the host and waits for it (bounded), pumping meanwhile.
    /// </summary>
    private void OnLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _exiting = true;

        // The application can exit while it is still being set up (e.g. the lifetime's Shutdown from the application's
        // OnFrameworkInitializationCompleted), before setup returned it: OnExit still gets it.
        _context.Application ??= global::Avalonia.Application.Current;
        _context.Lifetime ??= _lifetime;

        var sessionEnding = _context.IsRunning && SessionEnd.IsEnding();

        // The main loop is ending: Avalonia raises Exit from inside it. From now on Send is rejected rather than queued.
        _context.IsRunning = false;
        if (sessionEnding)
        {
            _logger.AvaloniaSessionEnding();
            StopHost();
        }

        Cleanup();

        if (sessionEnding)
        {
            _completed.TrySetResult(true);
            WaitForHostStopped();
        }
    }

    private void Cleanup()
    {
        // Once: from the lifetime's Exit, or when the application didn't exit through it.
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
#pragma warning disable CA1031 // Cleanup must reach the end so the host never waits for a UI thread that is gone; failures are logged.
        if (_context.Application is not null)
        {
            try
            {
                _options.OnExit?.Invoke(_serviceProvider, _context);
            }
            catch (Exception exception)
            {
                _logger.AvaloniaCleanupFailed(exception);
            }
        }

        try
        {
            _components?.Dispose();
        }
        catch (AggregateException exception)
        {
            foreach (var inner in exception.InnerExceptions)
            {
                _logger.AvaloniaCleanupFailed(inner);
            }
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Keeps running UI-thread work (other hosted services may still use it while they stop) until the host has stopped, up to
    /// the shutdown timeout.
    /// </summary>
    private void WaitForHostStopped()
    {
        if (_serviceProvider.GetService<IHostApplicationLifetime>() is not { } lifetime || _claim is not Claimed { Dispatcher: var dispatcher })
        {
            return;
        }

        var frame = new DispatcherFrame();
        using var registration = lifetime.ApplicationStopped.Register(() => dispatcher.Post(() => frame.Continue = false, DispatcherPriority.Send));
        using var timeout = DispatcherTimer.RunOnce(() => frame.Continue = false, _stopTimeout, DispatcherPriority.Send);
        if (!lifetime.ApplicationStopped.IsCancellationRequested)
        {
            dispatcher.PushFrame(frame);
        }
    }

    /// <summary>
    /// Runs on the UI thread as the first work of the main loop.
    /// </summary>
    private void OnApplicationStarted()
    {
        try
        {
            // Resolved after everything resolved during startup (the application, OnStartup, components), so the container,
            // which disposes in reverse creation order, disposes it before those services: if the host never finishes starting,
            // it stops the application while they still work.
            _serviceProvider.GetRequiredService<AvaloniaStartupGuard>();
        }
        catch (ObjectDisposedException)
        {
            // The host is already being disposed.
            _started.TrySetCanceled();
            ExitRunningApplication();
            return;
        }

        _started.TrySetResult(true);
    }

    private void InitializeComponents(DisposableList<IAvaloniaComponent> components)
    {
        foreach (var factory in _serviceProvider.GetServices<Func<IAvaloniaComponent>>())
        {
            if (_exiting)
            {
                return;
            }

            var component = factory();
            // Track it before initializing, so it is disposed even if InitializeComponent throws.
            components.Add(component);

            // Its constructor (or factory) exited the application: it was disposed by being tracked after cleanup.
            if (_exiting)
            {
                return;
            }

            component.InitializeComponent();
        }
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UnhandledException?.Invoke(this, e);
        if (!e.Handled)
        {
            // Avalonia rethrows it next, which terminates the process: leave a trace first.
            _logger.AvaloniaUnhandledException(e.Exception);
        }
    }

    /// <summary>
    /// The context handed out as <see cref="IAvaloniaThread.SynchronizationContext"/>: usable before the application runs.
    /// </summary>
    private sealed class HostSynchronizationContext(AvaloniaThread thread) : SynchronizationContext
    {
        public override SynchronizationContext CreateCopy() => this;

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            thread.Post(d, state);
        }

        /// <summary>
        /// Runs inline on the UI thread; otherwise waits until the work ran on it. Before the application runs it throws instead
        /// of blocking: a Send from a hosted service's StartAsync would otherwise deadlock, the UI starting after it.
        /// </summary>
        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            if (Thread.CurrentThread == thread.MainThread)
            {
                d(state);
                return;
            }

            if (!thread.Context.IsRunning || thread._claim is not Claimed { Dispatcher: var dispatcher })
            {
                throw new InvalidOperationException(
                    "Send is only available while the Avalonia application runs. Before it has started use Post, Dispatcher.InvokeAsync " +
                    "or JoinableTaskFactory.SwitchToMainThreadAsync.");
            }

            // InvokeAsync rather than Invoke: Invoke can return as if the work ran when the UI thread aborts it. Only an aborted
            // operation was dropped; a cancellation the callback itself throws is the callback's exception.
            var operation = dispatcher.InvokeAsync(() => d(state), DispatcherPriority.Send);
            try
            {
                operation.GetTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception) when (operation.Status == DispatcherOperationStatus.Aborted)
            {
                throw new InvalidOperationException("The work was dropped: the Avalonia application exited before running it.", exception);
            }
        }
    }
}
