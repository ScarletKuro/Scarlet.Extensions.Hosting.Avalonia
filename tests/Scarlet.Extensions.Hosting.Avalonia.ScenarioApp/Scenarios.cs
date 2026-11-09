using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;

namespace ScenarioApp;

internal static unsafe partial class Scenarios
{
    private static Task? s_sendDuringExit;
    private static System.Diagnostics.Stopwatch? s_stopRequested;
    private static Task? s_abandonedInvocation;
    private static readonly ConcurrentQueue<Task> s_flood = new();
    private static int s_floodRan;
    private static volatile bool s_stopFlood;
    private static readonly TaskCompletionSource s_queryEndSessionReported = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static bool TryConfigure(string scenario, IServiceCollection services)
    {
        switch (scenario)
        {
            case "stop":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                OnStarted(services, sp =>
                {
                    var thread = sp.GetRequiredService<IAvaloniaThread>();
                    Program.Report("Started", thread.Context.IsRunning ? "running" : "not-running");
                    thread.SynchronizationContext.Post(_ =>
                    {
                        Program.Report("Posted", Program.Where());
                        sp.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    }, null);
                });
                return true;

            case "window-close":
                AddAvalonia(services, ShutdownMode.OnLastWindowClose);
                services.AddAvaloniaComponent<WindowComponent>();
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().Dispatcher.Post(() =>
                {
                    Program.Report("ClosingWindow", Program.Where());
                    WindowComponent.Window!.Close();
                }));
                return true;

            case "app-main-window":
                AddAvalonia(services, ShutdownMode.OnMainWindowClose);
                services.AddSingleton(new MainWindowTitle("titled by DI"));
                return true;

            case "handle-exit":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                OnStarted(services, sp =>
                {
                    Program.Report("HandleApplicationExit", Program.Where());
                    sp.GetRequiredService<IAvaloniaThread>().HandleApplicationExit();
                });
                return true;

            case "startup-failure":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                services.AddAvaloniaComponent<FailingComponent>();
                return true;

            case "prestart-post":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                services.AddHostedService<PrestartPostService>();
                // Stopping from the posted work itself would race with the host still finishing its startup.
                OnStarted(services, sp => sp.GetRequiredService<IHostApplicationLifetime>().StopApplication());
                return true;

            case "prestart-abandoned":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddHostedService(sp => new AbandonedInvocationService(sp.GetRequiredService<IAvaloniaThread>()));
                services.AddHostedService<FailingStartService>();
                return true;

            case "unhandled-handled":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                OnStarted(services, sp =>
                {
                    var thread = sp.GetRequiredService<IAvaloniaThread>();
                    thread.UnhandledException += (_, e) =>
                    {
                        Program.Report("UnhandledException", $"{Program.Where()}:{e.Exception.Message}");
                        e.Handled = true;
                    };
                    thread.SynchronizationContext.Post(_ => throw new InvalidOperationException("posted failure"), null);
                    thread.SynchronizationContext.Post(_ =>
                    {
                        Program.Report("StillRunning", Program.Where());
                        sp.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    }, null);
                });
                return true;

            case "unhandled-crash":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext.Post(_ =>
                {
                    Program.Report("Throwing", Program.Where());
                    throw new InvalidOperationException("unhandled posted failure");
                }, null));
                return true;

