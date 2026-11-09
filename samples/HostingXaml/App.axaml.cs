using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace HostingXaml;

/// <summary>
/// The application, created by the host with constructor injection on the UI thread.
/// </summary>
internal sealed partial class App : Application
{
    private readonly MainWindowViewModel? _viewModel;

    /// <summary>
    /// For the XAML previewer.
    /// </summary>
    public App()
    {
    }

    [ActivatorUtilitiesConstructor]
    public App(MainWindowViewModel viewModel) => _viewModel = viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The lifetime shows the main window when its main loop starts; closing it exits the application, which stops the host.
        if (_viewModel is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = _viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
