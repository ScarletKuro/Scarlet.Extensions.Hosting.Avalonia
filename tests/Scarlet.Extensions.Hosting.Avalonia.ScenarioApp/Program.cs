using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scarlet.Extensions.Hosting.Avalonia;

namespace ScenarioApp;

/// <summary>
/// Runs one hosting scenario (args[0]) and reports what happened as <c>EVT|name|value</c> lines on stdout. With
/// <c>--main-thread</c> the UI runs on the main thread (<c>host.RunAvalonia()</c>), otherwise on a dedicated thread (<c>host.Run()</c>).
/// Exit codes: 0 host stopped normally, 2 unknown scenario, 3 host failed to start, 4 timeout; anything else is a crash.
/// </summary>
internal static class Program
{
    public const int UnknownScenarioExitCode = 2;
    public const int HostFailedExitCode = 3;
    public const int TimeoutExitCode = 4;

    private static readonly Lock ReportLock = new();
    private static volatile System.Threading.Thread? s_uiThread;

    public static AsyncLocal<string?> HostStartValue { get; } = new();

    public static string Scenario { get; private set; } = string.Empty;

    public static bool MainThreadMode { get; private set; }

    /// <summary>
    /// The UI thread, captured by the application when the UI thread creates it.
    /// </summary>
    public static IAvaloniaThread? Thread { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Scenario = args.Length > 0 ? args[0] : string.Empty;
        MainThreadMode = args.Contains("--main-thread");
        StartWatchdog(TimeSpan.FromSeconds(45));

        var builder = Host.CreateApplicationBuilder([.. args.Skip(1).Where(a => a != "--main-thread")]);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.AddAvaloniaLifetime();

        if (!Scenarios.TryConfigure(Scenario, builder.Services))
        {
            Report("UnknownScenario", Scenario);
            return UnknownScenarioExitCode;
        }

        using var host = builder.Build();
        var context = host.Services.GetRequiredService<IAvaloniaContext>();
        Scenarios.BeforeRun(Scenario, host);

        if (Scenario == "canceled-during-init")
        {
            // Startup is cancelled while a component is still initializing: the main loop must never run.
            var thread = host.Services.GetRequiredService<IAvaloniaThread>();
            s_uiThread = thread.MainThread;
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                host.StartAsync(cancel.Token).GetAwaiter().GetResult();
                Report("Started");
            }
            catch (OperationCanceledException)
            {
                Report("StartCanceled", context.IsRunning ? "running" : "not-running");
            }

            return 0;
        }

        if (Scenario == "run-after-resolve")
        {
            // A dedicated UI thread already exists: RunAvalonia can't make the main thread the UI thread any more.
            host.Services.GetRequiredService<IAvaloniaThread>();
            try
            {
                host.RunAvalonia();
                Report("RanAvalonia");
            }
            catch (InvalidOperationException exception)
            {
                Report("RunAvaloniaRejected", exception.Message.StartsWith("The Avalonia UI thread was already created", StringComparison.Ordinal) ? "dedicated-thread" : exception.Message);
            }

            return 0;
        }

        // Set by whoever starts the host: must not leak into the UI thread.
        HostStartValue.Value = "host-start";

        try
        {
            if (MainThreadMode)
            {
                s_uiThread = System.Threading.Thread.CurrentThread;
                host.RunAvalonia();
            }
            else
            {
                // Not resolving IAvaloniaThread here, as a real app does: the host must claim the UI thread on its own. The
                // application reports the UI thread when the UI thread creates it.
                host.Run();
            }
        }
        catch (Exception exception)
        {
            var message = exception.Message.Split(':')[0];
            Report("HostFailed", $"{exception.GetType().Name}: {(message.StartsWith("Avalonia's UI thread was already taken", StringComparison.Ordinal) || message.StartsWith("On macOS", StringComparison.Ordinal) ? message : exception.Message)}");
            Scenarios.AfterFailure(Scenario);
            return HostFailedExitCode;
        }

        Report("HostStopped", context.IsRunning ? "running" : "not-running");
        Scenarios.AfterRun(Scenario);
        return 0;
    }

    /// <summary>
    /// "ui" on the Avalonia UI thread, "other" elsewhere.
    /// </summary>
    public static void SetUiThread(System.Threading.Thread thread) => s_uiThread ??= thread;

    public static string Where() => System.Threading.Thread.CurrentThread == s_uiThread ? "ui" : "other";

    public static void Report(string name, string? value = null)
    {
        lock (ReportLock)
        {
            Console.Out.WriteLine($"EVT|{name}|{value}");
            Console.Out.Flush();
        }
    }

    private static void StartWatchdog(TimeSpan timeout)
    {
        var watchdog = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(timeout);
            Report("Timeout");
            Environment.Exit(TimeoutExitCode);
        })
        {
            IsBackground = true,
            Name = "Watchdog",
        };
        watchdog.Start();
    }
}

/// <summary>
/// The application, created with constructor injection on the UI thread.
/// </summary>
internal sealed class ScenarioApplication : Application
{
    private readonly IAvaloniaThread _thread;
    private readonly IServiceProvider _services;

    public ScenarioApplication(IAvaloniaThread thread, IServiceProvider services)
    {
        _thread = thread;
        _services = services;
        Program.SetUiThread(thread.MainThread);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Program.Thread = _thread;
        if (Program.Scenario == "app-main-window" && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Scenarios.CreateMainWindow(_services);
            Program.Report("MainWindowSet", Program.Where());
        }

        if (Program.Scenario == "shutdown-in-app-init" && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            // Exits while Avalonia is still setting the application up.
            Program.Report("AppInitShutdown", Program.Where());
            lifetime.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
