using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidVoice.Input;

namespace FluidVoice.Core;

public enum HotkeyActivationMode { Toggle, Hold, Automatic }
public enum OverlaySize { Pill, Small, Medium, Large }
public enum OverlayPosition { Top, Bottom }
public enum TextInsertionMode { Standard, ReliablePaste }
public enum ThemePreference { System, Light, Dark }
public enum PromptMode { Dictate, Edit }
public enum PromptRoutingScope { AllApps, SelectedAppsOnly }

/// <summary>One user dictionary replacement: any trigger word/phrase becomes the replacement.</summary>
public sealed class CustomDictionaryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Triggers { get; set; } = new();
    public string Replacement { get; set; } = "";
}

/// <summary>A named prompt profile for a mode (mirrors DictationPromptProfile).</summary>
public sealed class PromptProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public PromptMode Mode { get; set; } = PromptMode.Dictate;
    /// <summary>Body text; combined with the built-in base prompt at call time.</summary>
    public string Prompt { get; set; } = "";
}

/// <summary>Binds an app (process name, e.g. "notepad") to a prompt profile for a mode.</summary>
public sealed class AppPromptBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Lowercase process name without extension (Windows analog of a bundle id).</summary>
    public string AppId { get; set; } = "";
    public string? AppDisplayName { get; set; }
    public PromptMode Mode { get; set; } = PromptMode.Dictate;
    /// <summary>null = use the default prompt for this mode.</summary>
    public string? PromptId { get; set; }
}

/// <summary>A user-defined OpenAI-compatible provider.</summary>
public sealed class CustomProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

/// <summary>
/// All persisted user settings. Keys/defaults mirror the mac SettingsStore
/// (verified against SettingsStore.swift; see windows/docs/SPEC.md §10).
/// </summary>
public sealed class Settings
{
    // ----- Hotkeys -----
    public List<HotkeyShortcut> PrimaryDictationShortcuts { get; set; } = new() { HotkeyShortcut.RightAlt() };
    public HotkeyActivationMode HotkeyMode { get; set; } = HotkeyActivationMode.Toggle;
    public HotkeyShortcut PromptModeShortcut { get; set; } = HotkeyShortcut.RightShift();
    public bool PromptModeShortcutEnabled { get; set; }
    public HotkeyShortcut? CommandModeShortcut { get; set; }
    public bool CommandModeShortcutEnabled { get; set; }
    public HotkeyShortcut RewriteModeShortcut { get; set; } = HotkeyShortcut.AltR();
    public bool RewriteModeShortcutEnabled { get; set; } = true;
    public HotkeyShortcut CancelRecordingShortcut { get; set; } = HotkeyShortcut.Escape();
    public HotkeyShortcut? PasteLastTranscriptionShortcut { get; set; }
    public bool PasteLastTranscriptionShortcutEnabled { get; set; }

    // ----- Overlay -----
    public OverlaySize OverlaySize { get; set; } = OverlaySize.Medium;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.Bottom;
    public double OverlayBottomOffset { get; set; } = 50.0;
    public int TranscriptionPreviewCharLimit { get; set; } = 150;
    public bool EnableStreamingPreview { get; set; } = true;

    // ----- Speech model -----
    public string SelectedSpeechModel { get; set; } = "whisper-base";
    /// <summary>"auto" or an ISO 639-1 code understood by Whisper.</summary>
    public string WhisperLanguage { get; set; } = "auto";
    public string? PreferredInputDeviceId { get; set; }

    // ----- Typing -----
    // Windows default is clipboard paste: SendInput-unicode bursts are dropped/garbled by
    // modern apps (Win11 Notepad, WinUI/RichEdit, some Electron), whereas Ctrl+V is instant
    // and reliable everywhere. "Clipboard-free" remains selectable for apps that need it.
    // (mac defaults to .standard because CGEvent unicode is reliable there — see PARITY.md.)
    public TextInsertionMode TextInsertionMode { get; set; } = TextInsertionMode.ReliablePaste;
    public bool CopyTranscriptionToClipboard { get; set; }

    // ----- Formatting pipeline (defaults verified true in SettingsStore.swift:3571,3579) -----
    public bool RemoveFillerWordsEnabled { get; set; } = true;
    public List<string> FillerWords { get; set; } = new() { "um", "uh", "uhm", "hmm", "mhm", "erm" };
    public bool AutoConvertPunctuationEnabled { get; set; } = true;
    public List<CustomDictionaryEntry> CustomDictionaryEntries { get; set; } = new();
    public bool GaavRemoveTrailingPeriodEnabled { get; set; }
    public bool GaavLowercaseFirstLetterEnabled { get; set; }
    public bool ContinuousDictationSpacingEnabled { get; set; }
    public bool ContextAwareCapitalizationEnabled { get; set; }

