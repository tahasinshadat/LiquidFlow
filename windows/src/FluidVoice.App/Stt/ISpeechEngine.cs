namespace FluidVoice.Stt;

/// <summary>
/// Common surface of the STT engines (WhisperEngine, ParakeetEngine) so the
/// coordinator/UI/selftest don't care which runtime executes the selected model.
/// </summary>
public interface ISpeechEngine : IDisposable
{
    bool IsReady { get; }
    string? LoadedModelId { get; }

    /// <summary>Ensure the model is downloaded and loaded into memory.</summary>
    Task PrepareAsync(SpeechModelInfo model, IProgress<ModelPreparationProgress>? progress, CancellationToken ct);

    /// <summary>Final batch transcription of 16 kHz mono float PCM.</summary>
    Task<string> TranscribeAsync(float[] pcm, CancellationToken ct);

    /// <summary>Best-effort partial decode of the accumulated buffer; null when busy (adaptive skipping).</summary>
    Task<string?> TryTranscribePartialAsync(float[] pcm, CancellationToken ct);

    /// <summary>
    /// Start a true streaming partial session (Parakeet's online recognizer), or null if the
    /// engine can only batch-decode (Whisper) — callers then fall back to periodic re-decode
    /// via <see cref="TryTranscribePartialAsync"/>.
    /// </summary>
    IStreamingPartialSession? TryBeginStreamingSession();

    void Unload();
}

/// <summary>
/// One recording's worth of true streaming recognition. Single-threaded: the partial
/// loop feeds newly captured samples and renders the returned text. Dispose on stop.
/// </summary>
public interface IStreamingPartialSession : IDisposable
{
    /// <summary>Feed newly captured 16 kHz mono samples; returns the full partial text so far.</summary>
    string Feed(float[] newSamples);
}
