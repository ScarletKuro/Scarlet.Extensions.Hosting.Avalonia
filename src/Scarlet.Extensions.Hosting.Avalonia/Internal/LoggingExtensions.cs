using Microsoft.Extensions.Logging;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

internal static partial class LoggingExtensions
{
    [LoggerMessage(0, LogLevel.Information, "Starting Avalonia application")]
    internal static partial void AvaloniaStarting(this ILogger logger);

    [LoggerMessage(1, LogLevel.Information, "Avalonia UI thread started")]
    internal static partial void AvaloniaThreadStarted(this ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Stopping Avalonia application due to host shutdown")]
    internal static partial void AvaloniaStopping(this ILogger logger);

    [LoggerMessage(3, LogLevel.Information, "Avalonia application started")]
    internal static partial void AvaloniaApplicationStarted(this ILogger logger);

    [LoggerMessage(4, LogLevel.Information, "Avalonia application is shutting down...")]
    internal static partial void AvaloniaApplicationShuttingDown(this ILogger logger);

    [LoggerMessage(5, LogLevel.Information, "Avalonia application was stopped")]
    internal static partial void AvaloniaApplicationStopped(this ILogger logger);

    [LoggerMessage(6, LogLevel.Information, "Waiting for the host to be disposed, please ensure all 'IHost' instances are wrapped in 'using' blocks")]
    internal static partial void WaitingHost(this ILogger logger);

    [LoggerMessage(7, LogLevel.Information, "Lifetime: {lifetime}")]
    internal static partial void LifeTime(this ILogger logger, string lifetime);

    [LoggerMessage(8, LogLevel.Information, "Hosting environment: {envName}")]
    internal static partial void HostingEnvironment(this ILogger logger, string envName);

    [LoggerMessage(9, LogLevel.Information, "Content root path: {contentRoot}")]
    internal static partial void ContentRootPath(this ILogger logger, string contentRoot);

    [LoggerMessage(10, LogLevel.Critical, "Unhandled exception on the Avalonia UI thread")]
    internal static partial void AvaloniaUnhandledException(this ILogger logger, Exception exception);

    [LoggerMessage(11, LogLevel.Error, "Exception while exiting the Avalonia application")]
    internal static partial void AvaloniaCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(12, LogLevel.Warning, "The process is exiting and the Avalonia UI thread doesn't respond (e.g. Environment.Exit was called on it): the host stops without waiting for it, so the application's cleanup (OnExit, components) may not run")]
    internal static partial void AvaloniaNotRespondingOnProcessExit(this ILogger logger);

    [LoggerMessage(13, LogLevel.Warning, "The Avalonia UI thread didn't end within the host's shutdown timeout ({timeout}); the host stops without waiting for it")]
    internal static partial void AvaloniaStopTimedOut(this ILogger logger, TimeSpan timeout);

    [LoggerMessage(14, LogLevel.Information, "The Windows session is ending: stopping the Avalonia application and the host")]
    internal static partial void AvaloniaSessionEnding(this ILogger logger);
}
