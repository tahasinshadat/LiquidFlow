using FluidVoice.Core;
using Whisper.net;

namespace FluidVoice.Stt;

/// <summary>
/// Whisper.net (whisper.cpp, ARM64-native) transcription engine.
/// Mirrors WhisperProvider.swift: load-on-prepare, resident until model change,
/// 16kHz mono float input, min 1s audio (padding handled by caller), batch decode.
/// Also powers the live-preview loop (full-prefix re-decode, like the mac streaming path).
/// </summary>
public sealed class WhisperEngine : ISpeechEngine
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelId;
    private string? _loadedLanguage;

    public bool IsReady => _processor is not null;
    public string? LoadedModelId => _loadedModelId;

    /// <summary>Ensure the selected model is downloaded and loaded into memory.</summary>
    public async Task PrepareAsync(SpeechModelInfo model, IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        var language = NormalizeLanguage(Settings.Current.WhisperLanguage);
        if (_loadedModelId == model.Id && _loadedLanguage == language && _processor is not null)
        {
            progress?.Report(new(ModelPreparationPhase.Ready, 1));
            return;
        }

        if (!model.IsDownloaded)
        {
            await ModelDownloader.DownloadModelAsync(model, progress, ct);
        }

        progress?.Report(new(ModelPreparationPhase.Loading, 0));
        await _gate.WaitAsync(ct);
        try
        {
            UnloadLocked();
            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(model.LocalPath);
                var builder = _factory.CreateBuilder()
                    .WithThreads(Math.Clamp(Environment.ProcessorCount - 2, 4, 10)); // X Elite: use 10 of 12 cores
                builder = language is null ? builder.WithLanguageDetection() : builder.WithLanguage(language);
                _processor = builder.Build();
            }, ct);
            _loadedModelId = model.Id;
            _loadedLanguage = language;
            Log.Info("whisper", $"Loaded {model.Id} (language={language ?? "auto"})");
            progress?.Report(new(ModelPreparationPhase.Ready, 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Transcribe 16kHz mono float PCM. Serialized; concurrent calls queue.
    /// IMPORTANT: the decode itself is never cancelled mid-stream — aborting
    /// ProcessAsync leaves stale segments in the native context that replay
    /// into the next call (observed as doubled transcripts). `ct` only gates entry.
    /// </summary>
    public async Task<string> TranscribeAsync(float[] pcm, CancellationToken ct)
    {
        if (_processor is null) throw new InvalidOperationException("Whisper model not loaded");
        await _gate.WaitAsync(ct);
        try
        {
            var processor = _processor;
            if (processor is null) return "";
            var pieces = new List<string>();
            await foreach (var segment in processor.ProcessAsync(pcm, CancellationToken.None))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text)) pieces.Add(segment.Text.Trim());
            }
            return string.Join(" ", pieces).Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Non-blocking best-effort partial decode: returns null if the engine is busy. Runs to completion once started (see TranscribeAsync).</summary>
    public async Task<string?> TryTranscribePartialAsync(float[] pcm, CancellationToken ct)
    {
        if (_processor is null) return null;
        if (!await _gate.WaitAsync(0, ct)) return null; // adaptive skipping: busy → skip this chunk
        try
        {
            var processor = _processor;
            if (processor is null) return null;
            var pieces = new List<string>();
            await foreach (var segment in processor.ProcessAsync(pcm, CancellationToken.None))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text)) pieces.Add(segment.Text.Trim());
            }
            return string.Join(" ", pieces).Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Whisper is batch-only: no true streaming — callers use the re-decode partial loop.</summary>
    public IStreamingPartialSession? TryBeginStreamingSession() => null;

    public void Unload()
    {
        _gate.Wait();
        try { UnloadLocked(); }
        finally { _gate.Release(); }
    }

    private void UnloadLocked()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
        _loadedModelId = null;
    }

    private static string? NormalizeLanguage(string setting)
        => string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : setting.Trim().ToLowerInvariant();

    public void Dispose()
    {
        try { Unload(); } catch { }
        _gate.Dispose();
    }
}
