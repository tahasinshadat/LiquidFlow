using System.Diagnostics;
using System.IO;
using System.Net.Http;
using FluidVoice.Core;
using SharpCompress.Common;
using SharpCompress.Readers;
using SherpaOnnx;

namespace FluidVoice.Ai;

/// <summary>
/// Native ARM64 text-to-speech: Kokoro 82M (int8) via sherpa-onnx — the same preset voices
/// VoiceBox exposes, but with zero x64 emulation and instant startup. Model bundle
/// (kokoro-int8-multi-lang-v1_0, ~126 MB) downloads once from the sherpa-onnx releases.
/// Kokoro model: hexgrad/Kokoro-82M (Apache-2.0); runtime: k2-fsa/sherpa-onnx (Apache-2.0).
/// </summary>
public static class VoiceStudio
{
    private const string BundleName = "kokoro-int8-multi-lang-v1_0";
    private const string BundleUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly object EngineSync = new();
    private static OfflineTts? _engine;

    public static string RootDir => Path.Combine(AppPaths.DataDir, "Voices");
    public static string BundleDir => Path.Combine(RootDir, BundleName);
    public static string OutputDir => Path.Combine(RootDir, "Generated");

    public static bool IsInstalled =>
        Directory.Exists(BundleDir)
        && Directory.EnumerateFiles(BundleDir, "*.onnx").Any()
        && File.Exists(Path.Combine(BundleDir, "voices.bin"))
        && File.Exists(Path.Combine(BundleDir, "tokens.txt"));

    /// <summary>Download + extract the Kokoro bundle with progress (phase, 0..1 or -1).</summary>
    public static async Task DownloadAsync(IProgress<(string Phase, double Pct)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(RootDir);
        var archive = Path.Combine(RootDir, BundleName + ".tar.bz2");

        if (!IsInstalled)
        {
            var tmp = archive + ".part";
            using (var resp = await Http.GetAsync(BundleUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmp);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0)
                        progress.Report(($"Downloading voices… {done / 1048576} / {total / 1048576} MB", (double)done / total));
                }
            }
            File.Move(tmp, archive, overwrite: true);

