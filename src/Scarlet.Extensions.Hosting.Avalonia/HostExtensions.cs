using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia.Internal;

namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// Runs a host whose Avalonia UI runs on the calling thread.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// Runs the host with the Avalonia UI on the calling thread (the main thread, which Avalonia requires on macOS) and the
    /// host in the background. Blocks until the application has exited and the host has stopped, then disposes the host,
    /// like <c>host.Run()</c>. The lifecycle is the same as with <c>host.Run()</c>: the UI starts after every hosted service
    /// has started, and the host stops when the application exits (and vice versa).
    /// </summary>
    /// <param name="host">The <see cref="IHost"/> to run. It must not be started yet, nor its <see cref="IAvaloniaThread"/>
    /// resolved.</param>
    /// <exception cref="InvalidOperationException">On Windows, the calling thread is not an STA thread (mark <c>Main</c> with
    /// <c>[STAThread]</c>); or the UI thread was already created as a dedicated thread.</exception>
    public static void RunAvalonia(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("On Windows, RunAvalonia must be called on an STA thread: mark Main with [STAThread].");
        }

        // The calling thread becomes the UI thread and claims Avalonia's UI dispatcher before anything else runs.
        var thread = AvaloniaThread.ResolveOnCurrentThread(host.Services);

        // Started from the thread pool: the UI waits for the host to start it, or to give up on it (startup failure, disposal).
        var run = Task.Run(() => host.RunAsync());
        try
        {
            thread.RunOnCurrentThread();
        }
        catch
        {
            // An exception left unhandled on the UI thread propagates from here, as from WPF's Application.Run: it terminates
            // the process unless Main catches it. Stop the host first, and wait until it has stopped and been disposed, so a
            // caller's own disposal doesn't race with hosted services still stopping. The UI exception is the one reported.
            thread.HandleApplicationExit();
            try
            {
                run.GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // The UI exception is rethrown instead.
            catch
#pragma warning restore CA1031
            {
            }

            throw;
        }

        run.GetAwaiter().GetResult();
    }
}
