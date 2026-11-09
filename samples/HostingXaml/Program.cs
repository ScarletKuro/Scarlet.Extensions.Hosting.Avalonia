using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;
using Scarlet.Extensions.Hosting.Avalonia.Threading;

namespace HostingXaml;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect().LogToTrace());
        builder.Services.AddAvaloniaLifetime();
        builder.Services.AddThreadSwitching();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddHostedService<ClockService>();

        using var host = builder.Build();
        host.RunAvalonia();
    }

    // Used by the XAML previewer, which creates App with its parameterless constructor.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
