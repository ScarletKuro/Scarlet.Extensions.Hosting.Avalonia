# Scarlet.Extensions.Hosting.Avalonia: plan

**Status:** all phases are implemented. Locally verified on Windows, with the JIT and as Native AOT, in both thread
models. Linux and macOS are covered by the CI matrix (`.github/workflows/test.yml`) but haven't run yet.

Run an Avalonia desktop application inside the .NET Generic Host, with the same guarantees as
Scarlet.Extensions.Hosting.DirectN and Extensions.Hosting.Wpf. That means DI-created windows, hosted services next to
the UI, an ordered startup and shutdown in both directions, failures that fail the host, and Native AOT.

## Avalonia facts the design rests on (12.1.5, read in source)

| Fact | Source | Consequence |
| --- | --- | --- |
| `AppBuilder` setup runs once per process (`s_setupWasAlreadyCalled`) | `AppBuilder.cs` | One Avalonia app per process, like WPF; a host can't be restarted in-process. Tests need a process per scenario |
| The UI thread is whichever thread creates the first dispatcher (`s_uiThread ??= this`) | `Dispatcher.cs` | A dedicated UI thread is possible |
| `Dispatcher.UIThread` before setup creates a dispatcher on the **calling** thread and makes it the UI thread | `Dispatcher.ThreadStorage.cs` | A hosted service touching `Dispatcher.UIThread` early would hijack the UI thread. The host must claim it on its own thread first |
| `ClassicDesktopStyleApplicationLifetime.Start()` exists "for integrating with lifetime managers such as HostApplicationBuilder"; it shows `MainWindow` and runs `Dispatcher.UIThread.MainLoop` | `ClassicDesktopStyleApplicationLifetime.cs` | Reuse Avalonia's lifetime (`ShutdownMode`, `Windows`, `Exit`) instead of writing one |
| `Shutdown`/`TryShutdown` raise `ShutdownRequested` (cancellable), close windows, raise `Exit`, then `InvokeShutdown` | same | Host stop maps to `TryShutdown` or `Shutdown`; window close maps to host stop |
| OS shutdown arrives as `ShutdownRequested` with `IsOSShutdown` (platform lifetime events) | same | Session end handling has a hook; bound it like DirectN |
| Dispatcher shutdown aborts every queued operation, and later posts are rejected | `Dispatcher.MainLoop.cs`, verified with a 100k+ invocation flood | Work queued at exit faults instead of hanging |
| macOS requires the UI on the process's main thread | Cocoa | A dedicated UI thread works on Windows and Linux only. macOS needs a main-thread mode |

## Packages

No dependency on DirectN or any Scarlet DirectN package (`Scarlet.DirectN.Threading`, `Scarlet.Extensions.Hosting.DirectN*`).
Only Avalonia and `Microsoft.Extensions.Hosting`.

- **`Scarlet.Extensions.Hosting.Avalonia`** (core, `net10.0`, Windows, Linux and macOS, AOT-compatible): the host
  integration.
- **`Scarlet.Extensions.Hosting.Avalonia.Threading`** (`net10.0`, AOT-compatible): `AddThreadSwitching`, a
  `JoinableTaskContext` whose main thread is Avalonia's UI thread, and its `JoinableTaskFactory`. It carries its own copy
  of the trimmed, AOT-compatible Microsoft.VisualStudio.Threading extract (`JoinableTaskFactory`, `AsyncLazy<T>`,
  `await TaskScheduler.Default`). The copy comes from Hosting.DirectN.Threading, with the DirectN-specific parts
  replaced by Avalonia's thread and synchronization context, and its origin and changes listed in `UPSTREAM.md`. It's a
  copy rather than a shared package so neither repo depends on the other. A neutral shared package remains possible
  later.
- No tray package and no tray bootstrap. Avalonia's `TrayIcon` works on Windows and macOS, and on Linux desktops
  with StatusNotifierItem support. It's registered on the `Application` (`App.axaml` or `TrayIcon.SetIcons`) and disposed
  by Avalonia at dispatcher shutdown. A component creates it with DI on the UI thread, for example with an Exit item
  calling `IAvaloniaThread.HandleApplicationExit()`. A tray-only app uses `ShutdownMode.OnExplicitShutdown`. Covered by a
  sample.
