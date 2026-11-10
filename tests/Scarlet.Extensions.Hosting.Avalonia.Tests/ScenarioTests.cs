namespace Scarlet.Extensions.Hosting.Avalonia.Tests;

/// <summary>
/// End-to-end lifecycle tests: each runs a real host with an Avalonia application in a ScenarioApp process, with the UI on a
/// dedicated thread (<c>host.Run()</c>) and on the main thread (<c>host.RunAvalonia()</c>).
/// </summary>
public class ScenarioTests
{
    public static TheoryData<bool> Modes => ScenarioRunner.Modes;

    [Theory]
    [MemberData(nameof(Modes))]
    public void HostStopRunsStartupComponentsAndCleanupOnTheUiThreadInOrder(bool mainThread)
    {
        var result = ScenarioRunner.Run("stop", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "Started|running",
            "Posted|ui",
            "OnExit|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ClosingTheLastWindowStopsTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("window-close", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "WindowCreated|ui",
            "ClosingWindow|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheApplicationIsCreatedWithDependencyInjectionAndItsMainWindowShownAndClosedStopsTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("app-main-window", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "MainWindowSet|ui",
            "OnStartup|ui:app",
            "MainWindowShown|ui:titled by DI",
            "ClosingMainWindow|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void HandleApplicationExitFromAnotherThreadStopsTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("handle-exit", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "HandleApplicationExit|other",
            "OnExit|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ComponentFailureFailsHostStartupAndDisposesComponentsInReverseOrder(bool mainThread)
    {
        var result = ScenarioRunner.Run("startup-failure", mainThread);

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "FailingComponentInit|ui",
            "OnExit|ui",
            "FailingComponentDispose|ui",
            "ComponentDispose|ui",
            "HostFailed|InvalidOperationException: boom");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void WorkQueuedBeforeTheApplicationRunsRunsOnceStartedInItsCallersExecutionContext(bool mainThread)
    {
        var result = ScenarioRunner.Run("prestart-post", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "PrestartPosting|not-running",
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "PrestartPostRan|ui:prestart-caller",
            "PrestartDispatcherPostRan|ui:prestart-caller",
            "PrestartInvocationRan|ui:prestart-caller",
            "OnExit|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void WorkQueuedForAnApplicationThatNeverStartsIsAbortedNotLeftPending(bool mainThread)
    {
        var result = ScenarioRunner.Run("prestart-abandoned", mainThread);

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents(
            "HostFailed|InvalidOperationException: start failure",
            "BufferedInvocation|cancelled");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void HandledUnhandledExceptionKeepsTheApplicationRunning(bool mainThread)
    {
        var result = ScenarioRunner.Run("unhandled-handled", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "UnhandledException|ui:posted failure",
            "StillRunning|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UnhandledExceptionIsLoggedAndTerminatesTheProcess(bool mainThread)
    {
        var result = ScenarioRunner.Run("unhandled-crash", mainThread);

        Assert.Contains("Throwing|ui", result.Events);
        Assert.DoesNotContain(result.Events, e => e.StartsWith("HostStopped", StringComparison.Ordinal));
        Assert.Contains(result.Output, line => line.StartsWith("LOG|Critical|", StringComparison.Ordinal) && line.Contains("Unhandled exception on the Avalonia UI thread", StringComparison.Ordinal));
        AssertTerminated(result, mainThread, "HostFailed|InvalidOperationException: unhandled posted failure");
    }

    /// <summary>
    /// The process is terminated. On the main thread the exception propagates out of RunAvalonia, like WPF's Application.Run,
    /// where Main can catch it (here it does: HostFailed), except when it crosses the platform's native window procedure in a
    /// Native AOT build, which can't unwind native frames and fails fast.
    /// </summary>
    private static void AssertTerminated(ScenarioResult result, bool mainThread, string caughtByMain)
    {
        Assert.NotEqual(0, result.ExitCode);
        if (result.ExitCode == ScenarioRunner.HostFailedExitCode)
        {
            Assert.True(mainThread, result.Describe());
            Assert.Contains(caughtByMain, result.Events);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void SendAlwaysReturnsBeforeDuringAndAfterTheApplicationRuns(bool mainThread)
    {
        var result = ScenarioRunner.Run("send", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "SendBeforeStartRejected|",
            "ContextCopy|same",
            "SendWhileRunningRan|ui",
            "SendException|sent failure",
            "SendCallbackCancellation|callback cancelled",
            "SendOnUiThreadRan|ui",
            "SendDuringExitRejected|",
            "HostStopped|not-running",
            "SendDuringExitFinished|yes",
            // In main-thread mode, Main is the UI thread, where Send runs inline.
            mainThread ? "SendAfterExitRan|ui" : "SendAfterExitRejected|");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void HostStopEndsTheApplicationEvenWhenAWindowVetoesClosing(bool mainThread)
    {
        var result = ScenarioRunner.Run("exit-cancelled", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "WindowCreated|ui",
            "CloseVetoed|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Fact]
    public void StartupCancelledDuringInitializationNeverRunsTheMainLoop()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "Uses a dedicated UI thread, which macOS doesn't allow.");
        var result = ScenarioRunner.Run("canceled-during-init");

        result.AssertExitCode(0);

        // No PrestartPost: the work posted before startup never ran, the loop never started.
        result.AssertEvents(
            "OnStartup|ui:app",
            "SlowInitStarted|ui",
            "SlowInitFinished|ui",
            "OnExit|ui",
            "SlowComponentDispose|ui",
            "StartCanceled|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CleanupFailuresAreLoggedAndEverythingElseIsStillCleanedUp(bool mainThread)
    {
        var result = ScenarioRunner.Run("cleanup-failures", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "ComponentInit|ui",
            "OnExit|ui",
            "ThrowingDispose|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
        Assert.Contains(result.Output, line => line.Contains("exit failure", StringComparison.Ordinal));
        Assert.Contains(result.Output, line => line.Contains("component dispose failure", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UnhandledExceptionHandlerThatThrowsTerminatesTheProcess(bool mainThread)
    {
        var result = ScenarioRunner.Run("unhandled-handler-throws", mainThread);

        Assert.Contains("Throwing|ui", result.Events);
        Assert.DoesNotContain(result.Events, e => e.StartsWith("HostStopped", StringComparison.Ordinal));
        Assert.Contains(result.Output, line => line.Contains("handler failure", StringComparison.Ordinal));
        AssertTerminated(result, mainThread, "HostFailed|FormatException: handler failure");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CleanupOutlivingTheShutdownTimeoutDoesntHangTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("shutdown-timeout", mainThread);

        result.AssertExitCode(0);
        Assert.Contains(result.Output, line => line.Contains("didn't end within the host's shutdown timeout", StringComparison.Ordinal));
        if (mainThread)
        {
            // The host gave up waiting after its timeout, but RunAvalonia runs on the UI thread itself: it returns once the
            // cleanup has finished.
            result.AssertEvents(
                "OnExitStarted|ui",
                "OnExitFinished|ui",
                "HostStopped|not-running",
                Assert.Single(result.Events, e => e.StartsWith("StoppedWithinTimeout|", StringComparison.Ordinal)));
        }
        else
        {
            result.AssertEvents(
                "OnExitStarted|ui",
                "HostStopped|not-running",
                "StoppedWithinTimeout|yes");
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void PostsRunInTheirCallersExecutionContextAlsoWhenPostedBeforeStart(bool mainThread)
    {
        var result = ScenarioRunner.Run("execution-context", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "PrestartPost|prestart-caller",
            "RunningPost|running-caller",
            "LaterPost|",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void LaterStartupFailureCleansUpTheApplicationBeforeItsDependenciesAreDisposed(bool mainThread)
    {
        var result = ScenarioRunner.Run("started-failure", mainThread);

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents(
            "OnStartup|ui:app",
            "DependentComponentInit|ui",
            "OnExit|ui",
            "DependentComponentDispose|ui:dependency-alive",
            "DependencyDisposed|other",
            "HostFailed|InvalidOperationException: started failure");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void PostingAfterTheHostIsDisposedDoesNotThrow(bool mainThread)
    {
        var result = ScenarioRunner.Run("post-after-exit", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "OnExit|ui",
            "HostStopped|not-running",
            "PostAfterExit|no-throw");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AnAwaitOnTheUiThreadResumingAfterItEndedIsDroppedWithoutCrashing(bool mainThread)
    {
        var result = ScenarioRunner.Run("await-after-exit", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "OnExit|ui",
            "OtherStopping|other",
            "OtherStopped|other",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ExitingBeforeTheApplicationHasStartedStopsTheHostNormally(bool mainThread)
    {
        var result = ScenarioRunner.Run("exit-before-started", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "HandleApplicationExit|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Fact]
    public void TheDedicatedUiThreadDoesntInheritTheExecutionContextOfWhoeverStartedTheHost()
    {
        // In main-thread mode the UI thread is the one starting the host, so this only applies to the dedicated thread.
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "Uses a dedicated UI thread, which macOS doesn't allow.");
        var result = ScenarioRunner.Run("ui-execution-context");

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|",
            "FlowSuppressedPost|",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EnvironmentExitOnTheUiThreadStopsTheOtherServicesWithoutWaitingForTheShutdownTimeout(bool mainThread)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = ScenarioRunner.Run("environment-exit", mainThread);

        // The shutdown timeout is 10 s: waiting for the UI thread would take at least that.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"Took {watch.Elapsed}.{Environment.NewLine}{result.Describe()}");
        result.AssertExitCode(5);

        // Main can still return from Run (and report it) before Environment.Exit terminates the process.
        result.AssertEventsIgnoringLast(
            "HostStopped|not-running",
            "OnStartup|ui:app",
            "EnvironmentExit|ui",
            "OtherStopping|other",
            "OtherStopped|other");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheEndOfTheWindowsSessionStopsTheApplicationAndTheHostBeforeItsQueryReturns(bool mainThread)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows session end.");
        var result = ScenarioRunner.Run("session-end", mainThread);

        result.AssertExitCode(0);

        // HostStopped is reported by the main thread, independently of the query's return.
        Assert.Equal(
            [
                "OnStartup|ui:app",
                "ComponentInit|ui",
                "OnExit|ui",
                "ComponentDispose|ui",
                "OtherStopping|other",
                "OtherStopped|other",
                "QueryEndSession|1:host-stopped",
            ],
            result.Events.Where(e => !e.StartsWith("HostStopped|", StringComparison.Ordinal)));
        Assert.Contains("HostStopped|not-running", result.Events);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UsingAvaloniasUiThreadInAHostedServiceConstructorCantTakeItFromTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("hijack-in-constructor", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "TouchedUiThread|other",
            "OnStartup|ui:owns-dispatcher",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UsingAvaloniasUiThreadFromAnotherThreadBeforeTheHostStartsFailsStartupClearly(bool mainThread)
    {
        var result = ScenarioRunner.Run("hijack-before-start", mainThread);

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents("HostFailed|InvalidOperationException: Avalonia's UI thread was already taken by another thread");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void WorkInvokedWhileTheApplicationExitsRunsOrIsCancelledNeverLeftPending(bool mainThread)
    {
        var result = ScenarioRunner.Run("shutdown-flood", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "OnExit|ui",
            "HostStopped|not-running",
            "FloodPending|0",
            "FloodAccounted|yes");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void JoinableTaskFactorySwitchesToTheUiThreadBeforeAndAfterItStarts(bool mainThread)
    {
        var result = ScenarioRunner.Run("threading", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "EarlySwitch|ui",
            "SwitchedToMain|ui",
            "RunOnMain|ui",
            "AsyncLazy|ui:ui:1",
            "BackToBackground|other",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ShuttingDownInOnStartupStopsTheHostWithoutCreatingAnyComponent(bool mainThread)
    {
        var result = ScenarioRunner.Run("shutdown-in-startup", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AComponentShuttingDownStopsTheHostDisposesTheComponentsCreatedSoFarAndCreatesNoMore(bool mainThread)
    {
        var result = ScenarioRunner.Run("shutdown-in-component", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "ShuttingDownComponentInit|ui",
            "OnExit|ui",
            "ShuttingDownComponentDispose|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [InlineData("shutdown-in-constructor", false)]
    [InlineData("shutdown-in-constructor", true)]
    [InlineData("shutdown-in-factory", false)]
    [InlineData("shutdown-in-factory", true)]
    public void AComponentWhoseConstructionShutsDownIsDisposedNeverInitializedAndTheHostStops(string scenario, bool mainThread)
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS() && !mainThread, "Uses a dedicated UI thread, which macOS doesn't allow.");
        var result = ScenarioRunner.Run(scenario, mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ConstructorShutdown|ui",
            "OnExit|ui",
            "ShutdownInConstructorDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ShuttingDownWhileTheApplicationIsSetUpStillRunsOnExitAndStopsTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("shutdown-in-app-init", mainThread);

        result.AssertExitCode(0);
        result.AssertEvents(
            "AppInitShutdown|ui",
            "OnExit|ui",
            "HostStopped|not-running");
    }

    [Fact]
    public void AnUnhandledUiExceptionLeavesRunAvaloniaOnlyOnceTheHostHasStopped()
    {
        var result = ScenarioRunner.Run("unhandled-crash-slow-stop", mainThread: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Throwing|ui", result.Events);
        if (result.ExitCode == ScenarioRunner.HostFailedExitCode)
        {
            // Main caught it from RunAvalonia: the other hosted service had finished stopping by then. (A Native AOT build
            // fails fast instead: the exception can't cross the platform's native window procedure.)
            Assert.Equal(
                ["OnStartup|ui:app", "Throwing|ui", "OnExit|ui", "OtherStopping|other", "OtherStopped|other", "HostFailed|InvalidOperationException: unhandled posted failure"],
                result.Events);
        }
    }

    [Fact]
    public void AnUnhandledUiExceptionIsTheOneRunAvaloniaReportsEvenWhenAServiceFailsToStop()
    {
        var result = ScenarioRunner.Run("unhandled-crash-failing-stop", mainThread: true);

        AssertTerminated(result, mainThread: true, "HostFailed|InvalidOperationException: unhandled posted failure");
        Assert.DoesNotContain(result.Events, static e => e.Contains("stop failure", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void HandleApplicationExitOnTheUiThreadExitsTheApplicationAndStopsTheHost(bool mainThread)
    {
        var result = ScenarioRunner.Run("handle-exit-on-ui", mainThread);

        // It can't wait on the UI thread for the UI thread to end: it returns, and the application exits right after.
        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "HandleApplicationExit|ui",
            "HandleApplicationExitReturned|ui",
            "OnExit|ui",
            "ComponentDispose|ui",
            "HostStopped|not-running");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EnvironmentExitFromAServiceCleansUpTheApplicationAndKeepsItsExitCode(bool mainThread)
    {
        var result = ScenarioRunner.Run("environment-exit-from-service", mainThread);

        // The host stopping the application must not replace the exit code with Avalonia's default 0.
        result.AssertExitCode(5);
        result.AssertEventsIgnoringLast(
            "HostStopped|not-running",
            "OnStartup|ui:app",
            "ComponentInit|ui",
            "EnvironmentExit|other",
            "OnExit|ui",
            "ComponentDispose|ui",
            "OtherStopping|other",
            "OtherStopped|other");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ASendStillQueuedWhenTheApplicationExitsThrowsInsteadOfHanging(bool mainThread)
    {
        var result = ScenarioRunner.Run("send-queued-at-exit", mainThread);

        // Avalonia's shutdown aborts the queued work on Windows and Linux; on macOS it may still run it. Either way Send returns.
        var queuedSend = result.Events.Contains("QueuedSendRan|ui") ? "QueuedSendRan|ui" : "QueuedSendDropped|dropped";
        result.AssertExitCode(0);
        result.AssertEvents(
            "OnStartup|ui:app",
            "ExitingWithSendQueued|ui",
            "OnExit|ui",
            queuedSend,
            "HostStopped|not-running",
            "QueuedSendReturned|yes");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AMainWindowFailingAsItIsShownFailsHostStartupAndCleansUp(bool mainThread)
    {
        var result = ScenarioRunner.Run("main-window-fails", mainThread);

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents(
            "OnStartup|ui:app",
            "MainWindowOpened|ui",
            "OnExit|ui",
            "MainWindowFailingComponentDispose|ui",
            "HostFailed|InvalidOperationException: main window failure");
    }

    [Fact]
    public void StartupCancelledBeforeTheUiStartsNeverCreatesIt()
    {
        var result = ScenarioRunner.Run("canceled-before-ui");

        result.AssertExitCode(0);
        result.AssertEvents("CancellingStartup|other", "Started|not-running");
    }

    [Fact]
    public void RunAvaloniaAfterTheDedicatedUiThreadWasCreatedIsRejected()
    {
        var result = ScenarioRunner.Run("run-after-resolve", mainThread: true);

        result.AssertExitCode(0);
        result.AssertEvents("RunAvaloniaRejected|dedicated-thread");
    }

    [Fact]
    public void OnMacOsADedicatedUiThreadFailsStartupPointingToRunAvalonia()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        var result = ScenarioRunner.Run("macos-dedicated-thread");

        result.AssertExitCode(ScenarioRunner.HostFailedExitCode);
        result.AssertEvents("HostFailed|PlatformNotSupportedException: On macOS, Avalonia runs on the process's main thread");
    }
}
