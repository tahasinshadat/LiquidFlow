using System.IO;
using System.Windows.Threading;
using FluidVoice.Ai;
using FluidVoice.Audio;
using FluidVoice.Core;
using FluidVoice.Input;
using FluidVoice.Stt;
using FluidVoice.Text;
using FluidVoice.Typing;
using FluidVoice.Ui;

namespace FluidVoice.App;

/// <summary>
/// Orchestrates the dictation loop (port of ASRService + ContentView glue):
/// hotkey → capture focus + record → live partials → stop → pad to 1s →
/// transcribe → format pipeline → optional AI enhancement → type into the
/// captured app → history. Command/Rewrite recordings are routed to their services.
/// Two engines share the loop: Whisper (batch; partials = periodic re-decode) and
/// Parakeet (sherpa-onnx; partials = true streaming session fed incrementally).
/// </summary>
public sealed class DictationCoordinator : IDictationControl
{
    private readonly Dispatcher _dispatcher;
    private readonly AudioRecorder _recorder = new();
    private readonly WhisperEngine _whisper = new();
    private ParakeetEngine? _parakeet; // lazy: only allocated once a Parakeet model is used
    private readonly OverlayWindow _overlay;

    private volatile RecordingMode _activeMode = RecordingMode.None;
    private int _processingStop;
    private CancellationTokenSource? _partialCts;
    private Task? _partialTask;
    private FocusSnapshot? _focusAtStart;
    private string _lastPartial = "";
    private long _sessionId;

    public event Action<bool>? RecordingStateChanged; // for tray icon tint
    public event Action<string>? StatusChanged;

    /// <summary>Set by Program: routes a finished command-mode transcript to CommandModeService.</summary>
    public Func<string, Task>? CommandModeHandler;
    /// <summary>Set by Program: routes a finished rewrite-mode transcript to RewriteModeService.</summary>
    public Func<string, FocusSnapshot?, Task>? RewriteModeHandler;

    public RecordingMode ActiveMode => _activeMode;
    public bool IsProcessingStop => Volatile.Read(ref _processingStop) == 1;
    public AudioRecorder Recorder => _recorder;

    /// <summary>The engine that runs the currently selected speech model.</summary>
    public ISpeechEngine CurrentEngine => EngineFor(SpeechModels.Selected());

    public ISpeechEngine EngineFor(SpeechModelInfo model) => model.Engine switch
    {
        SpeechEngineKind.Parakeet => _parakeet ??= new ParakeetEngine(),
        _ => _whisper,
    };

    public DictationCoordinator(Dispatcher dispatcher, OverlayWindow overlay)
    {
        _dispatcher = dispatcher;
        _overlay = overlay;
        _recorder.LevelChanged += level => _overlay.Dispatcher.BeginInvoke(() => _overlay.SetLevel(level));
    }

    /// <summary>Prepare the model and free the other engine family's memory when switching.</summary>
    public async Task<ISpeechEngine> EnsureEngineReadyAsync(SpeechModelInfo model, IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        var engine = EngineFor(model);
        if (!ReferenceEquals(engine, _whisper) && _whisper.IsReady) _whisper.Unload();
        if (_parakeet is not null && !ReferenceEquals(engine, _parakeet) && _parakeet.IsReady) _parakeet.Unload();
        await engine.PrepareAsync(model, progress, ct);
        return engine;
    }

    /// <summary>Preload the selected model at app start so the first dictation is instant.</summary>
    public void WarmUpModelInBackground()
    {
        var model = SpeechModels.Selected();
        if (!model.IsDownloaded) return; // don't auto-download at startup; onboarding/settings handles that
        _ = Task.Run(async () =>
        {
            try { await EnsureEngineReadyAsync(model, null, CancellationToken.None); }
            catch (Exception ex) { Log.Error("coordinator", "Model warmup failed", ex); }
        });
    }

    // ---- IDictationControl (called from the hook thread; must not block) ----

    public void RequestStart(RecordingMode mode) => _dispatcher.BeginInvoke(() => StartOrSwitch(mode));
    public void RequestStopAndProcess() => _dispatcher.BeginInvoke(() => _ = StopAndProcessAsync());
    public void RequestCancel() => _dispatcher.BeginInvoke(CancelRecording);
    public void RequestPasteLast() => _dispatcher.BeginInvoke(() =>
    {
        var text = TypingService.LastTypedText;
        if (string.IsNullOrEmpty(text)) return;
        var target = FocusTracker.Capture();
        Task.Run(() => TypingService.TypeTextInstantly(text, target));
    });

    // ---- lifecycle ----

