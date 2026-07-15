using System.IO;
using System.Text.Json;

namespace FluidVoice.Core;

/// <summary>Persists meeting notes to meetings.json in the app data dir (mirrors HistoryStore).</summary>
public static class MeetingStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "meetings.json");
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static List<Meeting> _meetings = new();
    private static bool _loaded;

    /// <summary>Raised (any thread) after the meeting list changes.</summary>
    public static event Action? Changed;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Sync)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(FilePath))
                    _meetings = JsonSerializer.Deserialize<List<Meeting>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex)
            {
                Log.Warn("meeting", $"Failed to load meetings: {ex.Message}");
                _meetings = new();
            }
            _loaded = true;
        }
    }

    /// <summary>All meetings, newest first.</summary>
    public static IReadOnlyList<Meeting> All
    {
        get
        {
            EnsureLoaded();
            lock (Sync) return _meetings.OrderByDescending(m => m.StartedAt).ToList();
        }
    }

    /// <summary>Insert or update a meeting, then persist.</summary>
    public static void Save(Meeting meeting)
    {
        EnsureLoaded();
        lock (Sync)
        {
            var idx = _meetings.FindIndex(m => m.Id == meeting.Id);
            if (idx >= 0) _meetings[idx] = meeting;
            else _meetings.Add(meeting);
            Persist();
        }
        Changed?.Invoke();
    }

    public static void Delete(string id)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _meetings.RemoveAll(m => m.Id == id);
            Persist();
        }
        Changed?.Invoke();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_meetings, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("meeting", "Failed to save meetings", ex);
        }
    }
}
