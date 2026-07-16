using System.Threading;
using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>
/// Cross-process single-instance coordination. The first instance owns a named mutex and
/// listens on a named event; a second launch signals that event (bringing the running
/// window forward) and exits silently — no "already running" dialog.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\LiquidFlow.SingleInstance";
    private const string EventName = @"Local\LiquidFlow.Activate";

    private readonly Mutex _mutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _listener;
    private volatile bool _running;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Second instance: signal the running one to show its window, then we exit.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("instance", $"Failed to signal existing instance: {ex.Message}");
        }
    }

    /// <summary>First instance: run <paramref name="onActivate"/> whenever another launch pokes us.</summary>
    public void StartListening(Action onActivate)
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _running = true;
        _listener = new Thread(() =>
        {
            while (_running)
            {
                try
                {
                    if (_activateEvent.WaitOne(1000) && _running)
                        onActivate();
                }
                catch (Exception ex)
                {
                    Log.Warn("instance", $"Activation listener error: {ex.Message}");
                }
            }
        })
        { IsBackground = true, Name = "FluidVoice-Activation" };
        _listener.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _activateEvent?.Set(); } catch { }
        try { _activateEvent?.Dispose(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