    private void StartOrSwitch(RecordingMode mode)
    {
        if (_activeMode == mode) return;
        if (_activeMode != RecordingMode.None)
        {
            // in-flight mode switch: recording continues, target mode changes (GlobalHotkeyManager.swift:568-577)
            _activeMode = mode;
            _overlay.SetMode(mode);
            Log.Info("coordinator", $"Mode switched in-flight → {mode}");
            return;
        }

        try
        {
            _focusAtStart = FocusTracker.Capture();
            _sessionId++;
            _lastPartial = "";
            _recorder.Start(Settings.Current.PreferredInputDeviceId);
        }
        catch (Exception ex)
        {
            Log.Error("coordinator", "Failed to start recording", ex);
            StatusChanged?.Invoke("Microphone unavailable");
            return;
        }

        _activeMode = mode;
        SoundCues.PlayStart();
        MediaPauseService.PauseIfPlaying();
        _overlay.ShowRecording(mode);
        if (_focusAtStart is not null) _overlay.SetTargetApp(_focusAtStart.ProcessId);
        RecordingStateChanged?.Invoke(true);

        var engine = CurrentEngine;
        if (Settings.Current.EnableStreamingPreview && engine.IsReady)
            StartPartialLoop(engine, _sessionId);

        // lazily prepare the model while the user is speaking (mac: ensureAsrReady at stop)
        if (!engine.IsReady)
        {
            var model = SpeechModels.Selected();
            if (model.IsDownloaded)
                _ = Task.Run(() => EnsureEngineReadyAsync(model, null, CancellationToken.None));
        }
        Log.Info("coordinator", $"Recording started in {mode} mode (app: {_focusAtStart?.ProcessName})");
    }

    /// <summary>
    /// Live preview. Parakeet: a true streaming session — only new samples are fed each tick,
    /// so latency stays constant for arbitrarily long recordings. Whisper (or a Parakeet whose
    /// preview model failed to load): re-decode the accumulated buffer periodically (0.6s tick,
    /// min 1s of audio, adaptive skipping — SettingsStore.swift:4160-4186).
    /// </summary>
    private void StartPartialLoop(ISpeechEngine engine, long session)
    {
        _partialCts = new CancellationTokenSource();
        var ct = _partialCts.Token;
        _partialTask = Task.Run(async () =>
        {
            var streamingSession = engine.TryBeginStreamingSession();
            try
            {
                if (streamingSession is not null)
                    await RunStreamingPartialsAsync(streamingSession, session, ct);
                else
                    await RunRedecodePartialsAsync(engine, session, ct);
            }
            finally
            {
                streamingSession?.Dispose();
            }
        }, ct);
    }

