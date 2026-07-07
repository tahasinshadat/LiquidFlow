using FluidVoice.Core;

namespace FluidVoice.Typing;

/// <summary>
/// Smart Typing: reliable text insertion into whatever app is focused.
/// Port of TypingService.swift with its two modes and timing constants:
///  - Standard ("Clipboard Free Insert", default): SendInput Unicode chunks of 200
///    UTF-16 units → clipboard-paste fallback → char-by-char last resort.
///  - ReliablePaste ("Clipboard Paste"): snapshot clipboard → set text → Ctrl+V →
///    restore after 5s unless the clipboard changed externally.
/// Settle delays: standard 200ms / reliablePaste 80ms when the target window is
/// unknown, 0ms when we captured it at hotkey time (TypingService.swift:270-275).
/// </summary>
public static class TypingService
{
    private static int _isTyping;

    public static bool IsCurrentlyTyping => Volatile.Read(ref _isTyping) == 1;

    /// <summary>Last text we inserted (for the paste-last-transcription shortcut).</summary>
    public static string? LastTypedText { get; private set; }

    public static bool TypeTextInstantly(string text, FocusSnapshot? target)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (Interlocked.Exchange(ref _isTyping, 1) == 1)
        {
            Log.Warn("typing", "Skipped: another insertion is in progress");
            return false;
        }
        try
        {
            var mode = Settings.Current.TextInsertionMode;
            int settleMs = mode == TextInsertionMode.ReliablePaste
                ? (target is null ? 80 : 0)
                : (target is null ? 200 : 0);

            if (target is not null && !FocusTracker.IsStillForeground(target))
            {
                FocusTracker.Restore(target);
            }
            if (settleMs > 0) Thread.Sleep(settleMs);

            NativeInput.ReleaseStuckModifiers();

            bool ok = mode == TextInsertionMode.ReliablePaste
                ? InsertViaClipboardPaste(text) || InsertDirect(text)
                : InsertDirect(text) || InsertViaClipboardPaste(text) || InsertCharByChar(text);

            if (ok)
            {
                LastTypedText = text;
                if (Settings.Current.CopyTranscriptionToClipboard)
                    ClipboardService.SetText(text);
            }
            return ok;
        }
        finally
        {
            Volatile.Write(ref _isTyping, 0);
        }
    }

    private static bool InsertDirect(string text)
    {
        try
        {
            var ok = NativeInput.SendUnicodeText(text);
            if (ok) Log.Info("typing", $"Inserted {text.Length} chars via SendInput unicode");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn("typing", $"Direct insert failed: {ex.Message}");
            return false;
        }
    }

    private static bool InsertViaClipboardPaste(string text)
    {
        try
        {
            var snapshot = ClipboardService.TakeSnapshot();
            var seqAfterSet = ClipboardService.SetText(text);
            if (seqAfterSet is null) return false;

            var pasted = NativeInput.SendCtrlCombo(0x56 /*V*/, gapMs: 10);
            if (!pasted) return false;
            Log.Info("typing", $"Inserted {text.Length} chars via clipboard paste");

            if (snapshot is not null)
            {
                // Restore after 5s unless the clipboard changed externally (TypingService.swift:605-621)
                var ourSeq = seqAfterSet.Value;
                Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
                {
                    try
                    {
                        if (ClipboardService.SequenceNumber() == ourSeq || ClipboardService.GetText() == text)
                            ClipboardService.Restore(snapshot);
                        else
                            Log.Info("typing", "Skipped clipboard restore: clipboard changed externally");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("typing", $"Clipboard restore failed: {ex.Message}");
                    }
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("typing", $"Clipboard paste failed: {ex.Message}");
            return false;
        }
    }

    private static bool InsertCharByChar(string text)
    {
        try
        {
            foreach (var c in text)
            {
                if (c == '\r') continue;
                if (!NativeInput.SendUnicodeChar(c)) return false;
                Thread.Sleep(1); // 1ms inter-char (TypingService.swift:432)
            }
            Log.Info("typing", $"Inserted {text.Length} chars char-by-char");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("typing", "Char-by-char insert failed", ex);
            return false;
        }
    }
}
