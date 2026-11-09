using Avalonia;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// How to build the Avalonia application: its type, created with constructor injection, and the AppBuilder configuration
/// (platform, fonts, logging), as in Avalonia's <c>BuildAvaloniaApp()</c>.
/// </summary>
internal sealed class AvaloniaApplicationDefinition(Type applicationType, Func<IServiceProvider, AppBuilder> createBuilder)
{
    public Type ApplicationType { get; } = applicationType;

    /// <summary>
    /// Creates the configured builder. Called on the UI thread, which sets it up.
    /// </summary>
    public AppBuilder CreateBuilder(IServiceProvider services) => createBuilder(services);
}
