using System.IO;
using FluidVoice.Core;

namespace FluidVoice.Stt;

public sealed record SpeechModelInfo(
    string Id,
    string DisplayName,
    string Tagline,
    string Description,
    long ExpectedBytes,
    string FileName,
    string LanguageSupport,
    double SpeedPercent,
    double AccuracyPercent,
    string? Badge)
{
    public string LocalPath => Path.Combine(AppPaths.WhisperModelDir, FileName);
    public bool IsDownloaded => File.Exists(LocalPath) && new FileInfo(LocalPath).Length == ExpectedBytes;
    public string SizeDisplay => ExpectedBytes switch
    {
        >= 1_000_000_000 => $"{ExpectedBytes / 1024.0 / 1024 / 1024:0.00} GiB",
        _ => $"{ExpectedBytes / 1024.0 / 1024:0} MiB",
    };
}

/// <summary>
/// The Windows speech-model catalog. Whisper is the parity baseline; the mac-only
/// engines (Parakeet / Nemotron / Cohere / Apple Speech — CoreML, Apple Silicon)
/// cannot run on Windows ARM and are documented in PARITY.md.
/// Byte sizes/labels mirror SettingsStore.swift:3722-4211 exactly.
/// </summary>
public static class SpeechModels
{
    public const string HuggingFaceBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    public const string DefaultModelId = "whisper-base";

    public static readonly IReadOnlyList<SpeechModelInfo> All = new List<SpeechModelInfo>
    {
        new("whisper-tiny", "Whisper Tiny", "Fast & Light",
            "Minimal resource usage. Fastest response on battery.",
            77_691_713, "ggml-tiny.bin", "99 Languages", 0.90, 0.40, null),
        new("whisper-base", "Whisper Base", "Standard Choice",
            "Good balance of speed and accuracy. Works on any PC.",
            147_951_465, "ggml-base.bin", "99 Languages", 0.80, 0.60, "Default"),
        new("whisper-small", "Whisper Small", "Balanced Speed & Accuracy",
            "Better accuracy than Base. Moderate resource usage.",
            487_601_967, "ggml-small.bin", "99 Languages", 0.60, 0.70, "FluidVoice Pick"),
        new("whisper-medium", "Whisper Medium", "Medium Quality",
            "High accuracy for demanding tasks. Requires more memory.",
            1_533_763_059, "ggml-medium.bin", "99 Languages", 0.40, 0.80, null),
        new("whisper-large-turbo", "Whisper Large Turbo", "Higher Quality but Faster",
            "Near-maximum accuracy with optimized speed.",
            1_624_555_275, "ggml-large-v3-turbo.bin", "99 Languages", 0.65, 0.95, "New"),
        new("whisper-large", "Whisper Large", "Maximum Accuracy",
            "Best possible accuracy. Large download and memory usage.",
            3_095_033_483, "ggml-large-v3.bin", "99 Languages", 0.20, 1.00, null),
    };

    public static SpeechModelInfo? ById(string id) => All.FirstOrDefault(m => m.Id == id);

    public static SpeechModelInfo Selected()
        => ById(Settings.Current.SelectedSpeechModel) ?? ById(DefaultModelId)!;

    public static string DownloadUrl(SpeechModelInfo model) => HuggingFaceBase + model.FileName;
}