            case "send":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    // A Send that can no longer run (the application is exiting) must be released, not hang.
                    options.OnExit = (sp, _) =>
                    {
                        var context = sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext;
                        s_sendDuringExit = Task.Run(() => SendAndReport(context, "SendDuringExit"));
                        Thread.Sleep(300);
                    };
                });
                services.AddHostedService(sp => new SendBeforeStartService(sp.GetRequiredService<IAvaloniaThread>()));
                OnStarted(services, sp =>
                {
                    var thread = sp.GetRequiredService<IAvaloniaThread>();
                    SendAndReport(thread.SynchronizationContext, "SendWhileRunning");
                    try
                    {
                        thread.SynchronizationContext.Send(_ => throw new FormatException("sent failure"), null);
                    }
                    catch (FormatException ex)
                    {
                        Program.Report("SendException", ex.Message);
                    }

                    // A cancellation the callback throws is its exception, not the work being dropped.
                    try
                    {
                        thread.SynchronizationContext.Send(_ => throw new OperationCanceledException("callback cancelled"), null);
                    }
                    catch (OperationCanceledException ex)
                    {
                        Program.Report("SendCallbackCancellation", ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Program.Report("SendCallbackCancellation", $"misreported: {ex.Message}");
                    }

                    // On the UI thread itself, Send runs inline.
                    thread.SynchronizationContext.Post(_ => SendAndReport(thread.SynchronizationContext, "SendOnUiThread"), null);
                    thread.SynchronizationContext.Post(_ => sp.GetRequiredService<IHostApplicationLifetime>().StopApplication(), null);
                });
                return true;

            case "exit-cancelled":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnExit = (_, _) => Program.Report("OnExit", Program.Where());
                });
                // A window vetoing every close: the host stopping must still end the application.
                services.AddAvaloniaComponent(_ => new WindowComponent(vetoClose: true));
                OnStarted(services, sp => sp.GetRequiredService<IHostApplicationLifetime>().StopApplication());
                return true;

            case "canceled-during-init":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<SlowComponent>();
                services.AddHostedService(sp => new ContextPostService(sp.GetRequiredService<IAvaloniaThread>()));
                return true;

            case "cleanup-failures":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnExit = (_, _) =>
                    {
                        Program.Report("OnExit", Program.Where());
                        throw new InvalidOperationException("exit failure");
                    };
                });
                services.AddAvaloniaComponent<ProbeComponent>();
                services.AddAvaloniaComponent<ThrowingDisposeComponent>();
                OnStarted(services, sp => sp.GetRequiredService<IHostApplicationLifetime>().StopApplication());
                return true;

            case "unhandled-handler-throws":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                OnStarted(services, sp =>
                {
                    var thread = sp.GetRequiredService<IAvaloniaThread>();
                    thread.UnhandledException += (_, _) => throw new FormatException("handler failure");
                    thread.SynchronizationContext.Post(_ =>
                    {
                        Program.Report("Throwing", Program.Where());
                        throw new InvalidOperationException("posted failure");
                    }, null);
                });
                return true;

            case "shutdown-timeout":
                services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    // Cleanup that outlives the shutdown timeout: the host must give up waiting, not hang.
                    options.OnExit = (_, _) =>
                    {
                        Program.Report("OnExitStarted", Program.Where());
                        Thread.Sleep(TimeSpan.FromSeconds(4));
                        Program.Report("OnExitFinished", Program.Where());
                    };
                });
                OnStarted(services, sp =>
                {
                    s_stopRequested = System.Diagnostics.Stopwatch.StartNew();
                    sp.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                });
                return true;

            case "execution-context":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddHostedService(sp => new ContextPostService(sp.GetRequiredService<IAvaloniaThread>()));
                OnStarted(services, sp =>
                {
                    var context = sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext;
                    ContextPostService.Request.Value = "running-caller";
                    context.Post(_ =>
                    {
                        Program.Report("RunningPost", ContextPostService.Request.Value);
                        ContextPostService.Request.Value = "left-by-running";
                    }, null);
                    ContextPostService.Request.Value = null;
                    context.Post(_ =>
                    {
                        Program.Report("LaterPost", ContextPostService.Request.Value);
                        sp.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    }, null);
                });
                return true;

            case "started-failure":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddSingleton<GuardedDependency>();
                services.AddAvaloniaComponent<DependentComponent>();
                services.AddHostedService<FailingStartedService>();
                return true;

            case "post-after-exit":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().HandleApplicationExit());
                return true;

            case "await-after-exit":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddHostedService<SlowStopService>();
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext.Post(_ => _ = SlowStopService.AwaitStopOnUiThread(sp), null));
                return true;

            case "exit-before-started":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnStartup = (sp, _) =>
                    {
                        // Runs in the loop before the application has been reported as started.
                        var thread = sp.GetRequiredService<IAvaloniaThread>();
                        thread.SynchronizationContext.Post(_ =>
                        {
                            Program.Report("HandleApplicationExit", Program.Where());
                            thread.HandleApplicationExit();
                        }, null);
                    };
                    options.OnExit = (_, _) => Program.Report("OnExit", Program.Where());
                });
                return true;

            case "ui-execution-context":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnStartup = (sp, _) =>
                    {
                        Program.Report("OnStartup", Program.HostStartValue.Value);
                        var thread = sp.GetRequiredService<IAvaloniaThread>();
                        using (ExecutionContext.SuppressFlow())
                        {
                            thread.SynchronizationContext.Post(_ =>
                            {
                                Program.Report("FlowSuppressedPost", Program.HostStartValue.Value);
                                thread.HandleApplicationExit();
                            }, null);
                        }
                    };
                });
                return true;

            case "environment-exit":
                services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddHostedService<SlowStopService>();
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext.Post(_ =>
                {
                    Program.Report("EnvironmentExit", Program.Where());
                    Environment.Exit(5);
                }, null));
                return true;

            case "session-end":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                services.AddHostedService<SlowStopService>();
                OnStarted(services, sp =>
                {
                    var thread = sp.GetRequiredService<IAvaloniaThread>();
                    uint uiThreadId = 0;
                    thread.SynchronizationContext.Send(_ => uiThreadId = GetCurrentThreadId(), null);
                    var window = FindAvaloniaMessageWindow(uiThreadId);

                    // The check Windows answers with GetSystemMetrics(SM_SHUTTINGDOWN) while the session really ends.
                    Scarlet.Extensions.Hosting.Avalonia.Internal.SessionEnd.IsEndingForTests = static () => true;

                    // What Windows sends when the session ends. Avalonia runs the application's whole shutdown inside it.
                    const uint WM_QUERYENDSESSION = 0x0011;
                    var allowed = SendMessageW(window, WM_QUERYENDSESSION, 0, 0);
                    Program.Report("QueryEndSession", $"{allowed}:{(SlowStopService.Stopped ? "host-stopped" : "host-still-running")}");
                    s_queryEndSessionReported.TrySetResult();
                });
                return true;

            case "hijack-in-constructor":
                // Registered before AddAvalonia, and touching Avalonia's UI thread in its constructor, directly and through
                // TrayIcon's static constructor: the host still claims the UI thread first.
                services.AddHostedService<UiThreadTouchingService>();
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnStartup = (_, _) => Program.Report("OnStartup", $"{Program.Where()}:{(Dispatcher.UIThread.CheckAccess() ? "owns-dispatcher" : "foreign-dispatcher")}");
                });
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().HandleApplicationExit());
                return true;

            case "hijack-before-start":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                return true;

            case "shutdown-flood":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                OnStarted(services, sp =>
                {
                    var dispatcher = sp.GetRequiredService<IAvaloniaThread>().Dispatcher;
                    new Thread(() =>
                    {
                        // A heavy producer, not a tight loop: Avalonia aborts queued work one operation at a time when it shuts down,
                        // so a producer that never pauses could keep its queue from ever being empty.
                        while (!s_stopFlood)
                        {
                            for (var i = 0; i < 100; i++)
                            {
                                s_flood.Enqueue(dispatcher.InvokeAsync(() => { Interlocked.Increment(ref s_floodRan); }).GetTask());
                            }

                            Thread.Sleep(1);
                        }
                    })
                    { IsBackground = true }.Start();
                    Thread.Sleep(200);
                    sp.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                });
                return true;

            case "shutdown-in-startup":
                services.AddAvalonia<ScenarioApplication>(Configure, options =>
                {
                    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    options.OnStartup = (_, context) =>
                    {
                        Program.Report("OnStartup", Program.Where());
                        context.Lifetime!.Shutdown();
                    };
                    options.OnExit = (_, _) => Program.Report("OnExit", Program.Where());
                });
                services.AddAvaloniaComponent<NeverCreatedComponent>();
                return true;

            case "shutdown-in-component":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddAvaloniaComponent<ProbeComponent>();
                services.AddAvaloniaComponent<ShuttingDownComponent>();
                services.AddAvaloniaComponent<NeverCreatedComponent>();
                return true;

            case "unhandled-crash-slow-stop":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                services.AddHostedService<SlowStopService>();
                OnStarted(services, sp => sp.GetRequiredService<IAvaloniaThread>().SynchronizationContext.Post(_ =>
                {
                    Program.Report("Throwing", Program.Where());
                    throw new InvalidOperationException("unhandled posted failure");
                }, null));
                return true;

            case "threading":
                ThreadingScenario.Configure(services);
                return true;

            case "macos-dedicated-thread":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                return true;

            case "run-after-resolve":
                AddAvalonia(services, ShutdownMode.OnExplicitShutdown);
                return true;

            default:
                return false;
        }
    }

    public static void BeforeRun(string scenario, IHost host)
    {
        if (scenario == "hijack-before-start")
        {
            // Some code uses Avalonia's dispatcher from another thread before the host claimed it.
            Task.Run(() => Dispatcher.UIThread.CheckAccess()).GetAwaiter().GetResult();
        }
    }

    public static void AfterRun(string scenario)
    {
        var thread = Program.Thread!;
        if (scenario == "post-after-exit")
        {
            thread.SynchronizationContext.Post(_ => Program.Report("RanAfterExit"), null);
            thread.Dispatcher.Post(() => Program.Report("RanAfterExit"));
            Program.Report("PostAfterExit", "no-throw");
        }

        if (scenario == "session-end")
        {
            // Reported by the thread that sent the query, which may still be returning from it.
            s_queryEndSessionReported.Task.Wait(TimeSpan.FromSeconds(10));
        }

        if (scenario == "shutdown-timeout")
        {
            // ShutdownTimeout (1 s) once: disposing the host doesn't wait for the UI thread again after StoppingAsync gave up.
            Program.Report("StoppedWithinTimeout", s_stopRequested!.Elapsed < TimeSpan.FromSeconds(2.5) ? "yes" : $"no ({s_stopRequested.Elapsed})");
        }

        if (scenario == "send")
        {
            Program.Report("SendDuringExitFinished", s_sendDuringExit!.Wait(TimeSpan.FromSeconds(10)) ? "yes" : "hung");
            SendAndReport(thread.SynchronizationContext, "SendAfterExit");
        }

        if (scenario == "shutdown-flood")
        {
            // Keep invoking after the application ended for a moment, then account for every invocation.
            Thread.Sleep(200);
            s_stopFlood = true;
            Thread.Sleep(100);
            var all = s_flood.ToArray();
            SpinWait.SpinUntil(() => all.All(t => t.IsCompleted), TimeSpan.FromSeconds(10));
            var ran = all.Count(t => t.Status == TaskStatus.RanToCompletion);
            var cancelled = all.Count(t => t.IsCanceled);
            var pending = all.Count(t => !t.IsCompleted);
            Program.Report("FloodPending", pending.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Program.Report("FloodAccounted", ran == Volatile.Read(ref s_floodRan) && ran + cancelled == all.Length && cancelled > 0 ? "yes" : $"no ({all.Length} invocations: {ran} ran of {s_floodRan}, {cancelled} cancelled)");
        }
    }

    public static void AfterFailure(string scenario)
    {
        if (scenario == "prestart-abandoned")
        {
            // Queued before the host failed to start: must end cancelled, not stay pending forever.
            var invocation = s_abandonedInvocation!;
            SpinWait.SpinUntil(() => invocation.IsCompleted, TimeSpan.FromSeconds(5));
            Program.Report("BufferedInvocation", invocation.IsCanceled ? "cancelled" : invocation.Status.ToString());
        }
    }

    internal static void SendAndReport(SynchronizationContext context, string name)
    {
        try
        {
            context.Send(_ => Program.Report($"{name}Ran", Program.Where()), null);
        }
        catch (InvalidOperationException)
        {
            Program.Report($"{name}Rejected");
        }
    }

    internal static void SetAbandonedInvocation(Task task) => s_abandonedInvocation = task;

    internal static Window CreateMainWindow(IServiceProvider services)
    {
        var window = new Window { Title = services.GetRequiredService<MainWindowTitle>().Value, Width = 240, Height = 120 };
        window.Opened += (_, _) =>
        {
            Program.Report("MainWindowShown", $"{Program.Where()}:{window.Title}");
            Dispatcher.UIThread.Post(() =>
            {
                Program.Report("ClosingMainWindow", Program.Where());
                window.Close();
            });
        };
        return window;
    }

    internal static AppBuilder Configure(AppBuilder builder) => builder.UsePlatformDetect();

    private static void AddAvalonia(IServiceCollection services, ShutdownMode shutdownMode) =>
        services.AddAvalonia<ScenarioApplication>(Configure, options =>
        {
            options.ShutdownMode = shutdownMode;
            options.OnStartup = (_, context) => Program.Report("OnStartup", $"{Program.Where()}:{(context.Application is ScenarioApplication ? "app" : "no-app")}");
            options.OnExit = (_, _) => Program.Report("OnExit", Program.Where());
        });

    private static void OnStarted(IServiceCollection services, Action<IServiceProvider> action) =>
        services.AddHostedService(sp => new OnStartedService(sp.GetRequiredService<IHostApplicationLifetime>(), sp, action));

    private static nint FindAvaloniaMessageWindow(uint uiThreadId)
    {
        s_messageWindow = 0;
        EnumThreadWindows(uiThreadId, &FindMessageWindow, 0);
        return s_messageWindow != 0 ? s_messageWindow : throw new InvalidOperationException("No Avalonia message window.");
    }

    private static nint s_messageWindow;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FindMessageWindow(nint hwnd, nint lParam)
    {
        var className = stackalloc char[128];
        var length = GetClassNameW(hwnd, className, 128);
        if (new ReadOnlySpan<char>(className, length).StartsWith("AvaloniaMessageWindow "))
        {
            s_messageWindow = hwnd;
            return 0;
        }

        return 1;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial int EnumThreadWindows(uint threadId, delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetClassNameW(nint hwnd, char* className, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}

internal sealed record MainWindowTitle(string Value);
