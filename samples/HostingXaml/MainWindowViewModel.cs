using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Hosting;

namespace HostingXaml;

/// <summary>
/// The main window's view model, resolved from the container. Its properties are bound in XAML, so they change on the UI thread.
/// </summary>
internal sealed class MainWindowViewModel(IHostEnvironment environment, IHostApplicationLifetime lifetime) : INotifyPropertyChanged
{
    private string _time = "Waiting for the clock service...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Greeting { get; } = $"Hello from the {environment.EnvironmentName} host";

    public string Time
    {
        get => _time;
        set
        {
            if (_time != value)
            {
                _time = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Stops the host, which exits the application.
    /// </summary>
    public ICommand StopCommand { get; } = new DelegateCommand(lifetime.StopApplication);

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
