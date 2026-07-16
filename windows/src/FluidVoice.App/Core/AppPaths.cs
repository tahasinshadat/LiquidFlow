using System.IO;

namespace FluidVoice.Core;

/// <summary>Well-known data locations. Mirrors the mac app's use of Application Support / Caches.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>
    /// Data now lives in %LOCALAPPDATA%\LiquidFlow. On first run after the FluidVoice→LiquidFlow
    /// rename, move the old folder over: a same-volume Directory.Move is instant and atomic and
    /// preserves settings, history, models, and audio. If it can't (locked or cross-volume), keep
    /// using the old folder so no data is ever lost.
    /// </summary>
    private static string ResolveDataDir()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newDir = Path.Combine(local, "LiquidFlow");
        var oldDir = Path.Combine(local, "FluidVoice");
        if (!Directory.Exists(newDir) && Directory.Exists(oldDir))
        {
            try { Directory.Move(oldDir, newDir); }
            catch { return oldDir; }
        }
        return newDir;
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.json");
    public static string ChatHistoryFile => Path.Combine(DataDir, "command-chats.json");
    public static string LogDir => Path.Combine(DataDir, "Logs");
    public static string WhisperModelDir => Path.Combine(DataDir, "Models", "Whisper");
    public static string SherpaModelDir => Path.Combine(DataDir, "Models", "Sherpa");
    public static string LocalAiDir => Path.Combine(DataDir, "LocalAI");
    public static string LocalAiModelDir => Path.Combine(LocalAiDir, "Models");
    public static string LocalAiRuntimeDir => Path.Combine(LocalAiDir, "Runtime");
    public static string AudioHistoryDir => Path.Combine(DataDir, "DictationAudioHistory");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(WhisperModelDir);
        Directory.CreateDirectory(AudioHistoryDir);
    }
}
