using Avalonia.Controls;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// Options for the Avalonia application run by the host (<c>AddAvalonia</c>).
/// </summary>
public sealed class AvaloniaOptions
{
    /// <summary>
    /// Gets or sets when the Avalonia application exits on its own (which then stops the host). Default:
    /// <see cref="ShutdownMode.OnLastWindowClose"/>. Use <see cref="ShutdownMode.OnMainWindowClose"/> to exit when the
    /// lifetime's <c>MainWindow</c> closes, and <see cref="ShutdownMode.OnExplicitShutdown"/> for tray applications that run
    /// without windows.
    /// </summary>
    public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnLastWindowClose;

    /// <summary>
    /// Gets or sets the command-line arguments passed to the Avalonia lifetime (its <c>Startup</c> event and <c>Args</c>).
    /// Default: <see langword="null"/>, the process's arguments without the executable.
    /// </summary>
#pragma warning disable CA1819 // Mirrors IClassicDesktopStyleApplicationLifetime.Args.
    public string[]? Args { get; set; }
#pragma warning restore CA1819

    /// <summary>
    /// Gets or sets a callback run on the UI thread after the Avalonia application is set up (its
    /// <c>OnFrameworkInitializationCompleted</c> has run) and before the components are initialized and the main loop starts.
    /// Exceptions fail host startup.
    /// </summary>
    public Action<IServiceProvider, IAvaloniaContext>? OnStartup { get; set; }

    /// <summary>
    /// Gets or sets a callback run on the UI thread when the application exits, after its windows were closed and before
    /// the components are disposed. Services still work.
    /// </summary>
    public Action<IServiceProvider, IAvaloniaContext>? OnExit { get; set; }
}
