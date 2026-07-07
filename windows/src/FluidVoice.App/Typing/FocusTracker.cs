using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FluidVoice.Typing;

public sealed record FocusSnapshot(IntPtr Hwnd, uint ProcessId, string ProcessName, string WindowTitle);

/// <summary>
/// Foreground app tracking + focus restore (Windows analog of ActiveAppMonitor +
/// the TypingService focus snapshot/restore: raise window, wait 40ms, retry focus).
/// The process name (lowercase, no extension) is our per-app id — the bundle-ID analog.
/// </summary>
public static class FocusTracker
{
    public static FocusSnapshot? Capture()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        string name = "";
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName.ToLowerInvariant();
        }
        catch { }
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return new FocusSnapshot(hwnd, pid, name, title.ToString());
    }

    /// <summary>Brings the captured window back to foreground; ~40ms settle like the mac impl.</summary>
    public static bool Restore(FocusSnapshot snapshot)
    {
        if (snapshot.Hwnd == IntPtr.Zero || !IsWindow(snapshot.Hwnd)) return false;
        if (GetForegroundWindow() == snapshot.Hwnd) return true;

        // AttachThreadInput dance so SetForegroundWindow succeeds from a background process
        var foreThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var ourThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(snapshot.Hwnd, out _);
        bool attached1 = foreThread != ourThread && AttachThreadInput(ourThread, foreThread, true);
        bool attached2 = targetThread != ourThread && AttachThreadInput(ourThread, targetThread, true);
        try
        {
            if (IsIconic(snapshot.Hwnd)) ShowWindow(snapshot.Hwnd, 9 /*SW_RESTORE*/);
            SetForegroundWindow(snapshot.Hwnd);
            for (int i = 0; i < 3; i++) // 3 retries @50ms (TypingService.swift:197-208)
            {
                Thread.Sleep(i == 0 ? 40 : 50);
                if (GetForegroundWindow() == snapshot.Hwnd) return true;
                SetForegroundWindow(snapshot.Hwnd);
            }
            return GetForegroundWindow() == snapshot.Hwnd;
        }
        finally
        {
            if (attached1) AttachThreadInput(ourThread, foreThread, false);
            if (attached2) AttachThreadInput(ourThread, targetThread, false);
        }
    }

    public static bool IsStillForeground(FocusSnapshot snapshot) => GetForegroundWindow() == snapshot.Hwnd;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
