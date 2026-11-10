using Avalonia.Threading;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// Whether the host got Avalonia's UI dispatcher for its UI thread: <see cref="Claimed"/> with the dispatcher, or
/// <see cref="ClaimFailed"/> with the reason. One or the other, never neither or both.
/// </summary>
internal abstract class DispatcherClaim
{
    private protected DispatcherClaim()
    {
    }

    /// <summary>
    /// For tests: runs where the dispatcher is taken, before Avalonia creates it; throwing fails the claim.
    /// </summary>
    internal static Action? BeforeClaimForTests { get; set; }

    /// <summary>
    /// Gets the dispatcher, or throws why it couldn't be claimed.
    /// </summary>
    public abstract Dispatcher GetDispatcher();

    /// <summary>
    /// Takes Avalonia's UI dispatcher for the calling thread. Avalonia's UI thread is the thread on which a dispatcher is first
    /// created, and reading <see cref="Dispatcher.UIThread"/> creates one on the reading thread. Never throws: a failure is
    /// returned, so startup reports it instead of the process crashing on the UI thread.
    /// </summary>
    public static DispatcherClaim ForCurrentThread()
    {
        Dispatcher dispatcher;
#pragma warning disable CA1031 // Not swallowed: startup fails with it.
        try
        {
            BeforeClaimForTests?.Invoke();
            dispatcher = Dispatcher.UIThread;
        }
        catch (Exception exception)
        {
            // The root cause: Avalonia's own failures arrive wrapped, e.g. in a TypeInitializationException.
            return new ClaimFailed(new InvalidOperationException($"Avalonia's UI dispatcher couldn't be created: {exception.GetBaseException().Message}", exception));
        }
#pragma warning restore CA1031

        if (dispatcher.CheckAccess())
        {
            return new Claimed(dispatcher);
        }

        return new ClaimFailed(new InvalidOperationException(
            "Avalonia's UI thread was already taken by another thread: Dispatcher.UIThread (or a type that uses it, such as TrayIcon) " +
            "was used before the host started. Avalonia makes the first thread that uses it its UI thread. Don't use it before the " +
            "host has started, or run the UI on the main thread with host.RunAvalonia()."));
    }
}

/// <summary>
/// The host's UI thread owns Avalonia's UI dispatcher.
/// </summary>
internal sealed class Claimed(Dispatcher dispatcher) : DispatcherClaim
{
    public Dispatcher Dispatcher { get; } = dispatcher;

    public override Dispatcher GetDispatcher() => Dispatcher;
}

/// <summary>
/// The host's UI thread can't own Avalonia's UI dispatcher; host startup fails with <see cref="Error"/>.
/// </summary>
internal sealed class ClaimFailed(Exception error) : DispatcherClaim
{
    public Exception Error { get; } = error;

    public override Dispatcher GetDispatcher() => throw new InvalidOperationException(Error.Message, Error);
}
