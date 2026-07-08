using System.IO;
using FluidVoice.Core;
using FluidVoice.Stt;
using SherpaOnnx;

namespace FluidVoice.Audio;

/// <summary>
/// Silero-VAD trailing-silence auto-stop (ported from OpenWhispr's dictation VAD):
/// while recording, fresh samples are fed to Silero; once the user has spoken and
/// then stays silent for the configured duration, the recording stops by itself.
/// The tiny model (~0.6 MB) downloads in the background when the feature is enabled.
/// If the model isn't available the monitor falls back to an RMS energy gate so the
/// toggle still works offline — just slightly less robust in noisy rooms.
/// </summary>
public sealed class VadAutoStopMonitor : IDisposable
{
    public const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx";
    public const long ModelBytes = 643_854;
    public static string ModelPath => Path.Combine(AppPaths.SherpaModelDir, "vad", "silero_vad.onnx");
    public static bool IsModelInstalled => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 100_000;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;

    private VadAutoStopMonitor(AudioRecorder recorder, Func<bool> stillRecording, Action requestStop)
    {
        _task = Task.Run(() => RunAsync(recorder, stillRecording, requestStop, _cts.Token));
    }

    /// <summary>Starts the monitor for one recording session; never throws.</summary>
    public static VadAutoStopMonitor? TryStart(AudioRecorder recorder, Func<bool> stillRecording, Action requestStop)
    {
        try { return new VadAutoStopMonitor(recorder, stillRecording, requestStop); }
        catch (Exception ex)
        {
            Log.Warn("vad", $"Auto-stop monitor failed to start: {ex.Message}");
            return null;
        }
    }

    /// <summary>Background-download the Silero model (used by the settings toggle).</summary>
    public static Task DownloadModelAsync(IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
        => ModelDownloader.DownloadAsync(ModelUrl, ModelPath, ModelBytes, progress, ct);

    private static async Task RunAsync(AudioRecorder recorder, Func<bool> stillRecording, Action requestStop, CancellationToken ct)
    {
        var silenceWindow = TimeSpan.FromSeconds(Math.Clamp(Settings.Current.VadAutoStopSilenceSeconds, 1.0, 10.0));
        VoiceActivityDetector? vad = null;
        try
        {
            if (IsModelInstalled)
            {
                var cfg = new VadModelConfig
                {
                    SileroVad = new SileroVadModelConfig
                    {
                        Model = ModelPath,
                        Threshold = 0.5f,
                        MinSilenceDuration = 0.4f,
                        MinSpeechDuration = 0.25f,
                        WindowSize = 512,
                        MaxSpeechDuration = 30f,
                    },
                    SampleRate = AudioRecorder.TargetSampleRate,
                    NumThreads = 1,
                    Provider = "cpu",
                };
                vad = new VoiceActivityDetector(cfg, 4f);
            }
            else
            {
                Log.Info("vad", "Silero model not installed — using RMS fallback for auto-stop");
            }

            int cursor = 0;
            float sessionGain = 0; // same whisper-friendly AGC the partial loop uses
            bool everSpoke = false;
            var lastSpeech = DateTime.UtcNow;

            while (!ct.IsCancellationRequested && stillRecording())
            {
                await Task.Delay(150, ct).ContinueWith(_ => { });
                if (ct.IsCancellationRequested || !stillRecording()) break;

                var fresh = recorder.SnapshotFrom(cursor);
                if (fresh.Length == 0) continue;
                cursor += fresh.Length;

                if (sessionGain == 0 && cursor >= AudioRecorder.TargetSampleRate)
                    sessionGain = Dsp.GainFor(recorder.SnapshotAll());
                if (sessionGain > 1f)
                    Dsp.Scale(fresh, fresh, sessionGain);

                bool speechNow;
                if (vad is not null)
                {
                    vad.AcceptWaveform(fresh);
                    speechNow = vad.IsSpeechDetected();
                    while (!vad.IsEmpty()) vad.Pop(); // we only need the live flag, not segments
                }
                else
                {
                    speechNow = Dsp.Rms(fresh) > 0.02f;
                }

                if (speechNow)
                {
                    everSpoke = true;
                    lastSpeech = DateTime.UtcNow;
                }
                else if (everSpoke && DateTime.UtcNow - lastSpeech >= silenceWindow)
                {
                    Log.Info("vad", $"Auto-stop: {silenceWindow.TotalSeconds:0.#}s of silence after speech");
                    requestStop();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("vad", $"Auto-stop monitor error: {ex.Message}");
        }
        finally
        {
            vad?.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
