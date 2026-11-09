# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-11-09

First release: Microsoft.Extensions.Hosting for Avalonia desktop applications on Windows, Linux and macOS, Native AOT
compatible (`net10.0`, Avalonia 12).

### Added
- `Scarlet.Extensions.Hosting.Avalonia`: `AddAvalonia<TApplication>` (the application created with constructor injection,
  the `AppBuilder` configured as in `BuildAvaloniaApp()`; `ShutdownMode`, `Args`, `OnStartup`, `OnExit`),
  `IAvaloniaComponent` / `AddAvaloniaComponent`, `IAvaloniaThread` / `IAvaloniaContext`, `AvaloniaLifetime` /
  `AddAvaloniaLifetime` / `UseAvaloniaLifetime`. Two thread models with one lifecycle: `host.RunAvalonia()` runs the UI on
  the main thread (required on macOS) and the host in the background; `host.Run()` runs it on a dedicated thread (Windows,
  Linux). The UI starts after every hosted service has started and stops before them, startup failures fail host startup,
  cleanup runs on the UI thread while every service still works, and the host claims Avalonia's UI thread before any hosted
  service is constructed. Work queued before the application runs runs once it does; work dropped at exit is aborted rather
  than left pending. The end of the Windows session stops the application and the host before Windows may end the process.
- `Scarlet.Extensions.Hosting.Avalonia.Threading`: `AddThreadSwitching`, with a trimmed, Native AOT compatible extract of
  Microsoft.VisualStudio.Threading 17.12.19 (`JoinableTaskFactory`, `AsyncLazy<T>`, `await TaskScheduler.Default`).

[Unreleased]: https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/compare/1.0.0...HEAD
[1.0.0]: https://github.com/ScarletKuro/Scarlet.Extensions.Hosting.Avalonia/releases/tag/1.0.0
