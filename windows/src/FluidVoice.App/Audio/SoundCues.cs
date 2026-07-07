using FluidVoice.Core;
using NAudio.Wave;

namespace FluidVoice.Audio;

/// <summary>
/// Start/stop dictation cues (parity with TranscriptionSoundPlayer.swift).
/// The mac app ships small m4a chimes; we synthesize equivalent short chirps
/// so no proprietary assets are bundled.
/// </summary>
public static class SoundCues
{
    private static byte[]? _startWav;
    private static byte[]? _stopWav;

    public static void PlayStart() => Play(_startWav ??= Chirp(660, 990, 0.09), nameof(PlayStart));
    public static void PlayStop() => Play(_stopWav ??= Chirp(990, 660, 0.09), nameof(PlayStop));

    private static void Play(byte[] wav, string label)
    {
        if (!Settings.Current.EnableTranscriptionSounds) return;
        try
        {
            var volume = Math.Clamp(Settings.Current.TranscriptionSoundVolume, 0f, 1f);
            var stream = new MemoryStream(wav);
            var reader = new WaveFileReader(stream);
            var output = new WaveOutEvent { DesiredLatency = 80 };
            output.Init(reader);
            output.Volume = volume;
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
                stream.Dispose();
            };
            output.Play();
        }
        catch (Exception ex)
        {
            Log.Warn("sound", $"{label} failed: {ex.Message}");
        }
    }

    /// <summary>Short sine chirp with fade in/out, 16-bit 44.1k WAV in memory.</summary>
    private static byte[] Chirp(double fromHz, double toHz, double seconds)
    {
        const int rate = 44100;
        int n = (int)(rate * seconds);
        var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(rate, 16, 1)))
        {
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double freq = fromHz + (toHz - fromHz) * t;
                phase += 2 * Math.PI * freq / rate;
                double env = Math.Sin(Math.PI * t); // smooth fade in/out
                writer.WriteSample((float)(Math.Sin(phase) * env * 0.35));
            }
        }
        return ms.ToArray();
    }
}
