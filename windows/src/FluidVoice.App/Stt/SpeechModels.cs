using System.IO;
using FluidVoice.Core;

namespace FluidVoice.Stt;

/// <summary>Which native runtime executes a speech model.</summary>
public enum SpeechEngineKind
{
    /// <summary>whisper.cpp via Whisper.net — single GGML file, batch decode.</summary>
    Whisper,
    /// <summary>sherpa-onnx — NeMo/k2 transducer ONNX files, offline finals + true streaming partials.</summary>
    Parakeet,
}

/// <summary>One file of a multi-file (sherpa-onnx) model. RelativePath is under the model's LocalPath directory.</summary>
public sealed record ModelFile(string RelativePath, string Url, long Bytes);

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
    public SpeechEngineKind Engine { get; init; } = SpeechEngineKind.Whisper;

    /// <summary>Multi-file models: every file that must exist under LocalPath (a directory). Null for single-file Whisper models.</summary>
    public IReadOnlyList<ModelFile>? Files { get; init; }

    /// <summary>
    /// Whisper partials are periodic full re-decodes; on big models one pass takes far longer
    /// than the tick, and the stop path must wait for the in-flight decode — which made
    /// Large/Medium feel "very slow". Disabled there: recording stays free, stop = one decode.
    /// (Parakeet streams natively and ignores this flag.)
    /// </summary>
    public bool SupportsLivePreview { get; init; } = true;

    /// <summary>Whisper: the GGML file. Parakeet: the model directory (FileName is the directory name).</summary>
    public string LocalPath => Engine == SpeechEngineKind.Whisper
        ? Path.Combine(AppPaths.WhisperModelDir, FileName)
        : Path.Combine(AppPaths.SherpaModelDir, FileName);

    public bool IsDownloaded => Files is null
        ? File.Exists(LocalPath) && new FileInfo(LocalPath).Length == ExpectedBytes
        : Files.All(f =>
        {
            var path = Path.Combine(LocalPath, f.RelativePath);
            return File.Exists(path) && new FileInfo(path).Length == f.Bytes;
        });

    public string SizeDisplay => ExpectedBytes switch
    {
        >= 1_000_000_000 => $"{ExpectedBytes / 1024.0 / 1024 / 1024:0.00} GiB",
        _ => $"{ExpectedBytes / 1024.0 / 1024:0} MiB",
    };
}

/// <summary>
/// The Windows speech-model catalog. Whisper is the parity baseline; Parakeet TDT
/// (the mac app's default CoreML engine) is substituted with the k2-fsa ONNX export
/// running on sherpa-onnx (win-arm64 native). The remaining mac-only engines
/// (Nemotron / Cohere / Apple Speech — CoreML, Apple Silicon) are documented in PARITY.md.
/// Whisper byte sizes/labels mirror SettingsStore.swift:3722-4211 exactly.
/// </summary>
public static class SpeechModels
{
    public const string HuggingFaceBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    public const string DefaultModelId = "whisper-base";
    public const string ParakeetModelId = "parakeet-tdt-0.6b-v2";

    // k2-fsa ONNX export of nvidia/parakeet-tdt-0.6b-v2 (int8), offline transducer for final decodes.
    private const string ParakeetBase = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/";
    // Companion streaming transducer (k2 zipformer int8) that powers true live partials while recording;
    // the final transcript always comes from Parakeet. Stored under <model>/streaming/.
    private const string StreamingPreviewBase = "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/main/";

    private static readonly IReadOnlyList<ModelFile> ParakeetFiles = new List<ModelFile>
    {
        new("encoder.int8.onnx", ParakeetBase + "encoder.int8.onnx", 652_184_296),
        new("decoder.int8.onnx", ParakeetBase + "decoder.int8.onnx", 7_257_753),
        new("joiner.int8.onnx", ParakeetBase + "joiner.int8.onnx", 1_739_080),
        new("tokens.txt", ParakeetBase + "tokens.txt", 9_384),
        new(Path.Combine("streaming", "encoder.int8.onnx"), StreamingPreviewBase + "encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx", 71_083_163),
        new(Path.Combine("streaming", "decoder.int8.onnx"), StreamingPreviewBase + "decoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx", 1_307_236),
        new(Path.Combine("streaming", "joiner.int8.onnx"), StreamingPreviewBase + "joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx", 259_335),
        new(Path.Combine("streaming", "tokens.txt"), StreamingPreviewBase + "tokens.txt", 5_048),
    };

    public static readonly IReadOnlyList<SpeechModelInfo> All = new List<SpeechModelInfo>
    {
        new(ParakeetModelId, "Parakeet TDT 0.6B v2", "Fastest + Live Streaming",
            "NVIDIA Parakeet (ONNX int8, sherpa-onnx). Near-instant finals with true live streaming preview.",
            733_845_295, "parakeet-tdt-0.6b-v2-int8", "English", 0.95, 0.93, "New")
        {
            Engine = SpeechEngineKind.Parakeet,
            Files = ParakeetFiles,
        },
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
            1_533_763_059, "ggml-medium.bin", "99 Languages", 0.40, 0.80, null)
        { SupportsLivePreview = false },
        // NOTE: measured on Snapdragon X Elite — q5-quantized turbo decodes SLOWER than f16
        // (38s vs 26s for a 6.4s clip; the q5 kernels aren't NEON-optimized), so no quantized
        // variant is offered. For near-instant dictation use Parakeet; Small/Base for Whisper.
        new("whisper-large-turbo", "Whisper Large Turbo", "Accuracy over Speed",
            "Near-maximum accuracy, but heavy for CPU decoding (roughly 4x slower than real time on this class of device). For fast dictation use Parakeet or Whisper Small.",
            1_624_555_275, "ggml-large-v3-turbo.bin", "99 Languages", 0.25, 0.95, null)
        { SupportsLivePreview = false },
        new("whisper-large", "Whisper Large", "Maximum Accuracy",
            "Best possible accuracy. Very slow on CPU — best for File Transcription rather than live dictation.",
            3_095_033_483, "ggml-large-v3.bin", "99 Languages", 0.10, 1.00, null)
        { SupportsLivePreview = false },
    };

    public static SpeechModelInfo? ById(string id) => All.FirstOrDefault(m => m.Id == id);

    public static SpeechModelInfo Selected()
        => ById(Settings.Current.SelectedSpeechModel) ?? ById(DefaultModelId)!;

    public static string DownloadUrl(SpeechModelInfo model) => HuggingFaceBase + model.FileName;
}
