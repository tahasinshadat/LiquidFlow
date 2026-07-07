using System.IO;

namespace FluidVoice.Core;

/// <summary>Well-known data locations. Mirrors the mac app's use of Application Support / Caches.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidVoice");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.json");
    public static string ChatHistoryFile => Path.Combine(DataDir, "command-chats.json");
    public static string LogDir => Path.Combine(DataDir, "Logs");
    public static string WhisperModelDir => Path.Combine(DataDir, "Models", "Whisper");
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
