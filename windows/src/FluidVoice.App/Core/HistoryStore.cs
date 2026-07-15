using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidVoice.Core;

public sealed class DictationAudioMetadata
{
    public string FileName { get; set; } = "";
    public int DurationMilliseconds { get; set; }
    public long ByteCount { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? Model { get; set; }
}

/// <summary>Schema mirrors TranscriptionHistoryEntry (TranscriptionHistoryStore.swift).</summary>
public sealed class TranscriptionHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string RawText { get; set; } = "";
    public string ProcessedText { get; set; } = "";
    public string AppName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public bool WasAIProcessed { get; set; }
    /// <summary>User cancelled before insertion — transcript kept, nothing typed (Wispr-style cancel).</summary>
    public bool WasCancelled { get; set; }
    public string? ProcessingModel { get; set; }
    public string? AiProcessingError { get; set; }
    public DictationAudioMetadata? Audio { get; set; }

    [JsonIgnore] public int CharacterCount => ProcessedText.Length;
    [JsonIgnore]
    public int WordCount => ProcessedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// Transcription history + usage stats (newest first). Stats formulas mirror
/// TranscriptionHistoryStore.swift / StatsView.swift: time saved =
/// words/typingWPM − words/speakingWPM with speaking = 150 WPM.
/// </summary>
public static class HistoryStore
{
    private const int SpeakingWPM = 150;
    private static readonly object Sync = new();
    private static List<TranscriptionHistoryEntry>? _entries;

    public static event Action? HistoryChanged;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static List<TranscriptionHistoryEntry> Entries
    {
        get
        {
            lock (Sync)
            {
                if (_entries is null)
                {
                    try
                    {
                        _entries = File.Exists(AppPaths.HistoryFile)
                            ? JsonSerializer.Deserialize<List<TranscriptionHistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile), JsonOpts) ?? new()
                            : new();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("history", "Failed to load history", ex);
                        _entries = new();
                    }
                }
                return _entries;
            }
        }
    }

    public static void AddEntry(TranscriptionHistoryEntry entry)
    {
        if (!Settings.Current.SaveTranscriptionHistory) return;
        if (string.IsNullOrWhiteSpace(entry.ProcessedText) && string.IsNullOrWhiteSpace(entry.RawText)) return;
        lock (Sync)
        {
            Entries.Insert(0, entry); // newest first
            Persist();
        }
        HistoryChanged?.Invoke();
    }

    /// <summary>Replace the (typed) text of an existing entry — used when the user edits a past
    /// transcription to fix or delete a word.</summary>
    public static void UpdateEntry(Guid id, string newProcessedText)
    {
        lock (Sync)
        {
            var entry = Entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return;
            entry.ProcessedText = newProcessedText;
            Persist();
        }
        HistoryChanged?.Invoke();
    }

    public static void DeleteEntries(IEnumerable<Guid> ids)
    {
        var idSet = ids.ToHashSet();
        lock (Sync)
        {
            foreach (var e in Entries.Where(e => idSet.Contains(e.Id) && e.Audio is not null))
                TryDeleteAudio(e.Audio!);
            Entries.RemoveAll(e => idSet.Contains(e.Id));
            Persist();
        }
        HistoryChanged?.Invoke();
    }

    public static void ClearAll()
    {
        lock (Sync)
        {
            foreach (var e in Entries.Where(e => e.Audio is not null))
                TryDeleteAudio(e.Audio!);
            Entries.Clear();
            Persist();
        }
        HistoryChanged?.Invoke();
    }

    public static List<TranscriptionHistoryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Entries.ToList();
        lock (Sync)
        {
            return Entries.Where(e =>
                e.RawText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.ProcessedText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.AppName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private static void TryDeleteAudio(DictationAudioMetadata audio)
    {
        try { File.Delete(Path.Combine(AppPaths.AudioHistoryDir, audio.FileName)); } catch { }
    }

    private static void Persist()
    {
        try
        {
            var tmp = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, JsonOpts));
            File.Move(tmp, AppPaths.HistoryFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("history", "Failed to persist history", ex);
        }
    }

    // ---- stats (StatsView.swift formulas) ----

    public static int WordsToday => WordsSince(DateTime.Today);
    public static int TranscriptionsToday
    {
        get { lock (Sync) return Entries.Count(e => e.Timestamp >= DateTime.Today); }
    }

    public static int TotalWords
    {
        get { lock (Sync) return Entries.Sum(e => e.WordCount); }
    }

    private static int WordsSince(DateTime cutoff)
    {
        lock (Sync) return Entries.Where(e => e.Timestamp >= cutoff).Sum(e => e.WordCount);
    }

    /// <summary>minutes = words/typingWPM − words/speakingWPM</summary>
    public static double TimeSavedMinutes(int words)
    {
        var typingWpm = Math.Max(1, Settings.Current.UserTypingWPM);
        return words / (double)typingWpm - words / (double)SpeakingWPM;
    }

    public static string FormatMinutes(double minutes) => minutes switch
    {
        < 1 => "< 1m",
        < 60 => $"{(int)Math.Round(minutes)}m",
        _ => ((int)(minutes / 60), (int)Math.Round(minutes % 60)) is var (h, m) && m > 0 ? $"{h}h {m}m" : $"{(int)(minutes / 60)}h",
    };

    public static int CurrentStreakDays
    {
        get
        {
            lock (Sync)
            {
                var days = Entries.Select(e => e.Timestamp.Date).ToHashSet();
                if (days.Count == 0) return 0;
                var day = DateTime.Today;
                if (!days.Contains(day)) day = day.AddDays(-1); // streak survives until today ends
                int streak = 0;
                while (days.Contains(day))
                {
                    streak++;
                    day = day.AddDays(-1);
                }
                return streak;
            }
        }
    }

    public static List<(string App, int Count)> TopApps(int limit = 5)
    {
        lock (Sync)
        {
            return Entries.Where(e => e.AppName.Length > 0)
                .GroupBy(e => e.AppName)
                .Select(g => (g.Key, g.Count()))
                .OrderByDescending(t => t.Item2)
                .Take(limit)
                .ToList();
        }
    }

    public static double AiEnhancementRate
    {
        get
        {
            lock (Sync)
            {
                if (Entries.Count == 0) return 0;
                return Entries.Count(e => e.WasAIProcessed) / (double)Entries.Count;
            }
        }
    }

    public static List<(DateTime Date, int Words)> DailyWordCounts(int days)
    {
        lock (Sync)
        {
            var result = new List<(DateTime, int)>();
            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var words = Entries.Where(e => e.Timestamp.Date == date).Sum(e => e.WordCount);
                result.Add((date, words));
            }
            return result;
        }
    }
}
