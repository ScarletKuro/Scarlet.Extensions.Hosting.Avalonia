# Scarlet.Extensions.Hosting.Avalonia

Microsoft.Extensions.Hosting (Generic Host) for [Avalonia](https://avaloniaui.net/) desktop applications on Windows, Linux
and macOS. It runs the Avalonia UI inside the host, next to your hosted services, with an ordered startup and shutdown in
both directions, and is **Native AOT compatible**: every package is `IsAotCompatible`, and CI publishes the scenario app,
the tests and the samples with `PublishAot` on all three platforms.

It follows the design of [Scarlet.Extensions.Hosting.DirectN](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.DirectN)
and [Extensions.Hosting.Wpf](https://github.com/ScarletKuro/Microsoft.Extensions.Hosting.Wpf), without depending on them.

### [Changelog](CHANGELOG.md)

## NuGet packages

| Name | Description |
| --- | --- |
| **Scarlet.Extensions.Hosting.Avalonia** | The main library: runs an Avalonia application inside the Generic Host. |
| **Scarlet.Extensions.Hosting.Avalonia.Threading** | Thread switching between the UI thread and thread pool threads, via an AOT-compatible extract of **Microsoft.VisualStudio.Threading** (`JoinableTaskFactory`, `AsyncLazy<T>`). |

## Quick start

```csharp
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect().LogToTrace());
        builder.Services.AddAvaloniaComponent<MainWindowComponent>();

        using var host = builder.Build();
        host.RunAvalonia();
    }

    // Keep for the XAML previewer, which still builds the app this way.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
```

`AddAvalonia<App>` takes your `Application` (e.g. `App.axaml`), created with **constructor injection** on the UI thread,
and the same `AppBuilder` configuration as Avalonia's `BuildAvaloniaApp()`. Windows and other UI objects are created in
components:

```csharp
public sealed class MainWindowComponent(IAvaloniaContext context, MainWindowViewModel viewModel) : IAvaloniaComponent
{
    public void InitializeComponent() =>
        context.Lifetime!.MainWindow = new MainWindow { DataContext = viewModel };
}
```

Components are created with their dependencies from the container and initialized on the UI thread in registration order,
after the application is set up (its `OnFrameworkInitializationCompleted` has run) and before the main loop starts. If they
implement `IDisposable`, they're disposed on the UI thread, in reverse order, when the application exits. The container never
disposes them. Setting the lifetime's `MainWindow` in `App.OnFrameworkInitializationCompleted` works too, since the
application itself is created with dependency injection.

## Two thread models

| | `host.RunAvalonia()` | `host.Run()` |
| --- | --- | --- |
| UI thread | The calling thread (`Main`) | A dedicated thread the host creates |
| Host | Runs in the background | Runs on the calling thread |
| Platforms | Windows, Linux, macOS | Windows, Linux (macOS fails startup, pointing to `RunAvalonia`) |

Both share the same lifecycle. `RunAvalonia()` is the portable choice: macOS requires Avalonia on the process's main thread.
On Windows, `Main` must be `[STAThread]` for `RunAvalonia()`. The dedicated thread of `host.Run()` is already STA and
doesn't inherit the `ExecutionContext` (`AsyncLocal` values, logging scopes) of whoever started the host.

## Lifecycle

- **The UI starts after every hosted service has started**, the way ASP.NET Core starts serving requests. If any hosted
  service fails to start, the UI is never created. Failures while creating it (the application, `OnStartup`, a component)
  fail host startup instead of crashing the process.
- **The application exiting stops the host** (last window closed, per `ShutdownMode`), and **the host stopping exits the
  application**. The host's stop can't be vetoed by a window's `Closing` handler. Cleanup (`OnExit`, component disposal)
  runs on the UI thread while every service still works, before any hosted service's `StopAsync`.
- **Work queued for the UI before it runs** (through `IAvaloniaThread.Dispatcher`, `SynchronizationContext` or
  `JoinableTaskFactory`) runs once the main loop runs, in the caller's `ExecutionContext`. Startup code that pumps a
  nested dispatcher frame (e.g. `Dispatcher.PushFrame` in a component) runs it earlier, before `IsRunning` is true:
  Avalonia's queue holds it, and a nested frame drains that queue. Work still queued when the
  application exits, or for an application that never starts, is aborted: `InvokeAsync` callers get a cancelled task instead
  of waiting forever.
- **When the Windows session ends** (logoff, shutdown, restart), the application exits and the host stops, bounded by the
  host's shutdown timeout, before Windows may end the process.
- **Unhandled exceptions** on the UI thread are reported through `IAvaloniaThread.UnhandledException` (Avalonia's
  `Dispatcher.UnhandledException`). Set `Handled` to keep running; otherwise they're logged as critical and terminate the
  process, like WPF.

Options cover what an `App` class would otherwise do:

```csharp
builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect(), options =>
{
    options.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // tray apps; default OnLastWindowClose
    options.OnStartup = (services, context) => { /* UI thread, before the main loop */ };
    options.OnExit = (services, context) => { /* UI thread, when the application exits */ };
});
```

## Using the UI thread from services

`IAvaloniaThread` gives services the UI thread:

```csharp
public sealed class StatusService(IAvaloniaThread ui, MainWindowViewModel viewModel)
{
    public Task ShowAsync(string text) => ui.Dispatcher.InvokeAsync(() => viewModel.Status = text).GetTask();
}
```

`Dispatcher` is Avalonia's UI dispatcher, which the host claims for its UI thread before any hosted service starts.
`SynchronizationContext` posts to it and never throws, while its `Send` throws until the application runs, rather than
deadlocking. `HandleApplicationExit()` stops the host from any thread, e.g. from a tray icon's Exit item.

Work can be queued at any time, but don't *wait* for it inside a hosted service's `StartAsync`: the UI only starts after
every `StartAsync` has returned, so startup would hang. Use `BackgroundService.ExecuteAsync` or
`IHostApplicationLifetime.ApplicationStarted`.

**Don't use `Dispatcher.UIThread` before the host starts**, e.g. in `Main` or from another thread before `Run`/`RunAvalonia`.
Avalonia makes the first thread that uses it its UI thread. Hosted service constructors are fine, since the host claims the UI
thread first. If something took it, startup fails with "Avalonia's UI thread was already taken by another thread". Some
Avalonia types use it in their static constructor, e.g. `TrayIcon`.

For `await`-style switching (`await jtf.SwitchToMainThreadAsync()`) and `AsyncLazy<T>`, add
**Scarlet.Extensions.Hosting.Avalonia.Threading** and call `builder.Services.AddThreadSwitching()`; see
[docs/Threading.md](docs/Threading.md).

## Tray icons

Avalonia's own `TrayIcon` covers Windows, macOS, and Linux desktops with StatusNotifierItem support, so there is no tray
package. Create it in a component and register it on the application; Avalonia disposes it when the application exits:

```csharp
public sealed class TrayIconComponent(IAvaloniaThread ui, IAvaloniaContext context) : IAvaloniaComponent
{
    public void InitializeComponent()
    {
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ui.HandleApplicationExit();
        TrayIcon.SetIcons(context.Application!, [new TrayIcon { Icon = LoadIcon(), Menu = new NativeMenu { Items = { exit } } }]);
    }
}
```

Use `ShutdownMode.OnExplicitShutdown` for an application that runs without windows. See `samples/HostingTrayIcon`.

## Host lifetime

`builder.Services.AddAvaloniaLifetime()` replaces the default `ConsoleLifetime` for windowed applications: no Ctrl+C
handling or console hints, status messages logged instead, and a graceful host shutdown when the process exits. It's
optional: with any lifetime, the host stops when the application exits.

## Things to know

- **One Avalonia application per process**, as Avalonia sets up once per process. Calling `AddAvalonia` twice throws.
- **With `RunAvalonia()`, an unhandled UI exception** propagates out of `RunAvalonia()`, like WPF's `Application.Run`, and
  the host is asked to stop. In a Native AOT build it can't cross the platform's native window procedure and fails fast
  instead. Either way the process ends unless `Main` catches it.
- **With `RunAvalonia()`, cleanup that outlives the host's shutdown timeout** delays its return: the host stops on time, but
  `RunAvalonia()` runs on the UI thread itself and returns once `OnExit` and the components are done.
- **A producer that never pauses** (invoking on the UI thread in a tight loop) can keep Avalonia's dispatcher from finishing
  its shutdown, which aborts queued work one operation at a time.

## Samples

- `samples/HostingMinimal`: a main window from a component, updated by a `BackgroundService` through `JoinableTaskFactory`.
- `samples/HostingTrayIcon`: a tray-only application with Avalonia's `TrayIcon`.

## Building and testing

```
dotnet build Scarlet.Extensions.Hosting.Avalonia.slnx -c Release
dotnet test --solution Scarlet.Extensions.Hosting.Avalonia.slnx -c Release
```

The scenario tests run a real host and Avalonia application in a separate process per scenario (the ScenarioApp), with the
UI on a dedicated thread and on the main thread. On Linux they need a display (`xvfb-run`). CI also publishes the
ScenarioApp with Native AOT and reruns every scenario against it (`SCARLET_SCENARIO_APP`).
