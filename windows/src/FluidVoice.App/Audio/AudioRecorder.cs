using FluidVoice.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FluidVoice.Audio;

/// <summary>
/// WASAPI microphone capture → 16 kHz mono float32 (what every STT model expects).
/// Port of the ASRService audio pipeline: stateful linear resampler, RMS level with
/// noise gate 0.002, dB normalize (dB+55)/55, smoothing 0.7*new+0.3*history(2),
/// display silence threshold 0.04 (ASRService.swift:3842-3933).
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    public const int TargetSampleRate = 16_000;

    /// <summary>Normalized 0..1 mic level for the overlay waveform (already smoothed/gated).</summary>
    public event Action<float>? LevelChanged;
    /// <summary>Raised when capture dies unexpectedly (device unplugged etc.).</summary>
    public event Action<string>? CaptureFailed;

    private readonly object _sync = new();
    private readonly List<float> _buffer = new(TargetSampleRate * 120);
    private WasapiCapture? _capture;
    private volatile bool _recording;

    // stateful linear resampler (mirrors resampleTo16kLocked)
    private double _resamplePos;
    private float _resamplePrev;
    private bool _resampleHasPrev;
    private double _sourceRate;

    // level smoothing history (size 2, factor 0.7)
    private readonly Queue<float> _levelHistory = new(2);

    public bool IsRecording => _recording;

    public int SampleCount
    {
        get { lock (_sync) return _buffer.Count; }
    }

    public TimeSpan RecordedDuration => TimeSpan.FromSeconds((double)SampleCount / TargetSampleRate);

    public static IReadOnlyList<(string Id, string Name, bool IsDefault)> ListInputDevices()
    {
        var result = new List<(string, string, bool)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultId = def.ID;
            }
            catch { /* no default mic */ }
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                result.Add((dev.ID, dev.FriendlyName, dev.ID == defaultId));
                dev.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("audio", "Failed to enumerate input devices", ex);
        }
        return result;
    }

    /// <summary>
    /// Test seam: when FLUIDVOICE_TEST_AUDIO points to a WAV, capture is simulated by
    /// streaming that file's 16k-mono samples into the buffer in ~100ms chunks, so the
    /// full pipeline (partials → stop → transcribe → type) runs deterministically without a mic.
    /// </summary>
    private System.Threading.Timer? _testFeedTimer;

    public void Start(string? preferredDeviceId)
    {
        var testAudio = Environment.GetEnvironmentVariable("FLUIDVOICE_TEST_AUDIO");
        if (!string.IsNullOrEmpty(testAudio) && File.Exists(testAudio))
        {
            StartTestFeed(testAudio);
            return;
        }
        lock (_sync)
        {
            if (_recording) return;
            _buffer.Clear();
            _resamplePos = 0;
            _resamplePrev = 0;
            _resampleHasPrev = false;
            _levelHistory.Clear();

            MMDevice? device = null;
            var enumerator = new MMDeviceEnumerator();
            try
            {
                if (!string.IsNullOrEmpty(preferredDeviceId))
                {
                    try { device = enumerator.GetDevice(preferredDeviceId); }
                    catch { Log.Warn("audio", $"Preferred device {preferredDeviceId} unavailable; falling back to default"); }
                }
                device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            finally
            {
                enumerator.Dispose();
            }

            var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20);
            _sourceRate = capture.WaveFormat.SampleRate;
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) =>
            {
                if (_recording && e.Exception is not null)
                {
                    Log.Error("audio", "Capture stopped unexpectedly", e.Exception);
                    CaptureFailed?.Invoke(e.Exception.Message);
                }
            };
            capture.StartRecording();
            _capture = capture;
            _recording = true;
            Log.Info("audio", $"Recording started: {device.FriendlyName} @ {capture.WaveFormat.SampleRate}Hz {capture.WaveFormat.Channels}ch {capture.WaveFormat.Encoding}");
        }
    }

    private void StartTestFeed(string wavPath)
    {
        lock (_sync)
        {
            if (_recording) return;
            _buffer.Clear();
            _levelHistory.Clear();
            _recording = true;
        }
        Log.Info("audio", $"TEST FEED from {wavPath}");
        var samples = LoadWav16kMono(wavPath);
        int cursor = 0;
        const int chunk = 1600; // 100ms @16k
        _testFeedTimer = new System.Threading.Timer(_ =>
        {
            if (!_recording) return;
            int take = Math.Min(chunk, samples.Length - cursor);
            if (take <= 0) return;
            var slice = new float[take];
            Array.Copy(samples, cursor, slice, 0, take);
            cursor += take;
            lock (_sync) { if (_recording) _buffer.AddRange(slice); }
            double sumSq = 0;
            foreach (var s in slice) sumSq += s * s;
            LevelChanged?.Invoke(PushLevel((float)Math.Min(1.0, Math.Sqrt(sumSq / take) * 4)));
        }, null, 0, 100);
    }

    private static float[] LoadWav16kMono(string path)
    {
        using var reader = new AudioFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var srcRate = reader.WaveFormat.SampleRate;
        var all = new List<float>();
        var buf = new float[srcRate * channels];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels && i + c < read; c++) sum += buf[i + c];
                all.Add(sum / channels);
            }
        if (srcRate == TargetSampleRate) return all.ToArray();
        double ratio = srcRate / (double)TargetSampleRate;
        int outLen = (int)(all.Count / ratio);
        var outBuf = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double pos = i * ratio;
            int i0 = (int)pos;
            float frac = (float)(pos - i0);
            outBuf[i] = all[Math.Min(i0, all.Count - 1)] * (1 - frac) + all[Math.Min(i0 + 1, all.Count - 1)] * frac;
        }
        return outBuf;
    }

    /// <summary>Stops capture and returns the full 16k mono buffer.</summary>
    public float[] Stop()
    {
        if (_testFeedTimer is not null)
        {
            _testFeedTimer.Dispose();
            _testFeedTimer = null;
            lock (_sync)
            {
                _recording = false;
                var pcmTest = _buffer.ToArray();
                _buffer.Clear();
                return pcmTest;
            }
        }
        WasapiCapture? capture;
        lock (_sync)
        {
            _recording = false;
            capture = _capture;
            _capture = null;
        }
        if (capture is not null)
        {
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
        }
        lock (_sync)
        {
            var pcm = _buffer.ToArray();
            _buffer.Clear();
            return pcm;
        }
    }

    /// <summary>Thread-safe copy of everything captured so far (for streaming partials).</summary>
    public float[] SnapshotAll()
    {
        lock (_sync) return _buffer.ToArray();
    }

    /// <summary>Thread-safe copy of samples from <paramref name="start"/> to the end — lets the
    /// true-streaming partial loop feed only the samples it hasn't seen yet.</summary>
    public float[] SnapshotFrom(int start)
    {
        lock (_sync)
        {
            if (start >= _buffer.Count) return Array.Empty<float>();
            var count = _buffer.Count - start;
            var result = new float[count];
            _buffer.CopyTo(start, result, 0, count);
            return result;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!_recording || e.BytesRecorded == 0) return;
        var capture = _capture;
        if (capture is null) return;
        var fmt = capture.WaveFormat;

        var mono = ToMonoFloats(e.Buffer, e.BytesRecorded, fmt);
        if (mono.Length == 0) return;

        var resampled = Resample(mono, _sourceRate);
        float level = ComputeLevel(mono);

        lock (_sync)
        {
            if (_recording) _buffer.AddRange(resampled);
        }
        LevelChanged?.Invoke(level);
    }

    private static float[] ToMonoFloats(byte[] buffer, int bytes, WaveFormat fmt)
    {
        int channels = fmt.Channels;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
            (fmt is WaveFormatExtensible wfx && wfx.SubFormat == AudioSubtypes.MEDIASUBTYPE_IEEE_FLOAT) ||
            (fmt.Encoding == WaveFormatEncoding.Extensible && fmt.BitsPerSample == 32))
        {
            int frames = bytes / 4 / channels;
            var outBuf = new float[frames];
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    var f = (float*)p;
                    for (int i = 0; i < frames; i++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++) sum += f[i * channels + c];
                        outBuf[i] = sum / channels;
                    }
                }
            }
            return outBuf;
        }
        if (fmt.BitsPerSample == 16)
        {
            int frames = bytes / 2 / channels;
            var outBuf = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToInt16(buffer, (i * channels + c) * 2);
                outBuf[i] = sum / (float)channels / 32768f;
            }
            return outBuf;
        }
        if (fmt.BitsPerSample == 24)
        {
            int frames = bytes / 3 / channels;
            var outBuf = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int off = (i * channels + c) * 3;
                    int v = buffer[off] | (buffer[off + 1] << 8) | ((sbyte)buffer[off + 2] << 16);
                    sum += v;
                }
                outBuf[i] = sum / (float)channels / 8388608f;
            }
            return outBuf;
        }
        return Array.Empty<float>();
    }

    private float[] Resample(float[] src, double sourceRate)
    {
        if (Math.Abs(sourceRate - TargetSampleRate) < 0.5) return src;
        double step = sourceRate / TargetSampleRate;
        var output = new List<float>((int)(src.Length / step) + 2);

        double pos = _resamplePos;
        float prev = _resampleHasPrev ? _resamplePrev : (src.Length > 0 ? src[0] : 0f);

        while (pos < src.Length)
        {
            int idx = (int)pos;
            double frac = pos - idx;
            float s0 = idx == 0 ? prev : src[idx - 1];
            float s1 = src[idx];
            // interpolate between previous sample and current (continuous across callbacks)
            output.Add((float)(s0 + (s1 - s0) * frac));
            pos += step;
        }
        _resamplePos = pos - src.Length;
        _resamplePrev = src[^1];
        _resampleHasPrev = true;
        return output.ToArray();
    }

    private float ComputeLevel(float[] samples)
    {
        double sumSq = 0;
        for (int i = 0; i < samples.Length; i++) sumSq += samples[i] * samples[i];
        var rms = (float)Math.Sqrt(sumSq / Math.Max(1, samples.Length));
        if (rms < 0.002f) return PushLevel(0f);
        var db = 20.0 * Math.Log10(Math.Max(rms, 1e-10));
        var normalized = Math.Clamp((db + 55.0) / 55.0, 0.0, 1.0);

        return PushLevel((float)normalized);
    }

    private float PushLevel(float newLevel)
    {
        float avg;
        lock (_levelHistory)
        {
            avg = _levelHistory.Count > 0 ? _levelHistory.Average() : newLevel;
            _levelHistory.Enqueue(newLevel);
            while (_levelHistory.Count > 2) _levelHistory.Dequeue();
        }
        var smoothed = 0.7f * newLevel + 0.3f * avg;
        return smoothed < 0.04f ? 0f : smoothed;
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
    }
}

internal static class AudioSubtypes
{
    public static readonly Guid MEDIASUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00aa00389b71");
}
