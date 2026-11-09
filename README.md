# Scarlet.Extensions.Hosting.Avalonia
[![Nuget](https://img.shields.io/nuget/v/Scarlet.Extensions.Hosting.Avalonia?color=ff4081&logo=nuget)](https://www.nuget.org/packages/Scarlet.Extensions.Hosting.Avalonia/)
[![Nuget](https://img.shields.io/nuget/dt/Scarlet.Extensions.Hosting.Avalonia?color=ff4081&label=nuget%20downloads&logo=nuget)](https://www.nuget.org/packages/Scarlet.Extensions.Hosting.Avalonia/)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia?color=594ae2&logo=github)](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/blob/master/LICENSE)
[![codecov](https://codecov.io/github/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/graph/badge.svg?token=YDLXFV5A6I)](https://codecov.io/github/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia)

Unofficial implementation of Microsoft.Extensions.Hosting for [Avalonia](https://avaloniaui.net/) desktop applications on Windows, Linux and macOS. It runs the Avalonia UI inside the Generic Host, next to your hosted services, with an ordered startup and shutdown in both directions, and is **Native AOT compatible**: every package is `IsAotCompatible`, and CI publishes the scenario app, the tests and the samples with `PublishAot` on all three platforms.
It follows the design of [Scarlet.Extensions.Hosting.DirectN](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.DirectN) and [Extensions.Hosting.Wpf](https://github.com/ScarletKuro/Microsoft.Extensions.Hosting.Wpf), without depending on them. An extension package provides thread switching between the UI thread and the thread pool (`JoinableTaskFactory`, `AsyncLazy<T>`).

### [Changelog](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/blob/master/CHANGELOG.md) | [Wiki](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki)

## NuGet Packages

| Name | Description | Latest version |
| --- | --- | --- |
| **Scarlet.Extensions.Hosting.Avalonia** | The main library, with **Microsoft.Extensions.Hosting** for Avalonia support. | [![NuGet](https://img.shields.io/nuget/vpre/Scarlet.Extensions.Hosting.Avalonia.svg)](https://www.nuget.org/packages/Scarlet.Extensions.Hosting.Avalonia/) |
| **Scarlet.Extensions.Hosting.Avalonia.Threading** | An extension enabling thread switching between the UI thread and ThreadPool threads via an AOT-compatible extract of **Microsoft.VisualStudio.Threading** (`JoinableTaskFactory`, `AsyncLazy<T>`). | [![NuGet](https://img.shields.io/nuget/vpre/Scarlet.Extensions.Hosting.Avalonia.Threading.svg)](https://www.nuget.org/packages/Scarlet.Extensions.Hosting.Avalonia.Threading/) |

There is no tray icon package: Avalonia's own `TrayIcon` works inside the host.

## Quick Start

### 1. Create `Program.cs`

Use `Host.CreateApplicationBuilder` from `Microsoft.Extensions.Hosting`. Everything is registered on `builder.Services`
before calling `Build()`, so the same calls work with any `IHostApplicationBuilder` and inside
`IHostBuilder.ConfigureServices`.

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
        builder.Services.AddAvaloniaLifetime();
        builder.Services.AddAvaloniaComponent<MainWindowComponent>();

        using var host = builder.Build();
        host.RunAvalonia();
    }

    // Keep for the XAML previewer, which still builds the app this way (it needs App's parameterless constructor).
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
```

`AddAvalonia<App>` takes your `Application` (e.g. `App.axaml`), created with **constructor injection** on the UI thread,
and the same `AppBuilder` configuration as Avalonia's `BuildAvaloniaApp()`.

`host.RunAvalonia()` runs the UI on the calling thread and the host in the background; it works on every platform and is
required on macOS, where Avalonia must run on the main thread. `host.Run()` works too on Windows and Linux, with the UI on a
dedicated thread the host creates.

### 2. Create the UI in a component

Components are created with their dependencies from the container and initialized on the UI thread in registration order,
after the application is set up and before the main loop starts. If they implement `IDisposable`, they're disposed on the
UI thread, in reverse order, when the application exits. The container never disposes them.

```csharp
using Scarlet.Extensions.Hosting.Avalonia;

public sealed class MainWindowComponent(IAvaloniaContext context, MainWindowViewModel viewModel) : IAvaloniaComponent
{
    public void InitializeComponent() =>
        context.Lifetime!.MainWindow = new MainWindow { DataContext = viewModel };
}
```

Setting the lifetime's `MainWindow` in `App.OnFrameworkInitializationCompleted` works too, since the application itself is
created with dependency injection.

### 3. Update `.csproj`

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Avalonia.Desktop" />
  <PackageReference Include="Avalonia.Themes.Fluent" />
  <PackageReference Include="Scarlet.Extensions.Hosting.Avalonia" />
</ItemGroup>
```

For the full guide including **components**, **application options**, **thread models**, **thread switching**, **tray icons**, and more, see the **[Wiki](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki)**.

## Samples
1. [HostingMinimal](samples/HostingMinimal): A main window created by a component, and a `BackgroundService` updating it through **Threading** (`JoinableTaskFactory`).
2. [HostingTrayIcon](samples/HostingTrayIcon): A windowless tray application (`ShutdownMode.OnExplicitShutdown`) using Avalonia's `TrayIcon`, with Exit stopping the host.

Both publish with `PublishAot` on Windows, Linux and macOS.

## Documentation

Full documentation is available in the **[Wiki](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki)**:

- [Getting Started](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/3.-Getting-started-%F0%9F%9A%80) — Minimal setup guide
- [Components](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/4.-Components-%F0%9F%A7%A9) — UI-thread objects with constructor injection
- [Application Options](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/5.-Application-options-%E2%9A%99%EF%B8%8F) — Shutdown mode, startup and exit, unhandled exceptions
- [Avalonia Lifetime](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/6.-Avalonia-Lifetime-%E2%8F%B1%EF%B8%8F) — The host lifetime for windowed applications
- [Thread Models](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/7.-Thread-models-%F0%9F%A7%B5) — `host.RunAvalonia()` on the main thread vs `host.Run()` on a dedicated thread
- [Threading](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/8.-Threading-extension-%F0%9F%94%80) — Switch between UI and background threads
- [Tray Icons](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/9.-Tray-icons-%F0%9F%94%94) — Avalonia's `TrayIcon` inside the host
- [Architecture & How It Works](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/10.-Architecture-&-How-it-works-%F0%9F%93%90) — Internal architecture and lifecycle
- [API Reference](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/11.-API-Reference-%F0%9F%93%9A) — Complete API reference
- [FAQ & Troubleshooting](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/13.-FAQ-&-Troubleshooting-%E2%9D%93) — Common questions and error solutions
- [Migrating from Extensions.Hosting.Wpf and DirectN](https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/wiki/14.-Migrating-from-Extensions.Hosting.Wpf-and-DirectN-%F0%9F%94%84) — API mapping and a before/after example

## Building and testing

```
dotnet build Scarlet.Extensions.Hosting.Avalonia.slnx -c Release
dotnet test --solution Scarlet.Extensions.Hosting.Avalonia.slnx -c Release
```

The scenario tests run a real host and Avalonia application in a separate process per scenario (the ScenarioApp), with the
UI on a dedicated thread and on the main thread. On Linux they need a display (`xvfb-run`). CI also publishes the
ScenarioApp with Native AOT and reruns every scenario against it (`SCARLET_SCENARIO_APP`).

## License

MIT. `Scarlet.Extensions.Hosting.Avalonia.Threading` contains code from
[microsoft/vs-threading](https://github.com/microsoft/vs-threading) (MIT); see [LICENSE-3RD-PARTY.txt](LICENSE-3RD-PARTY.txt)
and [UPSTREAM.md](UPSTREAM.md).
