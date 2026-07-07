using System.Windows;
using System.Windows.Threading;
using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>
/// Keeps the tray app alive through transient faults. A background dictation app must not
/// vanish because one async task or UI callback threw — these handlers log everything and,
/// where the CLR allows, swallow the fault instead of tearing the process down.
/// </summary>
public static class CrashGuard
{
    public static void Install(System.Windows.Application app)
    {
        // UI-thread exceptions: log and keep running (the window/overlay stays alive)
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("crash", "Dispatcher exception (handled, app kept alive)", e.Exception);
            e.Handled = true;
        };

        // fire-and-forget Task exceptions that were never awaited
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("crash", "Unobserved task exception (observed, app kept alive)", e.Exception);
            e.SetObserved();
        };

        // last-resort: a background thread threw. We can't stop CLR teardown for a truly
        // fatal one, but we log it so "random shutdown" has a paper trail — and most of our
        // background work (hook, audio, updater, llama) is already wrapped in try/catch.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error("crash", $"Unhandled AppDomain exception (terminating={e.IsTerminating})", ex);
            else
                Log.Error("crash", $"Unhandled AppDomain error (terminating={e.IsTerminating}): {e.ExceptionObject}");
        };

        // log why we're exiting, so a genuine shutdown is distinguishable from a crash
        app.Exit += (_, e) => Log.Info("crash", $"Application.Exit (code {e.ApplicationExitCode})");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Info("crash", "ProcessExit");
    }
}
