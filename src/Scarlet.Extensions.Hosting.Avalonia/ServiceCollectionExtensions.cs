using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia.Internal;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// Registers the Avalonia application with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Runs an Avalonia application inside the host. The UI starts after every hosted service has started, and the host stops
    /// when the application exits (and vice versa). The UI thread is a dedicated thread with <c>host.Run()</c> (Windows, Linux),
    /// or the main thread with <see cref="HostExtensions.RunAvalonia"/> (required on macOS).
    /// </summary>
    /// <typeparam name="TApplication">The application (e.g. <c>App</c> from <c>App.axaml</c>), created with constructor injection
    /// on the UI thread.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureAppBuilder">Configures the <see cref="AppBuilder"/> as Avalonia's <c>BuildAvaloniaApp()</c> does,
    /// e.g. <c>builder =&gt; builder.UsePlatformDetect().WithInterFont().LogToTrace()</c>.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">An Avalonia application was already added: there is one per process.</exception>
    public static IServiceCollection AddAvalonia<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApplication>(
        this IServiceCollection services,
        Func<AppBuilder, AppBuilder> configureAppBuilder)
        where TApplication : Application
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureAppBuilder);

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(AvaloniaApplicationDefinition)))
        {
            throw new InvalidOperationException("An Avalonia application was already added: Avalonia runs one application per process.");
        }

        AvaloniaThread.RegisterProcessExitHandler();
        services.AddOptions<AvaloniaOptions>();
        services.AddSingleton(new AvaloniaApplicationDefinition(
            typeof(TApplication),
            provider => configureAppBuilder(AppBuilder.Configure(() => ActivatorUtilities.CreateInstance<TApplication>(provider)))));
        services.TryAddSingleton<AvaloniaContext>();
        services.TryAddSingleton<AvaloniaThread>();
        services.TryAddSingleton<IAvaloniaThread>(static provider => provider.GetRequiredService<AvaloniaThread>());
        services.TryAddSingleton<IAvaloniaContext>(static provider => provider.GetRequiredService<AvaloniaContext>());
        services.TryAddSingleton<AvaloniaStartupGuard>();

        // First, so the UI thread claims Avalonia's UI dispatcher before any other hosted service is even constructed.
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService, AvaloniaHostedService>());

        return services;
    }

    /// <summary>
    /// Runs an Avalonia application inside the host. See <see cref="AddAvalonia{TApplication}(IServiceCollection, Func{AppBuilder, AppBuilder})"/>.
    /// </summary>
    /// <typeparam name="TApplication">The application, created with constructor injection on the UI thread.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureAppBuilder">Configures the <see cref="AppBuilder"/>.</param>
    /// <param name="configureOptions">Configures the <see cref="AvaloniaOptions"/>.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAvalonia<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApplication>(
        this IServiceCollection services,
        Func<AppBuilder, AppBuilder> configureAppBuilder,
        Action<AvaloniaOptions> configureOptions)
        where TApplication : Application
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddAvalonia<TApplication>(configureAppBuilder);
        services.Configure(configureOptions);

        return services;
    }

    /// <summary>
    /// Adds a component created and initialized on the UI thread after the application is set up and before the main loop
    /// starts, and disposed there when the application exits. The component is created with its dependencies from the container
    /// (not resolved as a service, so the container never disposes it). Requires <c>AddAvalonia</c>.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAvaloniaComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TComponent>(this IServiceCollection services)
        where TComponent : class, IAvaloniaComponent
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not AddSingleton<IAvaloniaComponent, TComponent>(): the container would dispose it, on a non-UI thread
        // (https://github.com/dotnet/runtime/issues/36491). The UI thread disposes components itself.
        services.AddSingleton<Func<IAvaloniaComponent>>(static provider => () => ActivatorUtilities.CreateInstance<TComponent>(provider));

        return services;
    }

    /// <summary>
    /// Adds a component created by <paramref name="createComponent"/> on the UI thread after the application is set up and before
    /// the main loop starts, and disposed there when the application exits. Requires <c>AddAvalonia</c>.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="createComponent">Creates the component.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAvaloniaComponent<TComponent>(this IServiceCollection services, Func<IServiceProvider, TComponent> createComponent)
        where TComponent : class, IAvaloniaComponent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(createComponent);

        services.AddSingleton<Func<IAvaloniaComponent>>(provider => () => createComponent(provider));

        return services;
    }

    /// <summary>
    /// Uses <see cref="AvaloniaLifetime"/> as the host lifetime instead of the default ConsoleLifetime. Optional: with any
    /// lifetime, AddAvalonia already stops the host when the Avalonia application exits.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAvaloniaLifetime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHostLifetime, AvaloniaLifetime>();

        return services;
    }

    /// <summary>
    /// Uses <see cref="AvaloniaLifetime"/> as the host lifetime instead of the default ConsoleLifetime.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">Configures the <see cref="AvaloniaLifetimeOptions"/>.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAvaloniaLifetime(this IServiceCollection services, Action<AvaloniaLifetimeOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddAvaloniaLifetime();
        services.Configure(configureOptions);

        return services;
    }
}
