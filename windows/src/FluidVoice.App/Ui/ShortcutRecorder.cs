using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Input;

namespace FluidVoice.Ui;

/// <summary>
/// Click-to-record control for a HotkeyShortcut. Captures a regular key + modifiers,
/// or a bare modifier (Right Alt etc.) when pressed and released alone.
/// </summary>
public sealed class ShortcutRecorder : Button
{
    private HotkeyShortcut _shortcut;
    private bool _recording;

    public event Action<HotkeyShortcut>? ShortcutChanged;

    public ShortcutRecorder(HotkeyShortcut initial)
    {
        _shortcut = initial;
        Padding = new Thickness(10, 6, 10, 6);
        MinWidth = 160;
        Foreground = Theme.TextBrush;
        Background = new SolidColorBrush(Theme.Field);
        UpdateLabel();
        Click += (_, _) => BeginRecording();
        LostFocus += (_, _) => EndRecording();
    }

    public HotkeyShortcut Shortcut => _shortcut;

    /// <summary>Programmatic update (preset buttons).</summary>
    public void SetShortcut(HotkeyShortcut shortcut)
    {
        _shortcut = shortcut;
        _recording = false;
        UpdateLabel();
    }

    private void BeginRecording()
    {
        _recording = true;
        Content = "Press keys… (Esc to cancel)";
        Background = new SolidColorBrush(Theme.Accent);
        Focus();
    }

    private void EndRecording()
    {
        if (!_recording) return;
        _recording = false;
        Background = new SolidColorBrush(Theme.Field);
        UpdateLabel();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndRecording(); return; }

        // bare modifier: LeftAlt/RightAlt/Ctrl/Shift/Win pressed alone
        int? bareVk = key switch
        {
            Key.RightAlt => 0xA5,
            Key.LeftAlt => 0xA4,
            Key.RightCtrl => 0xA3,
            Key.LeftCtrl => 0xA2,
            Key.RightShift => 0xA1,
            Key.LeftShift => 0xA0,
            Key.LWin => 0x5B,
            Key.RWin => 0x5C,
            _ => null,
        };

        var mods = Keyboard.Modifiers;
        if (bareVk is { } vk && mods == ModifierKeys.None)
        {
            _shortcut = new HotkeyShortcut { VirtualKey = vk };
            Commit();
            return;
        }

        // regular key + modifiers
        if (bareVk is null && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin))
        {
            int winVk = KeyInterop.VirtualKeyFromKey(key);
            var mask = ModMask.None;
            if (mods.HasFlag(ModifierKeys.Control)) mask |= ModMask.Control;
            if (mods.HasFlag(ModifierKeys.Alt)) mask |= ModMask.Alt;
            if (mods.HasFlag(ModifierKeys.Shift)) mask |= ModMask.Shift;
            if (mods.HasFlag(ModifierKeys.Windows)) mask |= ModMask.Win;
            _shortcut = new HotkeyShortcut { VirtualKey = winVk, Modifiers = mask };
            Commit();
        }
    }

    private void Commit()
    {
        _recording = false;
        Background = new SolidColorBrush(Theme.Field);
        UpdateLabel();
        ShortcutChanged?.Invoke(_shortcut);
    }

    private void UpdateLabel() => Content = _shortcut.DisplayString;
}
