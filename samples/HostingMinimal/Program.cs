using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia;
using Scarlet.Extensions.Hosting.Avalonia.Threading;

namespace HostingMinimal;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddAvalonia<App>(app => app.UsePlatformDetect().LogToTrace());
        builder.Services.AddAvaloniaComponent<MainWindowComponent>();
        builder.Services.AddThreadSwitching();
        builder.Services.AddSingleton(new Greeting("Hello from a DI service"));
        builder.Services.AddSingleton<MainWindowHolder>();
        builder.Services.AddHostedService<ClockService>();

        using var host = builder.Build();

        // The UI on the main thread (required on macOS, fine everywhere), the host in the background. On Windows and Linux,
        // host.Run() would run the UI on a dedicated thread instead.
        host.RunAvalonia();
    }
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

internal sealed record Greeting(string Text);

/// <summary>
/// Lets services reach the window, which only the UI thread may touch.
/// </summary>
internal sealed class MainWindowHolder
{
    public MainWindow? Window { get; set; }
}

/// <summary>
/// Creates the main window with its dependencies, on the UI thread. The lifetime shows it when the main loop starts, and
/// closing it exits the application, which stops the host.
/// </summary>
internal sealed class MainWindowComponent(IAvaloniaContext context, Greeting greeting, MainWindowHolder holder) : IAvaloniaComponent
{
    public void InitializeComponent()
    {
        holder.Window = new MainWindow(greeting.Text);
        context.Lifetime!.MainWindow = holder.Window;
    }
}

internal sealed class MainWindow : Window
{
    private readonly TextBlock _time = new();

    public MainWindow(string greeting)
    {
        Title = "Scarlet.Extensions.Hosting.Avalonia";
        Width = 460;
        Height = 180;
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = greeting, FontSize = 20 }, _time },
        };
    }

    public void ShowTime(DateTimeOffset time) => _time.Text = $"Updated by a hosted service: {time:T}";
}

/// <summary>
/// A background service that updates the window every second, switching to the UI thread with JoinableTaskFactory.
/// </summary>
internal sealed class ClockService(JoinableTaskFactory joinableTaskFactory, MainWindowHolder holder) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await joinableTaskFactory.SwitchToMainThreadAsync(stoppingToken);
                holder.Window?.ShowTime(DateTimeOffset.Now);

                await TaskScheduler.Default;
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