    private async Task RunStreamingPartialsAsync(IStreamingPartialSession stream, long session, CancellationToken ct)
    {
        int cursor = 0;
        while (!ct.IsCancellationRequested && _activeMode != RecordingMode.None && session == _sessionId)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.2), ct).ContinueWith(_ => { });
            if (ct.IsCancellationRequested || _activeMode == RecordingMode.None) break;
            var fresh = _recorder.SnapshotFrom(cursor);
            if (fresh.Length == 0) continue;
            cursor += fresh.Length;
            var partial = stream.Feed(fresh);
            if (ct.IsCancellationRequested) break;
            EmitPartial(partial);
        }
    }

    private async Task RunRedecodePartialsAsync(ISpeechEngine engine, long session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _activeMode != RecordingMode.None && session == _sessionId)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.6), ct).ContinueWith(_ => { });
            if (ct.IsCancellationRequested || _activeMode == RecordingMode.None) break;
            var samples = _recorder.SnapshotAll();
            if (samples.Length < AudioRecorder.TargetSampleRate) continue; // min 1s
            // cap the live-preview decode window so stop latency stays bounded;
            // the preview shows the tail of the text anyway
            const int maxPreviewSamples = 25 * AudioRecorder.TargetSampleRate;
            if (samples.Length > maxPreviewSamples)
                samples = samples[^maxPreviewSamples..];
            var partial = await engine.TryTranscribePartialAsync(samples, ct);
            if (partial is null || ct.IsCancellationRequested) continue;
            EmitPartial(partial);
        }
    }

    private void EmitPartial(string rawPartial)
    {
        var formatted = TranscriptFormatter.Process(rawPartial, _focusAtStart?.ProcessName, _focusAtStart?.WindowTitle);
        var stable = SmartDiff(_lastPartial, formatted);
        _lastPartial = stable;
        _ = _overlay.Dispatcher.BeginInvoke(() => _overlay.SetPreviewText(stable));
    }

    /// <summary>Keep the stable common prefix so live text doesn't flicker (ASRService.swift:3228-3293).</summary>
    private static string SmartDiff(string previous, string current)
    {
        if (previous.Length == 0) return current;
        if (current.Length >= previous.Length) return current;
        // shorter re-decode: keep previous until the new one catches up, unless it diverges early
        int common = 0;
        int max = Math.Min(previous.Length, current.Length);
        while (common < max && previous[common] == current[common]) common++;
        return common >= previous.Length * 3 / 4 ? current : previous;
    }

    private async Task StopAndProcessAsync()
    {
        if (_activeMode == RecordingMode.None) return;
        if (Interlocked.Exchange(ref _processingStop, 1) == 1) return;

        var mode = _activeMode;
        var focus = _focusAtStart;
        try
        {
            _activeMode = RecordingMode.None;
            RecordingStateChanged?.Invoke(false);

            // 1) stop capture immediately, play the stop cue without waiting (ASRService.swift stop())
            var pcm = _recorder.Stop();
            SoundCues.PlayStop();
            MediaPauseService.ResumeIfWePaused();

            // 2) await the partial loop before touching state it uses
            _partialCts?.Cancel();
            if (_partialTask is not null)
            {
                try { await _partialTask; } catch { }
            }
            _partialTask = null;

            _overlay.ShowProcessing("Transcribing");
            StatusChanged?.Invoke("Transcribing");

            if (pcm.Length == 0)
            {
                _overlay.HideOverlay();
                return;
            }

            // 3) pad to at least 1s (whisper.cpp asserts on shorter input; harmless for parakeet;
            //    ASRService.swift:1430-1437)
            if (pcm.Length < AudioRecorder.TargetSampleRate)
            {
                var padded = new float[AudioRecorder.TargetSampleRate];
                Array.Copy(pcm, padded, pcm.Length);
                pcm = padded;
            }

            // 4) ensure model ready (downloads on first use)
            var model = SpeechModels.Selected();
            var engine = EngineFor(model);
            if (!engine.IsReady)
            {
                var progress = new Progress<ModelPreparationProgress>(p =>
                {
                    if (p.Phase == ModelPreparationPhase.Downloading)
                        _overlay.Dispatcher.BeginInvoke(() =>
                            _overlay.ShowProcessing($"Downloading model {(int)(p.Fraction * 100)}%"));
                });
                engine = await EnsureEngineReadyAsync(model, progress, CancellationToken.None);
                _ = _overlay.Dispatcher.BeginInvoke(() => _overlay.ShowProcessing("Transcribing"));
            }

            // 5) final transcription + local formatting pipeline
            var raw = await engine.TranscribeAsync(pcm, CancellationToken.None);
            var formatted = TranscriptFormatter.Process(raw, focus?.ProcessName, focus?.WindowTitle);

            if (string.IsNullOrWhiteSpace(formatted))
            {
                _overlay.HideOverlay();
                StatusChanged?.Invoke("Ready");
                return;
            }

            switch (mode)
            {
                case RecordingMode.Command when CommandModeHandler is not null:
                    _overlay.HideOverlay();
                    await CommandModeHandler(formatted);
                    break;

                case RecordingMode.Rewrite when RewriteModeHandler is not null:
                    _overlay.HideOverlay();
                    await RewriteModeHandler(formatted, focus);
                    break;

                default:
                    await DeliverDictationAsync(mode, raw, formatted, focus, pcm);
                    break;
            }
            StatusChanged?.Invoke("Ready");
        }
        catch (Exception ex)
        {
            Log.Error("coordinator", "Stop/process failed", ex);
            _overlay.HideOverlay();
            StatusChanged?.Invoke("Error");
        }
        finally
        {
            Volatile.Write(ref _processingStop, 0);
            if (_activeMode == RecordingMode.None)
                _ = _overlay.Dispatcher.BeginInvoke(() => { if (ActiveMode == RecordingMode.None) _overlay.HideOverlay(); });
        }
    }

    private async Task DeliverDictationAsync(RecordingMode mode, string raw, string formatted, FocusSnapshot? focus, float[] pcm)
    {
        string finalText = formatted;
        bool aiProcessed = false;
        string? aiModel = null;
        string? aiError = null;

        if (EnhancementService.IsConfiguredForDictation(focus?.ProcessName))
        {
            _overlay.ShowProcessing("Refining...");
            try
            {
                var enhanced = await EnhancementService.EnhanceDictationAsync(formatted, focus?.ProcessName, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(enhanced))
                {
                    finalText = TranscriptFormatter.ApplyGaavFormatting(enhanced.Trim());
                    aiProcessed = true;
                    aiModel = EnhancementService.LastUsedModelDescription;
                }
            }
            catch (Exception ex)
            {
                aiError = ex.Message;
                Log.Warn("coordinator", $"AI enhancement failed, typing raw transcription: {ex.Message}");
                Notifications.NotifyAiFallback(ex.Message);
            }
        }

        _overlay.HideOverlay();

        var target = focus;
        var typed = await Task.Run(() => TypingService.TypeTextInstantly(finalText, target));
        if (!typed) Log.Warn("coordinator", "All insertion strategies failed");

        var entry = new TranscriptionHistoryEntry
        {
            RawText = raw,
            ProcessedText = finalText,
            AppName = focus?.ProcessName ?? "",
            WindowTitle = focus?.WindowTitle ?? "",
            WasAIProcessed = aiProcessed,
            ProcessingModel = aiModel,
            AiProcessingError = aiError,
        };
        if (Settings.Current.SaveAudioWithTranscriptionHistory)
            entry.Audio = AudioHistoryStore.SaveAudio(pcm, SpeechModels.Selected().Id);
        HistoryStore.AddEntry(entry);
    }

    private void CancelRecording()
    {
        if (_activeMode == RecordingMode.None) return;
        Log.Info("coordinator", "Recording cancelled");
        _activeMode = RecordingMode.None;
        _partialCts?.Cancel();
        _recorder.Stop();
        SoundCues.PlayStop();
        MediaPauseService.ResumeIfWePaused();
        RecordingStateChanged?.Invoke(false);
        _overlay.HideOverlay();
        StatusChanged?.Invoke("Ready");
    }
}
