using System.Text;
using FluidVoice.Core;
using SherpaOnnx;

namespace FluidVoice.Stt;

/// <summary>
/// NVIDIA Parakeet TDT via sherpa-onnx (k2-fsa ONNX export, win-arm64 native) — the
/// substitute for the mac app's CoreML Parakeet provider. Two native recognizers:
///  - OfflineRecognizer (parakeet-tdt-0.6b-v2 int8, nemo_transducer): final transcripts,
///    near-instant even for long recordings.
///  - OnlineRecognizer (companion streaming zipformer int8 under &lt;model&gt;/streaming/):
///    true streaming partials — new samples are fed incrementally, so preview latency is
///    constant (~chunk size) instead of growing with the buffer like the Whisper re-decode loop.
/// Same load-on-prepare / resident-until-model-change lifecycle as WhisperEngine.
/// </summary>
public sealed class ParakeetEngine : ISpeechEngine
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineRecognizer? _offline;
    private OnlineRecognizer? _streaming; // null when the preview model failed to load (fallback: re-decode partials)
    private string? _loadedModelId;

    public bool IsReady => _offline is not null;
    public string? LoadedModelId => _loadedModelId;

    public async Task PrepareAsync(SpeechModelInfo model, IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        if (_loadedModelId == model.Id && _offline is not null)
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
                var dir = model.LocalPath;
                var config = new OfflineRecognizerConfig();
                config.FeatConfig.SampleRate = 16000;
                config.FeatConfig.FeatureDim = 80;
                config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
                config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
                config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
                config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
                config.ModelConfig.ModelType = "nemo_transducer";
                config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount - 2, 2, 8);
                config.ModelConfig.Provider = "cpu";
                config.DecodingMethod = "greedy_search";
                _offline = new OfflineRecognizer(config);

                try
                {
                    _streaming = LoadStreamingPreview(Path.Combine(dir, "streaming"));
                }
                catch (Exception ex)
                {
                    _streaming = null;
                    Log.Warn("parakeet", $"Streaming preview model unavailable, partials fall back to re-decode: {ex.Message}");
                }
            }, ct);
            _loadedModelId = model.Id;
            Log.Info("parakeet", $"Loaded {model.Id} (streaming preview: {(_streaming is not null ? "on" : "off")})");
            progress?.Report(new(ModelPreparationPhase.Ready, 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static OnlineRecognizer LoadStreamingPreview(string dir)
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.NumThreads = 2; // tiny model; leave cores for the offline decode at stop
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        // Segment on pauses so the per-segment decoder state stays small during long
        // dictations; committed segments are concatenated in the session below.
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = 0.9f;
        config.Rule3MinUtteranceLength = 300f; // never force a cut mid-speech
        return new OnlineRecognizer(config);
    }

    /// <summary>Final decode. Serialized; parakeet-tdt is fast enough that queueing is rare.</summary>
    public async Task<string> TranscribeAsync(float[] pcm, CancellationToken ct)
    {
        if (_offline is null) throw new InvalidOperationException("Parakeet model not loaded");
        await _gate.WaitAsync(ct);
        try
        {
            var recognizer = _offline;
            if (recognizer is null) return "";
            return await Task.Run(() => DecodeOffline(recognizer, pcm), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> TryTranscribePartialAsync(float[] pcm, CancellationToken ct)
    {
        if (_offline is null) return null;
        if (!await _gate.WaitAsync(0, ct)) return null; // adaptive skipping: busy → skip this chunk
        try
        {
            var recognizer = _offline;
            if (recognizer is null) return null;
            return await Task.Run(() => DecodeOffline(recognizer, pcm), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string DecodeOffline(OfflineRecognizer recognizer, float[] pcm)
    {
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, pcm);
        recognizer.Decode(stream);
        return stream.Result.Text.Trim();
    }

    public IStreamingPartialSession? TryBeginStreamingSession()
    {
        var recognizer = _streaming;
        return recognizer is null ? null : new StreamingSession(recognizer);
    }

    public void Unload()
    {
        _gate.Wait();
        try { UnloadLocked(); }
        finally { _gate.Release(); }
    }

    private void UnloadLocked()
    {
        _offline?.Dispose();
        _offline = null;
        _streaming?.Dispose();
        _streaming = null;
        _loadedModelId = null;
    }

    public void Dispose()
    {
        try { Unload(); } catch { }
        _gate.Dispose();
    }

    /// <summary>
    /// One recording's online-recognizer stream. Endpoints commit finished segments into
    /// _committed so text survives the recognizer reset; the current segment is appended live.
    /// The preview model emits uncased text — the final transcript (Parakeet) replaces it at stop.
    /// </summary>
    private sealed class StreamingSession : IStreamingPartialSession
    {
        private readonly OnlineRecognizer _recognizer;
        private readonly OnlineStream _stream;
        private readonly StringBuilder _committed = new();
        private bool _dead;

        public StreamingSession(OnlineRecognizer recognizer)
        {
            _recognizer = recognizer;
            _stream = recognizer.CreateStream();
        }

        public string Feed(float[] newSamples)
        {
            if (_dead) return Current("");
            try
            {
                if (newSamples.Length > 0) _stream.AcceptWaveform(16000, newSamples);
                while (_recognizer.IsReady(_stream)) _recognizer.Decode(_stream);
                var segment = NormalizeCase(_recognizer.GetResult(_stream).Text.Trim());
                if (_recognizer.IsEndpoint(_stream))
                {
                    if (segment.Length > 0)
                    {
                        if (_committed.Length > 0) _committed.Append(' ');
                        _committed.Append(segment);
                        segment = "";
                    }
                    _recognizer.Reset(_stream);
                }
                return Current(segment);
            }
            catch (Exception ex)
            {
                // native stream died (e.g. model unloaded mid-recording): go inert, keep last text
                _dead = true;
                Log.Warn("parakeet", $"Streaming partial session failed: {ex.Message}");
                return Current("");
            }
        }

        private string Current(string segment)
            => segment.Length == 0 ? _committed.ToString() : $"{_committed}{(_committed.Length > 0 ? " " : "")}{segment}";

        /// <summary>The zipformer preview model emits ALL-CAPS tokens; lowercase them so the
        /// formatter's sentence casing applies (finals come from Parakeet with real casing).</summary>
        private static string NormalizeCase(string text)
        {
            if (text.Length == 0) return text;
            int letters = 0, upper = 0;
            foreach (var c in text)
            {
                if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
            }
            return letters > 0 && upper >= letters * 0.8 ? text.ToLowerInvariant() : text;
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { }
        }
    }
}
