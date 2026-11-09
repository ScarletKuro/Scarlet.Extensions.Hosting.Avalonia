using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Scarlet.Extensions.Hosting.Avalonia.Internal;

/// <summary>
/// Whether the Windows session is ending (shutdown, restart or logoff). Avalonia raises its shutdown request from
/// WM_QUERYENDSESSION but only marks shutdown and restart as an OS shutdown (<c>IsOSShutdown</c>), not logoff.
/// </summary>
internal static partial class SessionEnd
{
    private const int SM_SHUTTINGDOWN = 0x2000;

    /// <summary>
    /// For tests: replaces the check (WM_QUERYENDSESSION can be sent to a test app, but the session doesn't really end).
    /// </summary>
    internal static Func<bool>? IsEndingForTests { get; set; }

    public static bool IsEnding()
    {
        if (IsEndingForTests is { } isEnding)
        {
            return isEnding();
        }

        return OperatingSystem.IsWindows() && IsEndingOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEndingOnWindows() => GetSystemMetrics(SM_SHUTTINGDOWN) != 0;

    [LibraryImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int GetSystemMetrics(int index);
}
