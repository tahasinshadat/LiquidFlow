using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace FluidVoice.Core;

/// <summary>
/// Optional local audio history (DictationAudioHistoryStore.swift): 16-bit PCM WAV
/// named yyyy-MM-ddTHH-mm-ssZ_XXXXXXXX.wav, pruned to a GB budget (orphans first,
/// then oldest), exportable as ZIP with manifest.jsonl + audio/ directory.
/// </summary>
public static class AudioHistoryStore
{
    public static DictationAudioMetadata? SaveAudio(float[] pcm16k, string? modelId)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AudioHistoryDir);
            var fileName = $"{DateTime.UtcNow:yyyy-MM-dd'T'HH-mm-ss'Z'}_{Guid.NewGuid().ToString("N")[..8]}.wav";
            var path = Path.Combine(AppPaths.AudioHistoryDir, fileName);
            WriteWav16(path, pcm16k, 16_000);
            PruneToBudget();
            return new DictationAudioMetadata
            {
                FileName = fileName,
                DurationMilliseconds = (int)(pcm16k.Length / 16.0),
                ByteCount = new FileInfo(path).Length,
                SampleRate = 16_000,
                Channels = 1,
                Model = modelId,
            };
        }
        catch (Exception ex)
        {
            Log.Error("audiohistory", "Failed to save audio", ex);
            return null;
        }
    }

    public static void WriteWav16(string path, float[] samples, int sampleRate)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int dataLen = samples.Length * 2;
        bw.Write("RIFF"u8);
        bw.Write(36 + dataLen);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);            // PCM
        bw.Write((short)1);            // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);      // byte rate
        bw.Write((short)2);            // block align
        bw.Write((short)16);           // bits
        bw.Write("data"u8);
        bw.Write(dataLen);
        foreach (var s in samples)
            bw.Write((short)Math.Clamp((int)(s * 32767f), short.MinValue, short.MaxValue));
    }

    /// <summary>Delete orphaned files first, then oldest, until under the GB budget.</summary>
    public static void PruneToBudget()
    {
        try
        {
            var budgetBytes = (long)(Math.Max(0.1, Settings.Current.AudioHistoryBudgetGB) * 1024 * 1024 * 1024);
            var dir = new DirectoryInfo(AppPaths.AudioHistoryDir);
            if (!dir.Exists) return;
            var files = dir.GetFiles("*.wav").OrderBy(f => f.CreationTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            if (total <= budgetBytes) return;

            var referenced = HistoryStore.Entries
                .Where(e => e.Audio is not null)
                .Select(e => e.Audio!.FileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var f in files.Where(f => !referenced.Contains(f.Name)).ToList())
            {
                if (total <= budgetBytes) break;
                total -= f.Length;
                f.Delete();
                files.Remove(f);
            }
            foreach (var f in files)
            {
                if (total <= budgetBytes) break;
                total -= f.Length;
                var name = f.Name;
                f.Delete();
                foreach (var e in HistoryStore.Entries.Where(e => e.Audio?.FileName == name))
                    e.Audio = null;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("audiohistory", $"Prune failed: {ex.Message}");
        }
    }

    public static long CurrentUsageBytes()
    {
        try
        {
            var dir = new DirectoryInfo(AppPaths.AudioHistoryDir);
            return dir.Exists ? dir.GetFiles("*.wav").Sum(f => f.Length) : 0;
        }
        catch { return 0; }
    }

    /// <summary>ZIP export: manifest.jsonl + audio/ (DictationAudioHistoryStore export format).</summary>
    public static void ExportZip(IEnumerable<TranscriptionHistoryEntry> entries, string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var manifestLines = new List<string>();
        foreach (var entry in entries.Where(e => e.Audio is not null).OrderBy(e => e.Timestamp))
        {
            var src = Path.Combine(AppPaths.AudioHistoryDir, entry.Audio!.FileName);
            if (!File.Exists(src)) continue;
            var archived = "audio/" + entry.Audio.FileName;
            zip.CreateEntryFromFile(src, archived, CompressionLevel.Optimal);
            manifestLines.Add(JsonSerializer.Serialize(new
            {
                audio = archived,
                raw_transcript = entry.RawText,
                final_transcript = entry.ProcessedText,
                timestamp = entry.Timestamp.ToUniversalTime().ToString("o"),
                duration_ms = entry.Audio.DurationMilliseconds,
                sample_rate = entry.Audio.SampleRate,
                channels = entry.Audio.Channels,
                app = entry.AppName,
                model = entry.Audio.Model ?? "",
            }));
        }
        var manifest = zip.CreateEntry("manifest.jsonl");
        using var writer = new StreamWriter(manifest.Open());
        foreach (var line in manifestLines) writer.WriteLine(line);
    }
}
