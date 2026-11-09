using Microsoft.Extensions.Hosting;
using Scarlet.Extensions.Hosting.Avalonia.Threading;

namespace HostingXaml;

/// <summary>
/// A background service that updates the bound view model every second, switching to the UI thread with JoinableTaskFactory.
/// </summary>
internal sealed class ClockService(JoinableTaskFactory joinableTaskFactory, MainWindowViewModel viewModel) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await joinableTaskFactory.SwitchToMainThreadAsync(stoppingToken);
                viewModel.Time = $"Updated by a hosted service: {DateTimeOffset.Now:T}";

                await TaskScheduler.Default;
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
