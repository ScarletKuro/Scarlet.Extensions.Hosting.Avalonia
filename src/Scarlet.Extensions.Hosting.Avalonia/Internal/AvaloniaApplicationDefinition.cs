using Avalonia;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// How to build the Avalonia application: the application, created with constructor injection, and the AppBuilder
/// configuration (platform, fonts, logging), as in Avalonia's <c>BuildAvaloniaApp()</c>.
/// </summary>
internal sealed class AvaloniaApplicationDefinition(Func<IServiceProvider, AppBuilder> createBuilder)
{
    /// <summary>
    /// Creates the configured builder. Called on the UI thread, which sets it up.
    /// </summary>
    public AppBuilder CreateBuilder(IServiceProvider services) => createBuilder(services);
}
