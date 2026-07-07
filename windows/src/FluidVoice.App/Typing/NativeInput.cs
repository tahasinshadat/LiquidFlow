using System.Runtime.InteropServices;

namespace FluidVoice.Typing;

/// <summary>SendInput helpers: Unicode text injection and virtual-key combos.</summary>
public static class NativeInput
{
    // Small batches with a short delay: modern apps (Windows 11 Notepad, WinUI/RichEdit,
    // some Electron) drop or garble characters when a large KEYEVENTF_UNICODE burst is
    // posted all at once. 8 units + ~4ms per batch types ~2000 cps yet stays reliable.
    public const int UnicodeChunkSize = 8;
    public const int DefaultInterChunkDelayMs = 4;

    /// <summary>
    /// Types text via KEYEVENTF_UNICODE in surrogate-safe chunks. Newlines are sent as
    /// VK_RETURN and tabs as VK_TAB so editors treat them as real keys.
    /// </summary>
    public static bool SendUnicodeText(string text, int interChunkDelayMs = DefaultInterChunkDelayMs)
    {
        foreach (var run in SplitRuns(text))
        {
            if (run.Length == 1 && run[0] == '\n')
            {
                if (!SendVirtualKey(0x0D)) return false;
                continue;
            }
            if (run.Length == 1 && run[0] == '\t')
            {
                if (!SendVirtualKey(0x09)) return false;
                continue;
            }

            int i = 0;
            while (i < run.Length)
            {
                int end = Math.Min(i + UnicodeChunkSize, run.Length);
                // don't split a surrogate pair across chunks (TypingService.swift:729-747)
                if (end < run.Length && char.IsHighSurrogate(run[end - 1]) && char.IsLowSurrogate(run[end]))
                    end--;
                if (end <= i) end = i + 1;

                int count = end - i;
                var inputs = new INPUT[count * 2];
                for (int j = 0; j < count; j++)
                {
                    ushort unit = run[i + j];
                    inputs[j * 2] = UnicodeInput(unit, keyUp: false);
                    inputs[j * 2 + 1] = UnicodeInput(unit, keyUp: true);
                }
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
                    return false;
                if (interChunkDelayMs > 0) Thread.Sleep(interChunkDelayMs);
                i = end;
            }
        }
        return true;
    }

    /// <summary>Types one character with explicit key timing (last-resort path, ~3ms/char).</summary>
    public static bool SendUnicodeChar(char c)
    {
        if (c == '\n') return SendVirtualKey(0x0D);
        var down = new[] { UnicodeInput(c, keyUp: false) };
        if (SendInput(1, down, Marshal.SizeOf<INPUT>()) != 1) return false;
        Thread.Sleep(2);
        var up = new[] { UnicodeInput(c, keyUp: true) };
        return SendInput(1, up, Marshal.SizeOf<INPUT>()) == 1;
    }

    public static bool SendVirtualKey(ushort vk)
    {
        var inputs = new[]
        {
            KeyInput(vk, keyUp: false),
            KeyInput(vk, keyUp: true),
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    /// <summary>Ctrl+key with a 10ms gap between down and up (TypingService.swift:654,766).</summary>
    public static bool SendCtrlCombo(ushort vk, int gapMs = 10)
    {
        var down = new[] { KeyInput(0x11, false), KeyInput(vk, false) };
        if (SendInput((uint)down.Length, down, Marshal.SizeOf<INPUT>()) != down.Length) return false;
        Thread.Sleep(gapMs);
        var up = new[] { KeyInput(vk, true), KeyInput(0x11, true) };
        return SendInput((uint)up.Length, up, Marshal.SizeOf<INPUT>()) == up.Length;
    }

    /// <summary>Releases any modifier keys the system believes are down (avoids Ctrl+V turning into Ctrl+Alt+V).</summary>
    public static void ReleaseStuckModifiers()
    {
        Span<ushort> mods = stackalloc ushort[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
        var ups = new List<INPUT>();
        foreach (var vk in mods)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                ups.Add(KeyInput(vk, keyUp: true));
        }
        if (ups.Count > 0)
            SendInput((uint)ups.Count, ups.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static IEnumerable<string> SplitRuns(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\n' or '\t' or '\r')
            {
                if (i > start) yield return text[start..i];
                if (c != '\r') yield return c.ToString(); // fold \r\n → \n
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    private static INPUT UnicodeInput(ushort unit, bool keyUp) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = unit,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
            },
        },
    };

    private static INPUT KeyInput(ushort vk, bool keyUp) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u },
        },
    };

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
