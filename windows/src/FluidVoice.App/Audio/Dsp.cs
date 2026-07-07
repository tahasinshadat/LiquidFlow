namespace FluidVoice.Audio;

/// <summary>
/// Pre-decode audio conditioning. Whispered/quiet speech reaches the models at RMS
/// ~0.002-0.01 where recognition collapses; commercial dictation apps run AGC before
/// ASR. We compute a per-utterance gain toward a target RMS (speech-level) with a
/// hard cap, so whispering works without blowing up loud recordings.
/// </summary>
public static class Dsp
{
    public const float TargetRms = 0.06f;   // comfortable speech level for 16-bit-trained ASR
    public const float MaxGain = 30f;       // whisper-quiet input gets up to ~30x
    public const float NoiseFloorRms = 0.0008f; // below this it's silence, not speech — don't amplify hiss

    /// <summary>Gain that would bring this buffer to the target RMS (1.0 = leave alone).</summary>
    public static float GainFor(ReadOnlySpan<float> pcm)
    {
        if (pcm.Length == 0) return 1f;
        double sumSq = 0;
        for (int i = 0; i < pcm.Length; i++) sumSq += pcm[i] * pcm[i];
        var rms = (float)Math.Sqrt(sumSq / pcm.Length);
        if (rms < NoiseFloorRms || rms >= TargetRms) return 1f;
        return Math.Min(MaxGain, TargetRms / rms);
    }

    /// <summary>Returns a gain-normalized copy (or the original array when no gain is needed), with soft clipping.</summary>
    public static float[] Normalize(float[] pcm)
    {
        var gain = GainFor(pcm);
        if (Math.Abs(gain - 1f) < 0.01f) return pcm;
        var output = new float[pcm.Length];
        Scale(pcm, output, gain);
        return output;
    }

    /// <summary>Scales src into dst with tanh soft clipping (keeps boosted peaks from squaring off).</summary>
    public static void Scale(ReadOnlySpan<float> src, Span<float> dst, float gain)
    {
        for (int i = 0; i < src.Length; i++)
        {
            var v = src[i] * gain;
            dst[i] = v is > 1f or < -1f ? MathF.Tanh(v) : v;
        }
    }
}
