using FluidVoice.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FluidVoice.Audio;

/// <summary>
/// Captures a meeting: system audio (WASAPI loopback on the default render device — hears every
/// participant) plus, optionally, the microphone (hears you), each resampled to 16 kHz mono and
/// summed into one channel. The growing mixed buffer is transcribed in chunks by MeetingService.
/// </summary>
public sealed class MeetingRecorder : IDisposable
{
    public const int Rate = 16_000;

    private readonly object _sync = new();
    private readonly List<float> _sys = new(Rate * 300);
    private readonly List<float> _mic = new(Rate * 300);
    private readonly Resampler _sysRs = new();
    private readonly Resampler _micRs = new();
    private readonly bool _includeMic;
    private WasapiLoopbackCapture? _sysCap;
    private WasapiCapture? _micCap;
    private volatile bool _recording;

    public MeetingRecorder(bool includeMic) => _includeMic = includeMic;

    public bool IsRecording => _recording;

    /// <summary>Length (samples) of the mixed 16 kHz timeline captured so far.</summary>
    public int MixedLength
    {
        get { lock (_sync) return Math.Max(_sys.Count, _mic.Count); }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds(MixedLength / (double)Rate);

    public void Start()
    {
        lock (_sync)
        {
            if (_recording) return;
            _sys.Clear();
            _mic.Clear();
        }

        // System audio (loopback captures whatever is playing — the other participants).
        var sys = new WasapiLoopbackCapture();
        sys.DataAvailable += (_, e) => OnData(e, sys.WaveFormat, _sys, _sysRs);
        sys.RecordingStopped += (_, e) =>
        {
            if (_recording && e.Exception is not null)
                Log.Warn("meeting", $"System-audio capture stopped: {e.Exception.Message}");
        };
        sys.StartRecording();
        _sysCap = sys;

        // Microphone (captures you). Optional and non-fatal if unavailable.
        if (_includeMic)
        {
            try
            {
                var mic = new WasapiCapture(); // default communications/mic device
                mic.DataAvailable += (_, e) => OnData(e, mic.WaveFormat, _mic, _micRs);
                mic.RecordingStopped += (_, e) =>
                {
                    if (_recording && e.Exception is not null)
                        Log.Warn("meeting", $"Mic capture stopped: {e.Exception.Message}");
                };
                mic.StartRecording();
                _micCap = mic;
            }
            catch (Exception ex)
            {
                Log.Warn("meeting", $"Mic capture unavailable (system audio only): {ex.Message}");
            }
        }

        _recording = true;
        Log.Info("meeting", $"Meeting capture started (system audio{(_micCap is not null ? " + mic" : "")}).");
    }

    private void OnData(WaveInEventArgs e, WaveFormat fmt, List<float> target, Resampler rs)
    {
        if (!_recording || e.BytesRecorded == 0) return;
        var mono = ToMonoFloats(e.Buffer, e.BytesRecorded, fmt);
        if (mono.Length == 0) return;
        var resampled = rs.Process(mono, fmt.SampleRate, Rate);
        lock (_sync) if (_recording) target.AddRange(resampled);
    }

    /// <summary>Mixed 16 kHz mono samples from <paramref name="cursor"/> to the current end.</summary>
    public float[] SnapshotMixedFrom(int cursor)
    {
        lock (_sync)
        {
            int end = Math.Max(_sys.Count, _mic.Count);
            if (cursor < 0) cursor = 0;
            if (cursor >= end) return Array.Empty<float>();
            var outp = new float[end - cursor];
            for (int i = 0; i < outp.Length; i++)
            {
                int idx = cursor + i;
                float a = idx < _sys.Count ? _sys[idx] : 0f;
                float b = idx < _mic.Count ? _mic[idx] : 0f;
                float v = a + b;
                outp[i] = v > 1f ? 1f : (v < -1f ? -1f : v);
            }
            return outp;
        }
    }

    public void Stop()
    {
        _recording = false;
        try { _sysCap?.StopRecording(); } catch { }
        try { _sysCap?.Dispose(); } catch { }
        try { _micCap?.StopRecording(); } catch { }
        try { _micCap?.Dispose(); } catch { }
        _sysCap = null;
        _micCap = null;
    }

    public void Dispose() => Stop();

    private static float[] ToMonoFloats(byte[] buffer, int bytes, WaveFormat fmt)
    {
        int ch = Math.Max(1, fmt.Channels);
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (fmt.Encoding == WaveFormatEncoding.Extensible && fmt.BitsPerSample == 32);
        if (isFloat)
        {
            int frames = bytes / 4 / ch;
            var o = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += BitConverter.ToSingle(buffer, (i * ch + c) * 4);
                o[i] = sum / ch;
            }
            return o;
        }
        if (fmt.BitsPerSample == 16)
        {
            int frames = bytes / 2 / ch;
            var o = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < ch; c++) sum += BitConverter.ToInt16(buffer, (i * ch + c) * 2);
                o[i] = sum / (float)ch / 32768f;
            }
            return o;
        }
        if (fmt.BitsPerSample == 24)
        {
            int frames = bytes / 3 / ch;
            var o = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    int off = (i * ch + c) * 3;
                    sum += buffer[off] | (buffer[off + 1] << 8) | ((sbyte)buffer[off + 2] << 16);
                }
                o[i] = sum / (float)ch / 8388608f;
            }
            return o;
        }
        return Array.Empty<float>();
    }

    /// <summary>Stateful linear resampler (continuous across capture callbacks), one per stream.</summary>
    private sealed class Resampler
    {
        private double _pos;
        private float _prev;
        private bool _has;

        public float[] Process(float[] src, double srcRate, int dstRate)
        {
            if (src.Length == 0) return src;
            if (Math.Abs(srcRate - dstRate) < 0.5) return src;
            double step = srcRate / dstRate;
            var outp = new List<float>((int)(src.Length / step) + 2);
            double pos = _pos;
            float prev = _has ? _prev : src[0];
            while (pos < src.Length)
            {
                int idx = (int)pos;
                double frac = pos - idx;
                float s0 = idx == 0 ? prev : src[idx - 1];
                float s1 = src[idx];
                outp.Add((float)(s0 + (s1 - s0) * frac));
                pos += step;
            }
            _pos = pos - src.Length;
            _prev = src[^1];
            _has = true;
            return outp.ToArray();
        }
    }
}