            progress.Report(("Unpacking voices (bzip2 — takes a minute)…", -1));
            await Task.Run(() =>
            {
                // Manual extraction with a zip-slip guard (GHSA-6c8g-7p36-r338 affects
                // SharpCompress's own WriteEntryToDirectory; we never call it).
                var rootFull = Path.GetFullPath(RootDir) + Path.DirectorySeparatorChar;
                using var stream = File.OpenRead(archive);
                using var reader = ReaderFactory.Open(stream);
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
                    var rel = reader.Entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var dest = Path.GetFullPath(Path.Combine(RootDir, rel));
                    if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var entry = reader.OpenEntryStream();
                    using var outFile = File.Create(dest);
                    entry.CopyTo(outFile);
                }
            }, ct);
            try { File.Delete(archive); } catch { /* keep disk tidy; harmless if locked */ }
        }
        progress.Report(("Voices ready.", 1));
    }

    private static OfflineTts GetEngine()
    {
        lock (EngineSync)
        {
            if (_engine is not null) return _engine;
            var model = Directory.EnumerateFiles(BundleDir, "*.onnx").OrderBy(f => f.Length).First();
            // us-en + zh, per the sherpa-onnx docs (adding gb-en too just spams duplicate-word warnings)
            var lexicons = new[] { "lexicon-us-en.txt", "lexicon-zh.txt" }
                .Select(n => Path.Combine(BundleDir, n)).Where(File.Exists);
            var config = new OfflineTtsConfig();
            config.Model.Kokoro.Model = model;
            config.Model.Kokoro.Voices = Path.Combine(BundleDir, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(BundleDir, "tokens.txt");
            config.Model.Kokoro.DataDir = Path.Combine(BundleDir, "espeak-ng-data");
            config.Model.Kokoro.Lexicon = string.Join(",", lexicons);
            var dict = Path.Combine(BundleDir, "dict");
            if (Directory.Exists(dict)) config.Model.Kokoro.DictDir = dict;
            config.Model.NumThreads = 3; // measured optimum for this memory-bandwidth-bound CPU
            config.Model.Debug = 0;
            config.Model.Provider = "cpu";
            _engine = new OfflineTts(config);
            Log.Info("voices", $"Kokoro TTS engine loaded ({Path.GetFileName(model)})");
            return _engine;
        }
    }

    /// <summary>Synthesize to a WAV file; returns (path, seconds of audio, wall-clock ms).</summary>
    public static Task<(string Path, double Seconds, long Ms)> GenerateAsync(string text, int speakerId, float speed, CancellationToken ct)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var engine = GetEngine();
            var sw = Stopwatch.StartNew();
            var audio = engine.Generate(text, speed, speakerId);
            sw.Stop();
            Directory.CreateDirectory(OutputDir);
            var path = Path.Combine(OutputDir, $"voice-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            audio.SaveToWaveFile(path);
            var seconds = audio.Samples.Length / (double)audio.SampleRate;
            return (path, seconds, sw.ElapsedMilliseconds);
        }, ct);

    /// <summary>The 53 speakers of kokoro-multi-lang-v1_0, id order per sherpa-onnx docs.
    /// (Name, Id, Blurb). Jarvis persona first — Kokoro "George" (bm_george, id 26).</summary>
    public static readonly (string Name, int Id, string Blurb)[] Voices =
    {
        ("Jarvis", 26, "British male — the J.A.R.V.I.S. pick"),
        ("Alloy", 0, "American female"), ("Aoede", 1, "American female"), ("Bella", 2, "American female"),
        ("Heart", 3, "American female"), ("Jessica", 4, "American female"), ("Kore", 5, "American female"),
        ("Nicole", 6, "American female"), ("Nova", 7, "American female"), ("River", 8, "American female"),
        ("Sarah", 9, "American female"), ("Sky", 10, "American female"),
        ("Adam", 11, "American male"), ("Echo", 12, "American male"), ("Eric", 13, "American male"),
        ("Fenrir", 14, "American male"), ("Liam", 15, "American male"), ("Michael", 16, "American male"),
        ("Onyx", 17, "American male"), ("Puck", 18, "American male"), ("Santa", 19, "American male"),
        ("Alice (UK)", 20, "British female"), ("Emma (UK)", 21, "British female"),
        ("Isabella (UK)", 22, "British female"), ("Lily (UK)", 23, "British female"),
        ("Daniel (UK)", 24, "British male"), ("Fable (UK)", 25, "British male"),
        ("George (UK)", 26, "British male"), ("Lewis (UK)", 27, "British male"),
        ("Dora (Spanish)", 28, "Spanish female"), ("Alex (Spanish)", 29, "Spanish male"),
        ("Siwis (French)", 30, "French female"),
        ("Alpha (Hindi)", 31, "Hindi female"), ("Beta (Hindi)", 32, "Hindi female"),
        ("Omega (Hindi)", 33, "Hindi male"), ("Psi (Hindi)", 34, "Hindi male"),
        ("Sara (Italian)", 35, "Italian female"), ("Nicola (Italian)", 36, "Italian male"),
        ("Alpha (Japanese)", 37, "Japanese female"), ("Gongitsune (Japanese)", 38, "Japanese female"),
        ("Nezumi (Japanese)", 39, "Japanese female"), ("Tebukuro (Japanese)", 40, "Japanese female"),
        ("Kumo (Japanese)", 41, "Japanese male"),
        ("Dora (Portuguese)", 42, "Portuguese female"), ("Alex (Portuguese)", 43, "Portuguese male"),
        ("Santa (Portuguese)", 44, "Portuguese male"),
        ("Xiaobei (Chinese)", 45, "Chinese female"), ("Xiaoni (Chinese)", 46, "Chinese female"),
        ("Xiaoxiao (Chinese)", 47, "Chinese female"), ("Xiaoyi (Chinese)", 48, "Chinese female"),
        ("Yunjian (Chinese)", 49, "Chinese male"), ("Yunxi (Chinese)", 50, "Chinese male"),
        ("Yunxia (Chinese)", 51, "Chinese male"), ("Yunyang (Chinese)", 52, "Chinese male"),
    };
}
