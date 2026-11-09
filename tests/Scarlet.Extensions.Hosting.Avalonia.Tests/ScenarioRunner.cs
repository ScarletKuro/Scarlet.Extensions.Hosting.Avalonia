using System.Diagnostics;

namespace Scarlet.Extensions.Hosting.Avalonia.Tests;

/// <summary>
/// Runs a ScenarioApp scenario in its own process (Avalonia sets up one application per process) and collects its events.
/// Set SCARLET_SCENARIO_APP to run another build of it, e.g. the Native AOT published one.
/// </summary>
internal static class ScenarioRunner
{
    public const int HostFailedExitCode = 3;

    private const string AppName = "Scarlet.Extensions.Hosting.Avalonia.ScenarioApp";

    /// <summary>
    /// The thread models a scenario runs in: the UI on a dedicated thread (host.Run), and on the main thread
    /// (host.RunAvalonia). On macOS, where Avalonia requires the main thread, only the latter.
    /// </summary>
    public static TheoryData<bool> Modes { get; } = OperatingSystem.IsMacOS() ? [true] : [false, true];

    public static ScenarioResult Run(string scenario, bool mainThread = false)
    {
        var startInfo = new ProcessStartInfo(FindExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scenario);
        if (mainThread)
        {
            startInfo.ArgumentList.Add("--main-thread");
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var lockObject = new Lock();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lockObject)
                {
                    output.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lockObject)
                {
                    output.Add(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(TimeSpan.FromSeconds(90)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Scenario '{scenario}' did not exit.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
        }

        // Flushes the asynchronous output readers.
        process.WaitForExit();
        lock (lockObject)
        {
            return new ScenarioResult($"{scenario}{(mainThread ? " (main thread)" : string.Empty)}", process.ExitCode, [.. output]);
        }
    }

    private static string FindExecutable()
    {
        if (Environment.GetEnvironmentVariable("SCARLET_SCENARIO_APP") is { Length: > 0 } configured)
        {
            return configured;
        }

        // tests/<this project>/bin/<configuration>/<tfm>/ -> tests/ScenarioApp/bin/<configuration>/<tfm>/
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = baseDirectory.Name;
        var configuration = baseDirectory.Parent!.Name;
        var testsDirectory = baseDirectory.Parent!.Parent!.Parent!.Parent!.FullName;
        var executable = OperatingSystem.IsWindows() ? AppName + ".exe" : AppName;
        var path = Path.Combine(testsDirectory, AppName, "bin", configuration, tfm, executable);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("ScenarioApp not found; build the solution or set SCARLET_SCENARIO_APP.", path);
        }

        return path;
    }
}

internal sealed record ScenarioResult(string Scenario, int ExitCode, IReadOnlyList<string> Output)
{
    /// <summary>
    /// The reported events, in order, as "name|value".
    /// </summary>
    public IReadOnlyList<string> Events { get; } = [.. Output.Where(line => line.StartsWith("EVT|", StringComparison.Ordinal)).Select(line => line[4..])];

    public string Describe() => $"Scenario '{Scenario}' exited with {ExitCode}.{Environment.NewLine}{string.Join(Environment.NewLine, Output)}";

    public void AssertEvents(params string[] expected)
    {
        Assert.True(Events.SequenceEqual(expected), $"Expected events:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}{Environment.NewLine}{Environment.NewLine}{Describe()}");
    }

    /// <summary>
    /// Like <see cref="AssertEvents"/>, but the last event may also be <paramref name="optionalLast"/>.
    /// </summary>
    public void AssertEventsIgnoringLast(string optionalLast, params string[] expected)
    {
        var events = Events.Count > 0 && Events[^1] == optionalLast ? Events.Take(Events.Count - 1) : Events;
        Assert.True(events.SequenceEqual(expected), $"Expected events (optionally followed by {optionalLast}):{Environment.NewLine}{string.Join(Environment.NewLine, expected)}{Environment.NewLine}{Environment.NewLine}{Describe()}");
    }

    public void AssertExitCode(int expected) => Assert.True(ExitCode == expected, Describe());
}
