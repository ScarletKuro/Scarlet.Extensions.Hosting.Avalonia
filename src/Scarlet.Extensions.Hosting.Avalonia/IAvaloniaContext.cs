using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// State of the Avalonia application run by the host.
/// </summary>
public interface IAvaloniaContext
{
    /// <summary>
    /// Gets a value indicating whether the Avalonia main loop is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the application, created with constructor injection on the UI thread, or <see langword="null"/> before it exists.
    /// </summary>
    Application? Application { get; }

    /// <summary>
    /// Gets the application's lifetime (windows, <c>MainWindow</c>, <c>ShutdownMode</c>, events), or <see langword="null"/>
    /// before it exists. The host owns it: stop the host (<see cref="IAvaloniaThread.HandleApplicationExit"/>) rather than
    /// calling its <c>Shutdown</c>, although that also stops the host.
    /// </summary>
    IClassicDesktopStyleApplicationLifetime? Lifetime { get; }
}
