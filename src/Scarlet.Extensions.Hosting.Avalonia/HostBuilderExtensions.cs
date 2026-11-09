using Microsoft.Extensions.Hosting;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// <see cref="IHostBuilder"/> shortcuts. With <c>Host.CreateApplicationBuilder</c> use the
/// <see cref="ServiceCollectionExtensions"/> on <c>builder.Services</c> instead.
/// </summary>
public static class HostBuilderExtensions
{
    /// <summary>
    /// Uses <see cref="AvaloniaLifetime"/> as the host lifetime instead of the default ConsoleLifetime.
    /// </summary>
    /// <param name="hostBuilder">The <see cref="IHostBuilder" /> to configure.</param>
    /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
    public static IHostBuilder UseAvaloniaLifetime(this IHostBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        return hostBuilder.ConfigureServices(static (_, services) => services.AddAvaloniaLifetime());
    }

    /// <summary>
    /// Uses <see cref="AvaloniaLifetime"/> as the host lifetime instead of the default ConsoleLifetime.
    /// </summary>
    /// <param name="hostBuilder">The <see cref="IHostBuilder" /> to configure.</param>
    /// <param name="configureOptions">Configures the <see cref="AvaloniaLifetimeOptions"/>.</param>
    /// <returns>The same instance of the <see cref="IHostBuilder"/> for chaining.</returns>
    public static IHostBuilder UseAvaloniaLifetime(this IHostBuilder hostBuilder, Action<AvaloniaLifetimeOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return hostBuilder.ConfigureServices((_, services) => services.AddAvaloniaLifetime(configureOptions));
    }
}
