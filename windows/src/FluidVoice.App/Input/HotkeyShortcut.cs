using System.Text.Json.Serialization;

namespace FluidVoice.Input;

public enum ShortcutKind { Keyboard, Mouse }

[Flags]
public enum ModMask
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global shortcut: either a regular key + modifiers, a bare modifier key
/// (e.g. Right Alt — parity with mac Right Option), or a mouse button + modifiers.
/// </summary>
public sealed class HotkeyShortcut : IEquatable<HotkeyShortcut>
{
    public ShortcutKind Kind { get; set; } = ShortcutKind.Keyboard;
    /// <summary>Virtual-key code for keyboard shortcuts (specific L/R codes for modifiers).</summary>
    public int VirtualKey { get; set; }
    /// <summary>Mouse button for mouse shortcuts: 2=middle, 3=X1, 4=X2.</summary>
    public int MouseButton { get; set; }
    public ModMask Modifiers { get; set; }

    [JsonIgnore]
    public bool IsModifierOnly => Kind == ShortcutKind.Keyboard && IsModifierKey(VirtualKey) && Modifiers == ModMask.None;

    public static bool IsModifierKey(int vk) => vk is
        0xA0 or 0xA1 or // L/R Shift
        0xA2 or 0xA3 or // L/R Control
        0xA4 or 0xA5 or // L/R Alt
        0x5B or 0x5C;   // L/R Win

    public static ModMask ModifierFlagFor(int vk) => vk switch
    {
        0xA0 or 0xA1 => ModMask.Shift,
        0xA2 or 0xA3 => ModMask.Control,
        0xA4 or 0xA5 => ModMask.Alt,
        0x5B or 0x5C => ModMask.Win,
        _ => ModMask.None,
    };

    public static HotkeyShortcut RightAlt() => new() { VirtualKey = 0xA5 };
    public static HotkeyShortcut RightCtrl() => new() { VirtualKey = 0xA3 };
    public static HotkeyShortcut RightShift() => new() { VirtualKey = 0xA1 };
    public static HotkeyShortcut AltR() => new() { VirtualKey = 0x52, Modifiers = ModMask.Alt };
    public static HotkeyShortcut Escape() => new() { VirtualKey = 0x1B };

    public string DisplayString
    {
        get
        {
            if (Kind == ShortcutKind.Mouse)
            {
                var btn = MouseButton switch { 2 => "Middle Click", 3 => "Mouse X1", 4 => "Mouse X2", _ => $"Mouse {MouseButton}" };
                return Modifiers == ModMask.None ? btn : $"{ModString(Modifiers)}+{btn}";
            }
            var key = KeyNames.NameOf(VirtualKey);
            return Modifiers == ModMask.None ? key : $"{ModString(Modifiers)}+{key}";
        }
    }

    private static string ModString(ModMask m)
    {
        var parts = new List<string>(4);
        if (m.HasFlag(ModMask.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModMask.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModMask.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModMask.Win)) parts.Add("Win");
        return string.Join("+", parts);
    }

    public bool Equals(HotkeyShortcut? other) =>
        other is not null && Kind == other.Kind && VirtualKey == other.VirtualKey &&
        MouseButton == other.MouseButton && Modifiers == other.Modifiers;

    public override bool Equals(object? obj) => Equals(obj as HotkeyShortcut);
    public override int GetHashCode() => HashCode.Combine(Kind, VirtualKey, MouseButton, Modifiers);
}

public static class KeyNames
{
    public static string NameOf(int vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x14 => "CapsLock",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),        // 0-9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),        // A-Z
        0x5B => "Left Win",
        0x5C => "Right Win",
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"VK{vk:X2}",
    };
}
