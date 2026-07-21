using System.IO;
using System.Text.Json;

namespace FluidVoice.Core;

/// <summary>One scratchpad note (quick thoughts you want to come back to).</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool CustomTitle { get; set; }
    public string Body { get; set; } = "";
    /// <summary>FlowDocument XAML for rich formatting; Body remains the searchable/plain-text copy.</summary>
    public string RichTextXaml { get; set; } = "";
    public bool IsPinned { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>Persists scratchpad notes to notes.json (mirrors MeetingStore).</summary>
public static class NotesStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "notes.json");
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static List<Note> _notes = new();
    private static bool _loaded;

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
                    _notes = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex)
            {
                Log.Warn("notes", $"Failed to load notes: {ex.Message}");
                _notes = new();
            }
            _loaded = true;
        }
    }

    /// <summary>All notes, most recently updated first.</summary>
    public static IReadOnlyList<Note> All
    {
        get
        {
            EnsureLoaded();
            lock (Sync) return _notes
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();
        }
    }

    public static void Save(Note note)
    {
        EnsureLoaded();
        lock (Sync)
        {
            note.UpdatedAt = DateTime.Now;
            var idx = _notes.FindIndex(n => n.Id == note.Id);
            if (idx >= 0) _notes[idx] = note;
            else _notes.Add(note);
            Persist();
        }
        Changed?.Invoke();
    }

    public static void Delete(string id)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _notes.RemoveAll(n => n.Id == id);
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(_notes, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("notes", "Failed to save notes", ex);
        }
    }
}
