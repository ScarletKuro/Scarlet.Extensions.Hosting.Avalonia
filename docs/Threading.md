# Scarlet.Extensions.Hosting.Avalonia.Threading

`JoinableTaskFactory`-based thread switching for
[Scarlet.Extensions.Hosting.Avalonia](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia).

For a single UI operation, the main package's `IAvaloniaThread.Dispatcher` (Avalonia's UI dispatcher) is enough:

```csharp
ui.Dispatcher.Post(() => viewModel.Status = "Updated");
await ui.Dispatcher.InvokeAsync(() => viewModel.Status = "Updated");
```

Use `JoinableTaskFactory` when you need to switch the current method to the UI thread or coordinate synchronous waits:

```csharp
builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect());
builder.Services.AddThreadSwitching();

public sealed class WindowService(JoinableTaskFactory jtf)
{
    public async Task ShowAsync()
    {
        await jtf.SwitchToMainThreadAsync();   // now on the Avalonia UI thread
        // ... create or update windows ...
        await TaskScheduler.Default;           // back to the thread pool
    }
}
```

`AddThreadSwitching()` registers a `JoinableTaskContext` whose main thread is the Avalonia UI thread, and its
`JoinableTaskFactory`. It works before the application has started: `SwitchToMainThreadAsync()` then completes once it has.
Don't wait for that inside a hosted service's `StartAsync` (awaiting the switch, or `JoinableTaskFactory.Run`): the UI only
starts after every `StartAsync` has returned, so host startup would hang. Use `BackgroundService.ExecuteAsync` or
`IHostApplicationLifetime.ApplicationStarted` instead. It works with both thread models (`host.Run()` and
`host.RunAvalonia()`).

## Contents

A trimmed, Native AOT compatible extract of
[Microsoft.VisualStudio.Threading](https://github.com/microsoft/vs-threading) `v17.12.19` (MIT, not affiliated with Microsoft).
Type and member names are unchanged, so code moves over by replacing `using Microsoft.VisualStudio.Threading;` with
`using Scarlet.Extensions.Hosting.Avalonia.Threading;`.

Public types: `JoinableTaskContext`, `JoinableTaskContextNode`, `JoinableTaskFactory`, `JoinableTask`, `JoinableTask<T>`,
`JoinableTaskCollection`, `JoinableTaskCreationOptions`, `JoinableTaskContextException`, `AsyncLazy<T>`, and
`AwaitExtensions` (`await TaskScheduler.Default`, `SwitchTo`).

```csharp
var settings = new AsyncLazy<Settings>(LoadSettingsAsync, jtf);
var value = await settings.GetValueAsync();
```

## Differences from vs-threading

- Not included: `AsyncReaderWriterLock`, `AsyncSemaphore`, `ReentrantSemaphore`, `AsyncManualResetEvent` (internal), the
  other async primitives, WPF `Dispatcher` support, and the hang *report* (`GetHangReport`; hang *detection*
  `JoinableTaskContext.OnHangDetected` is kept). See
  [UPSTREAM.md](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/blob/main/UPSTREAM.md) for the full list.
- `AwaitExtensions` has the same name and members as upstream's: in a project that also references
  `Microsoft.VisualStudio.Threading`, `await TaskScheduler.Default` is ambiguous when both namespaces are imported. Import
  only one of them in such files.
- The VSTHRD analyzers (`Microsoft.VisualStudio.Threading.Analyzers`) recognize the `Microsoft.VisualStudio.Threading`
  namespace only, so they don't analyze these types.
- `JoinableTaskFactory.Run` on the UI thread doesn't pump the Avalonia dispatcher while it waits (as upstream): work that
  needs the UI thread still runs through the factory's own queue, so it doesn't deadlock, but the UI doesn't update until it
  returns.