    // ----- AI enhancement -----
    /// <summary>"" = AI processing off (mac: promptSelection .off / empty provider).</summary>
    public string SelectedProviderID { get; set; } = "";
    public Dictionary<string, string> SelectedModelByProvider { get; set; } = new();
    public Dictionary<string, List<string>> AvailableModelsByProvider { get; set; } = new();
    public List<CustomProvider> CustomProviders { get; set; } = new();
    public Dictionary<string, string> VerifiedProviderFingerprints { get; set; } = new();
    public bool EnableAIStreaming { get; set; } = true;
    public bool ShowThinkingTokens { get; set; }
    public bool NotifyAIProcessingFailures { get; set; } = true;

    // Prompt profiles + routing
    public List<PromptProfile> PromptProfiles { get; set; } = new();
    public string? SelectedDictationPromptId { get; set; }
    public string? SelectedEditPromptId { get; set; }
    public bool DictationPromptOff { get; set; }
    public bool EditPromptOff { get; set; }
    public string? DefaultDictationPromptOverride { get; set; }
    public string? DefaultEditPromptOverride { get; set; }
    public List<AppPromptBinding> AppPromptBindings { get; set; } = new();
    public PromptRoutingScope DictationPromptRoutingScope { get; set; } = PromptRoutingScope.AllApps;
    public PromptRoutingScope EditPromptRoutingScope { get; set; } = PromptRoutingScope.AllApps;

    // ----- Local AI (open substitute for the proprietary Fluid Intelligence runtime) -----
    public string LocalAiModelId { get; set; } = "qwen2.5-1.5b-instruct-q4";
    public int LocalAiContextTokens { get; set; } = 4096;

    // ----- Command mode -----
    public bool CommandModeLinkedToGlobal { get; set; } = true;
    public string CommandModeSelectedProviderID { get; set; } = "";
    public string? CommandModeSelectedModel { get; set; }
    public bool CommandModeConfirmBeforeExecute { get; set; } = true;

    // ----- Rewrite (edit) mode -----
    public bool RewriteModeLinkedToGlobal { get; set; } = true;
    public string RewriteModeSelectedProviderID { get; set; } = "";
    public string? RewriteModeSelectedModel { get; set; }

    // ----- History & stats -----
    public bool SaveTranscriptionHistory { get; set; } = true;
    public bool SaveAudioWithTranscriptionHistory { get; set; }
    public double AudioHistoryBudgetGB { get; set; } = 5.0;
    public int UserTypingWPM { get; set; } = 40;

    // ----- Sounds / media -----
    public bool EnableTranscriptionSounds { get; set; } = true;
    public float TranscriptionSoundVolume { get; set; } = 1.0f;
    public bool PauseMediaDuringTranscription { get; set; }

    // ----- App behavior -----
    /// <summary>Bumped when a release wants to migrate existing settings once.</summary>
    public int SettingsRevision { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.Light; // Wispr-style light-first
    public string AccentColor { get; set; } = "#3AC8C6"; // Cyan default (mac AccentColorOption)
    /// <summary>UI font display name (see Ui/FontChoice). "System" = Segoe UI Variable.</summary>
    public string AppFont { get; set; } = "System";
    public bool LaunchAtStartup { get; set; } = true; // always-on dictation app
    public bool AutoUpdateCheckEnabled { get; set; } = true;
    public bool BetaReleasesEnabled { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string DisplayName { get; set; } = "";
    /// <summary>Welcome-page "Setup Tested Successfully" checkmark.</summary>
    public bool SetupTested { get; set; }

    // ------------------------------------------------------------------

    [JsonIgnore] public static Settings Current { get; private set; } = new();

    /// <summary>Raised (on any thread) after Save(); arg is a hint of what changed ("" = unknown/many).</summary>
    public static event Action<string>? Changed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly object SaveSync = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (loaded is not null)
                {
                    Current = loaded;
                    // rev 1: Wispr-style light-first UI — move existing System-theme users to Light once
                    if (Current.SettingsRevision < 1)
                    {
                        if (Current.Theme == ThemePreference.System) Current.Theme = ThemePreference.Light;
                        Current.SettingsRevision = 1;
                        Current.Save("migration");
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("settings", "Failed to load settings; using defaults", ex);
        }
        Current = new Settings();
    }

    public void Save(string changedHint = "")
    {
        lock (SaveSync)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error("settings", "Failed to save settings", ex);
            }
        }
        Changed?.Invoke(changedHint);
    }
}