- No Win32Aot integration package. An app that wants Win32Aot's `BlazorWebView` inside Avalonia writes the small
  dispatcher adapter itself, as in the Win32Aot repo's Avalonia example.

## Public API (sketch, mirrors Hosting.DirectN)

```csharp
builder.Services.AddAvalonia<App>(
    configureApp: app => app.UsePlatformDetect().WithInterFont(),   // AppBuilder configuration
    configureOptions: o =>
    {
        o.ShutdownMode = ShutdownMode.OnLastWindowClose;
        o.OnStartup = (services, context) => { };
        o.OnExit = (services, context) => { };
    });
builder.Services.AddAvaloniaComponent<MainWindowComponent>();      // created with DI on the UI thread
```

- `App` (the user's `Application`, e.g. `App.axaml`) is created with constructor injection.
- `IAvaloniaThread`: `MainThread`, `Dispatcher` (Avalonia's), `SynchronizationContext` (usable before the UI thread
  runs), `HandleApplicationExit()`, `UnhandledException`.
- `IAvaloniaContext`: `IsRunning`, `Application`, `Lifetime` (the classic desktop lifetime).
- `IAvaloniaComponent` / `AddAvaloniaComponent<T>`: UI objects (main window, tray icon) created on the UI thread, then
  disposed there. The first window a component shows becomes `MainWindow` unless set explicitly.
- `AvaloniaLifetime` (`IHostLifetime`), like `DirectNLifetime`, only if parity is wanted. `ConsoleLifetime` already works.

## Lifecycle (the contract, same as Hosting.DirectN)

1. **Claim the UI thread early.** Resolving `IAvaloniaThread` creates the UI thread, which creates Avalonia's UI
   dispatcher right away and then waits. No hosted service can hijack it, even by touching `Dispatcher.UIThread`.
2. **Hosted services start first.** Work posted to the UI thread before it runs is kept and runs once it does, in the
   poster's `ExecutionContext`. Awaiting it in `StartAsync` would deadlock, as in DirectN, and is documented.
3. **Then the UI starts.** On the UI thread: Avalonia setup, the `App` from DI, `OnStartup`, components, `MainWindow`,
   main loop. A failure here fails host startup. "Started" is signalled once the loop runs.
4. **The app exits, so the host stops.** Last window closed, `Shutdown()` or `HandleApplicationExit()` stops the host.
5. **The host stops, so the app exits.** `StopAsync` asks the lifetime to shut down on the UI thread, then waits for
   the UI thread to end (bounded by the host's shutdown timeout) before services are disposed. `OnExit` and component
   disposal run on the UI thread while services still work.
6. **Session end.** OS shutdown (`IsOSShutdown`) gives cleanup and hosted services a bounded chance to finish.
7. **Unhandled UI exceptions.** `Dispatcher.UIThread.UnhandledException` surfaces as `IAvaloniaThread.UnhandledException`
   (set `Handled` to continue); unhandled ones are logged as critical and terminate the process, like WPF.

## Decided in phase 1

- **Host stop forces the exit.** When the host stops, it calls the lifetime's `Shutdown()` (forced), not
  `TryShutdown()`: a window's `Closing` handler can't veto a host that is already stopping. This matches DirectN, where an
  `ApplicationExiting` handler can't veto it either. The app's own exits (the user closing the last window,
  `TryShutdown()` from app code) stay Avalonia's and can be cancelled.
- **Work queued before startup uses Avalonia's own queue.** Before setup, the claimed dispatcher runs on Avalonia's
  managed implementation and keeps its queue. When setup replaces the implementation, it re-signals pending work
  (`Dispatcher.ReplaceImplementation`), so the work runs once the loop runs, in the poster's `ExecutionContext` (Avalonia
  captures it). If the app never starts, the UI thread calls `InvokeShutdown`, which aborts everything queued, so
  `InvokeAsync` callers get a cancelled task instead of hanging. Unlike DirectN, no buffer of our own is needed; a
  scenario checks this.
- **Session end on Windows.** Avalonia raises `ShutdownRequested` from `WM_QUERYENDSESSION` and runs the whole shutdown
  inside it. The host detects that the session is ending with `GetSystemMetrics(SM_SHUTTINGDOWN)`, which also covers
  logoff, where `IsOSShutdown` is false. It then does DirectN's bounded wait for the host to stop inside the lifetime's
  `Exit`, pumping a nested dispatcher frame so other services can still use the UI thread meanwhile. On macOS and
  Linux, exit goes through the process-exit path.

## Thread model: both modes in 1.0

- **A. Dedicated UI thread** (Windows, Linux). Same model as DirectN and WPF: `await host.RunAsync()` in `Main`.
- **B. Main-thread mode** (required on macOS, also usable on Windows and Linux). `Main` runs the UI and the host runs in
  the background: `host.RunAvalonia()` blocks on the main thread until the app exits and the host has stopped.

The core is built around a small "UI thread runner" with these two implementations, so both modes share the lifecycle
code and the contract above holds in both. The difference is only who owns the thread:
- In A, the host creates and claims the UI thread when `IAvaloniaThread` is resolved.
- In B, `RunAvalonia()` claims the calling thread before it starts the host, so the same "no hijacking" guarantee
  holds.

Calling `host.RunAsync()` on macOS, which would start mode A there, fails fast with a message pointing to
`RunAvalonia()`.

## Testing (same approach as Hosting.DirectN)

- A **ScenarioApp** run in a separate process per scenario (setup is once per process). Scenarios:
  - startup order;
  - startup failure;
  - exit in both directions;
  - posting before the UI thread starts;
  - `Dispatcher.UIThread` touched early from a hosted service, directly or indirectly (`TrayIcon`'s static constructor
    subscribes to `Dispatcher.UIThread.ShutdownStarted`, so touching the type is enough);
  - component lifecycle and disposal order;
  - unhandled exceptions;
  - a shutdown flood (no pending work);
  - session end on Windows.
- Every scenario runs in both thread modes.
- Thread switching gets the ported upstream tests, as Hosting.DirectN.Threading did, plus scenarios with the host
  (switching before the UI thread runs, during shutdown).
- xunit.v3 on Microsoft.Testing.Platform, run with the JIT. The ScenarioApp is also published as Native AOT and the
  scenarios rerun against it.
- CI on Windows, Linux (Xvfb) and macOS runners, since all three are claimed. Session-end scenarios are per platform.
- Every fix gets a test that's shown to fail without it.

## Repository

Same conventions as Hosting.DirectN: `.slnx`, `Directory.Build.props`, explicit csproj settings, no SDK pin in
`global.json`, `CHANGELOG.md` (Keep a Changelog), tag-based release workflow, README plus wiki, samples (see phase 5).
No sample references Win32Aot either, since it depends on `Scarlet.DirectN.Threading`.

## Phases

1. Repo skeleton. Core lifecycle with the UI thread runner and mode A. `IAvaloniaThread` and `IAvaloniaContext`.
   Scenario tests for startup order, startup failure, exit in both directions and early posting. CI on Windows.
2. Mode B (`RunAvalonia()`); every scenario in both modes; CI on Linux and macOS.
3. Components, options, unhandled exceptions, session end; the remaining scenarios; Native AOT CI.
4. `Scarlet.Extensions.Hosting.Avalonia.Threading`: the copied extract adapted to Avalonia, the ported tests, host
   scenarios.
5. Samples (minimal, DI windows, tray-only app, thread switching, macOS main-thread mode); docs and wiki; 1.0.0.

## Decisions

1. Platforms for 1.0: Windows, Linux and macOS. Both thread modes ship in 1.0.
2. No DirectN dependency of any kind; no Win32Aot integration package.
3. `JoinableTaskFactory` thread switching ships in 1.0, as its own package with its own copy of the extract.
