using System.Windows.Automation;
using FluidVoice.Core;

namespace FluidVoice.Typing;

/// <summary>
/// Reads the current text selection from whatever app is focused
/// (TextSelectionService.swift): UI Automation TextPattern first,
/// then a clipboard-preserving Ctrl+C fallback.
/// </summary>
public static class SelectionReader
{
    public static string? GetSelectedText()
    {
        var viaUia = TryUiAutomation();
        if (!string.IsNullOrEmpty(viaUia)) return viaUia;
        return TryCopyFallback();
    }

    private static string? TryUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return null;
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) &&
                patternObj is TextPattern textPattern)
            {
                var ranges = textPattern.GetSelection();
                if (ranges is { Length: > 0 })
                {
                    var text = string.Join("", ranges.Select(r => r.GetText(int.MaxValue)));
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("selection", $"UIA selection read failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>Simulate Ctrl+C, read the clipboard, restore the previous contents.</summary>
    private static string? TryCopyFallback()
    {
        try
        {
            var snapshot = ClipboardService.TakeSnapshot();
            var seqBefore = ClipboardService.SequenceNumber();
            NativeInput.ReleaseStuckModifiers();
            NativeInput.SendCtrlCombo(0x43 /*C*/, gapMs: 10);

            // wait up to 500ms for the app to publish the copy
            string? text = null;
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(50);
                if (ClipboardService.SequenceNumber() != seqBefore)
                {
                    text = ClipboardService.GetText();
                    break;
                }
            }
            if (snapshot is not null) ClipboardService.Restore(snapshot);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex)
        {
            Log.Warn("selection", $"Copy fallback failed: {ex.Message}");
            return null;
        }
    }
}
