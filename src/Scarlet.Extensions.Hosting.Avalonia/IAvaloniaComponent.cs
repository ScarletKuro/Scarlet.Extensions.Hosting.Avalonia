namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// A component living on the Avalonia UI thread, e.g. a main window or a tray icon.
/// </summary>
/// <remarks>
/// Components are registered with <c>AddAvaloniaComponent</c>, created with constructor injection and initialized on the UI
/// thread in registration order, after the application is set up and before the main loop starts. If they implement
/// <see cref="IDisposable"/>, they are disposed on the UI thread in reverse order when the application exits. The DI
/// container never disposes them.
/// </remarks>
public interface IAvaloniaComponent
{
    /// <summary>
    /// Initializes the component, e.g. creates and shows a window or sets the lifetime's <c>MainWindow</c>. Called on the UI
    /// thread before the main loop starts. Exceptions fail host startup.
    /// </summary>
    void InitializeComponent();
}
