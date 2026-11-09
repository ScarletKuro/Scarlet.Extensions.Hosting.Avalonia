using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;

namespace HostingTrayIcon;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // A tray application runs without windows: it only exits when asked to.
        builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect().LogToTrace(), options => options.ShutdownMode = ShutdownMode.OnExplicitShutdown);
        builder.Services.AddAvaloniaComponent<TrayIconComponent>();

        using var host = builder.Build();
        host.RunAvalonia();
    }
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>
/// Creates Avalonia's tray icon on the UI thread. Avalonia disposes it when the application exits.
/// </summary>
internal sealed class TrayIconComponent(IAvaloniaThread thread, IAvaloniaContext context) : IAvaloniaComponent
{
    public void InitializeComponent()
    {
        var open = new NativeMenuItem("Open window");
        open.Click += (_, _) => new Window { Title = "Opened from the tray", Width = 360, Height = 160 }.Show();

        // Stops the host, which exits the application.
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => thread.HandleApplicationExit();

        var trayIcon = new TrayIcon
        {
            Icon = CreateIcon(),
            ToolTipText = "Scarlet.Extensions.Hosting.Avalonia",
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } },
        };
        TrayIcon.SetIcons(context.Application!, [trayIcon]);
    }

    /// <summary>
    /// A plain square, so the sample needs no image asset.
    /// </summary>
    private static WindowIcon CreateIcon()
    {
        var bitmap = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new int[32 * 32];
            Array.Fill(pixels, unchecked((int)0xFF8A2BE2));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return new WindowIcon(bitmap);
    }
}
