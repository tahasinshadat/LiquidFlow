using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidVoice.Core;

namespace FluidVoice.Input;

/// <summary>Which recording flow a shortcut triggers.</summary>
public enum RecordingMode { None, Dictation, PromptMode, Command, Rewrite }

/// <summary>Implemented by the dictation coordinator; all methods must be non-blocking (hook thread!).</summary>
public interface IDictationControl
{
    RecordingMode ActiveMode { get; }
    bool IsProcessingStop { get; }
    void RequestStart(RecordingMode mode);
    void RequestStopAndProcess();
    void RequestCancel();
    void RequestPasteLast();
}

/// <summary>
/// Global hotkey routing + activation-mode state machine.
/// Port of GlobalHotkeyManager.swift: toggle / hold / automatic (tap&lt;400ms toggles,
/// hold&gt;=400ms is push-to-talk), bare-modifier clean-press detection, deferred stop
/// when a hold is released before recording actually started (60 x 50ms retries).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const double AutomaticTapThresholdSeconds = 0.4; // GlobalHotkeyManager.swift:73
    private const int DeferredStopMaxAttempts = 60;          // GlobalHotkeyManager.swift:1218
    private const int DeferredStopRetryMs = 50;

    private readonly KeyboardHook _hook;
    private readonly IDictationControl _control;
    private readonly bool _allowInjected =
        Environment.GetEnvironmentVariable("FLUIDVOICE_ALLOW_INJECTED") == "1";

    private sealed record Binding(HotkeyShortcut Shortcut, RecordingMode Mode);

    private volatile List<Binding> _recordingBindings = new();
    private HotkeyShortcut _cancelShortcut = HotkeyShortcut.Escape();
    private HotkeyShortcut? _pasteLastShortcut;
    private HotkeyActivationMode _activation = HotkeyActivationMode.Toggle;

    // Bare-modifier press tracking
    private sealed class ModPress
    {
        public int Vk;
        public RecordingMode Mode;
        public long DownTimestamp;
        public bool OtherKeyPressed;
        public bool WasTargetActive;
        public bool StartTriggered;
    }
    private ModPress? _modPress;

    // Automatic-mode press tracking for regular-key shortcuts
    private sealed class AutoPress
    {
        public RecordingMode Mode;
        public long DownTimestamp;
        public bool WasTargetActive;
    }
    private readonly Dictionary<int, AutoPress> _autoPressByVk = new();
    private readonly Dictionary<int, RecordingMode> _holdStartedByVk = new();
    private readonly HashSet<int> _swallowedDownVks = new();
    private int _pendingStopToken;

    /// <summary>Set true while a shortcut-capture UI is active: all events pass through untouched.</summary>
    public volatile bool CaptureMode;
    /// <summary>Raised (hook thread) with the raw shortcut when CaptureMode is on and a key/mouse is pressed.</summary>
    public event Action<HotkeyShortcut>? ShortcutCaptured;
    /// <summary>A transform hotkey (Win+Alt+N) fired; arg = TransformDef.Id.</summary>
    public event Action<string>? TransformRequested;
    private List<(HotkeyShortcut Shortcut, string TransformId)> _transformShortcuts = new();

    public HotkeyManager(KeyboardHook hook, IDictationControl control)
    {
        _hook = hook;
        _control = control;
        _hook.OnKey = HandleKey;
        _hook.OnMouse = HandleMouse;
        ReloadBindings();
        Settings.Changed += _ => ReloadBindings();
    }

    public void ReloadBindings()
    {
        var s = Settings.Current;
        var list = new List<Binding>();
        foreach (var sc in s.PrimaryDictationShortcuts)
            list.Add(new Binding(sc, RecordingMode.Dictation));
        if (s.PromptModeShortcutEnabled)
            list.Add(new Binding(s.PromptModeShortcut, RecordingMode.PromptMode));
        if (s.CommandModeShortcutEnabled && s.CommandModeShortcut is not null)
            list.Add(new Binding(s.CommandModeShortcut, RecordingMode.Command));
        if (s.RewriteModeShortcutEnabled)
            list.Add(new Binding(s.RewriteModeShortcut, RecordingMode.Rewrite));
        _recordingBindings = list;
        _cancelShortcut = s.CancelRecordingShortcut;
        _pasteLastShortcut = s.PasteLastTranscriptionShortcutEnabled ? s.PasteLastTranscriptionShortcut : null;
        _transformShortcuts = s.TransformsEnabled
            ? s.Transforms.Where(t => t.Slot is >= 1 and <= 9)
                .Select(t => (new HotkeyShortcut { VirtualKey = 0x30 + t.Slot, Modifiers = ModMask.Win | ModMask.Alt }, t.Id))
                .ToList()
            : new();
        _activation = s.HotkeyMode;
    }

    // ------------------------------------------------------------------
    private static readonly bool DebugHook =
        Environment.GetEnvironmentVariable("FLUIDVOICE_DEBUG_HOOK") == "1";

    // VK 0xFF is only ever our own synthetic mask key (SendMaskKey). Never let it touch state,
    // or it would count as "another key pressed during the modifier" and break clean-press detection.
    private const int MaskVirtualKey = 0xFF;

    private bool HandleKey(int vk, bool isDown, bool injected, bool repeat)
    {
        if (vk == MaskVirtualKey) return false;
        if (DebugHook && !repeat)
            Log.Info("hook", $"key vk=0x{vk:X2} {(isDown ? "down" : "up")} injected={injected} allow={_allowInjected} activeMode={_control.ActiveMode}");
        if (injected && !_allowInjected) return false;

        if (CaptureMode)
        {
            if (isDown && !HotkeyShortcut.IsModifierKey(vk))
            {
                var mods = _hook.CurrentModifiers();
                ShortcutCaptured?.Invoke(new HotkeyShortcut { VirtualKey = vk, Modifiers = mods });
            }
            else if (!isDown && HotkeyShortcut.IsModifierKey(vk) && !_hook.AnyNonModifierKeyDown()
                     && _hook.CurrentModifiers() == ModMask.None)
            {
                // bare modifier released cleanly while capturing → capture it as modifier-only
                ShortcutCaptured?.Invoke(new HotkeyShortcut { VirtualKey = vk });
            }
            return false;
        }

        return isDown ? HandleKeyDown(vk, repeat) : HandleKeyUp(vk);
    }

    private bool HandleKeyDown(int vk, bool repeat)
    {
        if (repeat)
            return _swallowedDownVks.Contains(vk); // keep swallowing repeats of swallowed keys

        if (HotkeyShortcut.IsModifierKey(vk))
        {
            // a second modifier during a bare-modifier press counts as interruption
            if (_modPress is { } mp && mp.Vk != vk) mp.OtherKeyPressed = true;

            var binding = FindBareModifierBinding(vk);
            if (DebugHook) Log.Info("hook", $"modifier down vk=0x{vk:X2} bareBinding={(binding is null ? "none" : binding.Mode.ToString())} bindings={_recordingBindings.Count}");
            if (binding is not null && _modPress is null)
            {
                var press = new ModPress
                {
                    Vk = vk,
                    Mode = binding.Mode,
                    DownTimestamp = Stopwatch.GetTimestamp(),
                    WasTargetActive = _control.ActiveMode == binding.Mode,
                };
                _modPress = press;

                // Alt/Win taps would trigger the app menu; inject a mask key like AutoHotkey does.
                if (vk is 0xA4 or 0xA5 or 0x5B or 0x5C) SendMaskKey();

                if (_activation is HotkeyActivationMode.Hold or HotkeyActivationMode.Automatic)
                {
                    if (_control.ActiveMode != binding.Mode && CanTrigger())
                    {
                        press.StartTriggered = true;
                        _control.RequestStart(binding.Mode);
                    }
                }
            }
            return false; // never swallow modifier keys themselves
        }

        // regular (non-modifier) key
        if (_modPress is { } activeMod) activeMod.OtherKeyPressed = true;

        var mods = _hook.CurrentModifiers();

        if (Matches(_cancelShortcut, vk, mods) && _cancelShortcut.Kind == ShortcutKind.Keyboard)
        {
            if (_control.ActiveMode != RecordingMode.None)
            {
                _control.RequestCancel();
                _swallowedDownVks.Add(vk);
                return true;
            }
            return false;
        }

        if (_pasteLastShortcut is not null && Matches(_pasteLastShortcut, vk, mods))
        {
            _control.RequestPasteLast();
            _swallowedDownVks.Add(vk);
            return true;
        }

        foreach (var (sc, transformId) in _transformShortcuts)
        {
            if (Matches(sc, vk, mods))
            {
                TransformRequested?.Invoke(transformId);
                _swallowedDownVks.Add(vk);
                return true;
            }
        }

        var rec = FindRegularBinding(vk, mods);
        if (rec is null) return false;

        switch (_activation)
        {
            case HotkeyActivationMode.Toggle:
                HandleToggleTrigger(rec.Mode);
                break;
            case HotkeyActivationMode.Hold:
                if (_control.ActiveMode != rec.Mode && CanTrigger())
                {
                    _holdStartedByVk[vk] = rec.Mode;
                    _control.RequestStart(rec.Mode);
                }
                else
                {
                    _holdStartedByVk[vk] = rec.Mode;
                }
                break;
            case HotkeyActivationMode.Automatic:
                var wasActive = _control.ActiveMode == rec.Mode;
                _autoPressByVk[vk] = new AutoPress
                {
                    Mode = rec.Mode,
                    DownTimestamp = Stopwatch.GetTimestamp(),
                    WasTargetActive = wasActive,
                };
                if (!wasActive && CanTrigger()) _control.RequestStart(rec.Mode);
                break;
        }
        _swallowedDownVks.Add(vk);
        return true;
    }

    private bool HandleKeyUp(int vk)
    {
        if (HotkeyShortcut.IsModifierKey(vk))
        {
            if (_modPress is { } press && press.Vk == vk)
            {
                _modPress = null;
                var clean = !press.OtherKeyPressed;
                var elapsed = Stopwatch.GetElapsedTime(press.DownTimestamp).TotalSeconds;

                switch (_activation)
                {
                    case HotkeyActivationMode.Toggle:
                        if (clean) HandleToggleTrigger(press.Mode);
                        break;
                    case HotkeyActivationMode.Hold:
                        if (press.StartTriggered || _control.ActiveMode == press.Mode)
                            StopAfterRelease(press.Mode);
                        break;
                    case HotkeyActivationMode.Automatic:
                        if (!clean)
                        {
                            if (press.StartTriggered) StopAfterRelease(press.Mode);
                        }
                        else if (elapsed < AutomaticTapThresholdSeconds)
                        {
                            // tap: toggles — stop only if it was already recording before this press
                            if (press.WasTargetActive) _control.RequestStopAndProcess();
                            // else: keep recording (we started it on key-down)
                        }
                        else
                        {
                            // hold: push-to-talk ends on release
                            StopAfterRelease(press.Mode);
                        }
                        break;
                }
            }
            return false;
        }

        bool swallow = _swallowedDownVks.Remove(vk);

        if (_holdStartedByVk.Remove(vk, out var heldMode))
        {
            StopAfterRelease(heldMode);
            return swallow;
        }

        if (_autoPressByVk.Remove(vk, out var auto))
        {
            var elapsed = Stopwatch.GetElapsedTime(auto.DownTimestamp).TotalSeconds;
            if (elapsed < AutomaticTapThresholdSeconds)
            {
                if (auto.WasTargetActive) _control.RequestStopAndProcess();
            }
            else
            {
                StopAfterRelease(auto.Mode);
            }
            return swallow;
        }

        return swallow;
    }

    private bool HandleMouse(int button, bool isDown, bool injected)
    {
        if (injected && !_allowInjected) return false;
        if (CaptureMode)
        {
            if (isDown && button >= 2)
                ShortcutCaptured?.Invoke(new HotkeyShortcut
                {
                    Kind = ShortcutKind.Mouse,
                    MouseButton = button,
                    Modifiers = _hook.CurrentModifiers(),
                });
            return false;
        }

        var mods = _hook.CurrentModifiers();
        var binding = _recordingBindings.FirstOrDefault(b =>
            b.Shortcut.Kind == ShortcutKind.Mouse && b.Shortcut.MouseButton == button && b.Shortcut.Modifiers == mods);
        if (binding is null) return false;

        if (isDown)
        {
            switch (_activation)
            {
                case HotkeyActivationMode.Toggle:
                    HandleToggleTrigger(binding.Mode);
                    break;
                case HotkeyActivationMode.Hold:
                case HotkeyActivationMode.Automatic:
                    var wasActive = _control.ActiveMode == binding.Mode;
                    _autoPressByVk[-button] = new AutoPress
                    {
                        Mode = binding.Mode,
                        DownTimestamp = Stopwatch.GetTimestamp(),
                        WasTargetActive = wasActive,
                    };
                    if (!wasActive && CanTrigger()) _control.RequestStart(binding.Mode);
                    break;
            }
            return true;
        }

        if (_autoPressByVk.Remove(-button, out var press))
        {
            if (_activation == HotkeyActivationMode.Hold)
            {
                StopAfterRelease(press.Mode);
            }
            else
            {
                var elapsed = Stopwatch.GetElapsedTime(press.DownTimestamp).TotalSeconds;
                if (elapsed < AutomaticTapThresholdSeconds)
                {
                    if (press.WasTargetActive) _control.RequestStopAndProcess();
                }
                else
                {
                    StopAfterRelease(press.Mode);
                }
            }
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    private void HandleToggleTrigger(RecordingMode mode)
    {
        if (!CanTrigger()) return;
        var active = _control.ActiveMode;
        if (active == mode) _control.RequestStopAndProcess();
        else _control.RequestStart(mode); // idle start, or in-flight mode switch
    }

    private bool CanTrigger() => !_control.IsProcessingStop;

    /// <summary>
    /// Port of beginPendingReleaseStop: if recording hasn't started yet when the key is
    /// released (slow start), retry for up to 3s; skip if the mode changed meanwhile.
    /// </summary>
    private void StopAfterRelease(RecordingMode mode)
    {
        if (_control.ActiveMode == mode)
        {
            _control.RequestStopAndProcess();
            return;
        }
        var token = Interlocked.Increment(ref _pendingStopToken);
        Task.Run(async () =>
        {
            for (var i = 0; i < DeferredStopMaxAttempts; i++)
            {
                await Task.Delay(DeferredStopRetryMs);
                if (token != Volatile.Read(ref _pendingStopToken)) return; // superseded
                var active = _control.ActiveMode;
                if (active == mode)
                {
                    _control.RequestStopAndProcess();
                    return;
                }
                if (active != RecordingMode.None) return; // a different mode took over
            }
            // never started → user's press-and-release predates recording; discard
        });
    }

    private Binding? FindBareModifierBinding(int vk) =>
        _recordingBindings.FirstOrDefault(b =>
            b.Shortcut.Kind == ShortcutKind.Keyboard && b.Shortcut.IsModifierOnly && b.Shortcut.VirtualKey == vk);

    private Binding? FindRegularBinding(int vk, ModMask mods) =>
        _recordingBindings.FirstOrDefault(b =>
            b.Shortcut.Kind == ShortcutKind.Keyboard && !b.Shortcut.IsModifierOnly &&
            b.Shortcut.VirtualKey == vk && b.Shortcut.Modifiers == mods);

    private static bool Matches(HotkeyShortcut sc, int vk, ModMask mods) =>
        sc.Kind == ShortcutKind.Keyboard && sc.VirtualKey == vk && sc.Modifiers == mods;

    /// <summary>Injects VK 0xFF down/up so a bare Alt/Win tap doesn't focus the app menu (AutoHotkey-style mask).</summary>
    private static void SendMaskKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1;
        inputs[0].U.ki.wVk = 0xFF;
        inputs[1].type = 1;
        inputs[1].U.ki.wVk = 0xFF;
        inputs[1].U.ki.dwFlags = 2; // KEYEVENTF_KEYUP
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => _hook.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
