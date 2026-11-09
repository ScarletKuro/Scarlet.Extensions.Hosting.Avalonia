using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

internal sealed class AvaloniaContext : IAvaloniaContext
{
    // Written on the UI thread and read from host threads.
    private volatile bool _isRunning;
    private volatile Application? _application;
    private volatile IClassicDesktopStyleApplicationLifetime? _lifetime;

    public bool IsRunning
    {
        get => _isRunning;
        internal set => _isRunning = value;
    }

    public Application? Application
    {
        get => _application;
        internal set => _application = value;
    }

    public IClassicDesktopStyleApplicationLifetime? Lifetime
    {
        get => _lifetime;
        internal set => _lifetime = value;
    }
}
