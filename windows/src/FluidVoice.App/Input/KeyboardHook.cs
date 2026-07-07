using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidVoice.Core;

namespace FluidVoice.Input;

/// <summary>
/// Low-level keyboard + mouse hooks on a dedicated message-loop thread.
/// Windows analog of the mac CGEvent tap in GlobalHotkeyManager.swift.
/// Handlers run on the hook thread and must be fast; return true to swallow the event.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public delegate bool KeyHandler(int vk, bool isDown, bool isInjected, bool isRepeat);
    public delegate bool MouseHandler(int button, bool isDown, bool isInjected);

    public KeyHandler? OnKey;      // return true to swallow
    public MouseHandler? OnMouse;  // return true to swallow

    private IntPtr _kbHook;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly HashSet<int> _downKeys = new();
    private readonly object _downSync = new();
    private LowLevelProc? _kbProc;   // keep delegates alive (GC!)
    private LowLevelProc? _mouseProc;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "FluidVoice-Hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            Log.Error("hook", "Hook thread failed to start in time");
    }

    /// <summary>Currently held-down modifier mask, tracked from hook events.</summary>
    public ModMask CurrentModifiers()
    {
        lock (_downSync)
        {
            var m = ModMask.None;
            foreach (var vk in _downKeys) m |= HotkeyShortcut.ModifierFlagFor(vk);
            return m;
        }
    }

    /// <summary>Non-modifier keys currently held down.</summary>
    public bool AnyNonModifierKeyDown()
    {
        lock (_downSync)
        {
            foreach (var vk in _downKeys)
                if (!HotkeyShortcut.IsModifierKey(vk)) return true;
            return false;
        }
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _kbProc = KbCallback;
        _mouseProc = MouseCallback;
        using var module = Process.GetCurrentProcess().MainModule;
        var hMod = GetModuleHandle(module?.ModuleName);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_kbHook == IntPtr.Zero)
            Log.Error("hook", $"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }

    private IntPtr KbCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt64();
            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            if (isDown || isUp)
            {
                bool injected = (data.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
                bool repeat = false;
                if (!injected)
                {
                    lock (_downSync)
                    {
                        if (isDown) repeat = !_downKeys.Add((int)data.vkCode);
                        else _downKeys.Remove((int)data.vkCode);
                    }
                }
                try
                {
                    if (OnKey?.Invoke((int)data.vkCode, isDown, injected, repeat) == true)
                        return (IntPtr)1;
                }
                catch (Exception ex)
                {
                    Log.Error("hook", "key handler threw", ex);
                }
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt64();
            int button = -1;
            bool isDown = false;
            switch (msg)
            {
                case WM_MBUTTONDOWN: button = 2; isDown = true; break;
                case WM_MBUTTONUP: button = 2; break;
                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    button = (int)(data.mouseData >> 16) == 1 ? 3 : 4;
                    isDown = msg == WM_XBUTTONDOWN;
                    break;
            }
            if (button >= 0)
            {
                var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool injected = (d.flags & 0x1) != 0; // LLMHF_INJECTED
                try
                {
                    if (OnMouse?.Invoke(button, isDown, injected) == true)
                        return (IntPtr)1;
                }
                catch (Exception ex)
                {
                    Log.Error("hook", "mouse handler threw", ex);
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
    }

    // ---- interop ----
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int ptX;
        public int ptY;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }
}
