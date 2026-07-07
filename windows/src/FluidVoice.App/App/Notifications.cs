using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>
/// User notifications (NotificationService.swift). Routed through the tray balloon
/// so no packaging identity is required.
/// </summary>
public static class Notifications
{
    /// <summary>Wired by Program to the tray icon's balloon tip.</summary>
    public static Action<string, string>? ShowHandler;

    public static void NotifyAiFallback(string error)
    {
        if (!Settings.Current.NotifyAIProcessingFailures) return;
        // exact strings from NotificationService.swift
        ShowHandler?.Invoke("AI Enhancement failed", "Typed raw transcription instead.");
        Log.Warn("notify", $"AI Enhancement failed: {error}");
    }

    public static void NotifyCommandModeSetup(string error)
    {
        if (!Settings.Current.NotifyAIProcessingFailures) return;
        ShowHandler?.Invoke("Command Mode needs setup", error);
    }

    public static void Show(string title, string body) => ShowHandler?.Invoke(title, body);
}
