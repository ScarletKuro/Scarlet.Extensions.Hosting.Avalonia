using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Scarlet.Extensions.Hosting.Avalonia.Threading;

/// <summary>
/// Registers JoinableTaskFactory based thread switching with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="JoinableTaskContext"/> whose main thread is the Avalonia UI thread, and its
    /// <see cref="JoinableTaskFactory"/>, so services can <c>await joinableTaskFactory.SwitchToMainThreadAsync()</c> and use
    /// <see cref="AsyncLazy{T}"/> with it. Usable before the application has started: switching then completes once it has.
    /// Requires <c>AddAvalonia</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddThreadSwitching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static provider =>
        {
            var thread = provider.GetRequiredService<IAvaloniaThread>();

            // The UI thread exists before the host starts; its SynchronizationContext queues work until the application runs.
            return new JoinableTaskContext(thread.MainThread, thread.SynchronizationContext);
        });
        services.TryAddSingleton(static provider => provider.GetRequiredService<JoinableTaskContext>().Factory);

        return services;
    }
}
