using System.Runtime.InteropServices;
using FluidVoice.Core;

namespace FluidVoice.Typing;

/// <summary>
/// Raw Win32 clipboard access with full-format snapshot/restore
/// (parity with ClipboardService.swift + TypingService pasteboard session:
/// snapshot ALL formats, set text, paste, restore after 5s unless the
/// clipboard changed externally).
/// </summary>
public static class ClipboardService
{
    public sealed record Snapshot(List<(uint Format, string? Name, byte[] Data)> Items, uint SequenceNumber);

    private static readonly object Sync = new();

    /// <summary>Formats that are not HGLOBAL-backed and can't be byte-copied.</summary>
    private static bool IsCopyableFormat(uint fmt) => fmt switch
    {
        2 => false,      // CF_BITMAP (GDI handle)
        3 => false,      // CF_METAFILEPICT
        14 => false,     // CF_ENHMETAFILE
        0x0080 => false, // CF_OWNERDISPLAY
        >= 0x0081 and <= 0x008F => false, // CF_DSP*
        >= 0x0200 and <= 0x02FF => false, // CF_PRIVATEFIRST..LAST
        >= 0x0300 and <= 0x03FF => false, // CF_GDIOBJFIRST..LAST
        _ => true,
    };

    public static Snapshot? TakeSnapshot()
    {
        lock (Sync)
        {
            if (!OpenClipboardWithRetry()) return null;
            try
            {
                var items = new List<(uint, string?, byte[])>();
                uint fmt = 0;
                while ((fmt = EnumClipboardFormats(fmt)) != 0)
                {
                    if (!IsCopyableFormat(fmt)) continue;
                    var handle = GetClipboardData(fmt);
                    if (handle == IntPtr.Zero) continue;
                    var size = GlobalSize(handle);
                    if (size == UIntPtr.Zero || (long)size > 64L * 1024 * 1024) continue;
                    var ptr = GlobalLock(handle);
                    if (ptr == IntPtr.Zero) continue;
                    try
                    {
                        var data = new byte[(long)size];
                        Marshal.Copy(ptr, data, 0, data.Length);
                        string? name = null;
                        if (fmt >= 0xC000) // registered format: capture its name to re-register
                        {
                            var sb = new System.Text.StringBuilder(256);
                            if (GetClipboardFormatName(fmt, sb, sb.Capacity) > 0) name = sb.ToString();
                        }
                        items.Add((fmt, name, data));
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                return new Snapshot(items, GetClipboardSequenceNumber());
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    /// <summary>Sets plain unicode text; returns the sequence number after the set.</summary>
    public static uint? SetText(string text)
    {
        lock (Sync)
        {
            if (!OpenClipboardWithRetry()) return null;
            try
            {
                EmptyClipboard();
                var bytes = (text + "\0").ToCharArray();
                var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(bytes.Length * 2));
                if (handle == IntPtr.Zero) return null;
                var ptr = GlobalLock(handle);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(handle);
                if (SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero)
                {
                    GlobalFree(handle);
                    return null;
                }
            }
            finally
            {
                CloseClipboard();
            }
            return GetClipboardSequenceNumber();
        }
    }

    public static string? GetText()
    {
        lock (Sync)
        {
            if (!OpenClipboardWithRetry()) return null;
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                var ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    public static uint SequenceNumber() => GetClipboardSequenceNumber();

    public static bool Restore(Snapshot snapshot)
    {
        lock (Sync)
        {
            if (!OpenClipboardWithRetry()) return false;
            try
            {
                EmptyClipboard();
                foreach (var (fmt, name, data) in snapshot.Items)
                {
                    uint targetFmt = fmt;
                    if (name is not null)
                    {
                        var reg = RegisterClipboardFormat(name);
                        if (reg != 0) targetFmt = reg;
                    }
                    var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                    if (handle == IntPtr.Zero) continue;
                    var ptr = GlobalLock(handle);
                    Marshal.Copy(data, 0, ptr, data.Length);
                    GlobalUnlock(handle);
                    if (SetClipboardData(targetFmt, handle) == IntPtr.Zero) GlobalFree(handle);
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    private static bool OpenClipboardWithRetry()
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        Log.Warn("clipboard", "Could not open clipboard after retries");
        return false;
    }

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, System.Text.StringBuilder lpszFormatName, int cchMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);
}
