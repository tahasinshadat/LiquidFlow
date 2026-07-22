using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FluidVoice.App;

/// <summary>
/// Typed client for the local native VoiceBox server (127.0.0.1:17493). Shapes match the
/// backend's FastAPI models (verified against the vendored source). Used by the native
/// LiquidFlow-styled VoiceBox studio page — no web UI involved.
/// </summary>
public static class VoiceBoxApi
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{VoiceBoxNative.Port}"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    public sealed record Profile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("voice_type")] string? VoiceType,
        [property: JsonPropertyName("preset_engine")] string? PresetEngine,
        [property: JsonPropertyName("preset_voice_id")] string? PresetVoiceId,
        [property: JsonPropertyName("default_engine")] string? DefaultEngine,
        [property: JsonPropertyName("sample_count")] int? SampleCount);

    public sealed record Generation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("profile_id")] string? ProfileId,
        [property: JsonPropertyName("profile_name")] string? ProfileName,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("engine")] string? Engine,
        [property: JsonPropertyName("created_at")] string? CreatedAt,
        [property: JsonPropertyName("is_favorited")] bool? IsFavorited);

    private sealed record HistoryPage([property: JsonPropertyName("items")] List<Generation> Items);

    public sealed record ModelInfo(
        [property: JsonPropertyName("model_name")] string ModelName,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("downloaded")] bool Downloaded,
        [property: JsonPropertyName("downloading")] bool Downloading,
        [property: JsonPropertyName("loaded")] bool Loaded,
        [property: JsonPropertyName("size_mb")] double? SizeMb);

    private sealed record ModelsStatus([property: JsonPropertyName("models")] List<ModelInfo> Models);

    /// <summary>EnsureSuccessStatusCode, but the exception carries the server's `detail`
    /// message — "400 Bad Request" alone is useless in a status line.</summary>
    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string detail = "";
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.ToString() : body;
        }
        catch { }
        throw new HttpRequestException(string.IsNullOrWhiteSpace(detail)
            ? $"{(int)resp.StatusCode} {resp.ReasonPhrase}"
            : detail);
    }

    public static async Task<List<Profile>> GetProfilesAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<Profile>>("/profiles", ct) ?? new();

    public static async Task CreatePresetProfileAsync(string name, string engine, string voiceId, string lang, string desc, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/profiles", new
        {
            name,
            description = desc,
            language = lang,
            voice_type = "preset",
            preset_engine = engine,
            preset_voice_id = voiceId,
            default_engine = engine,
        }, ct);
        await EnsureOkAsync(resp, ct);
    }

    public static Task DeleteProfileAsync(string id, CancellationToken ct = default)
        => Http.DeleteAsync($"/profiles/{id}", ct);

    /// <summary>Create a CLONED profile (qwen engine) — attach reference audio via UploadSampleAsync.</summary>
    public static async Task<Profile> CreateClonedProfileAsync(string name, string? desc, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/profiles", new
        {
            name,
            description = desc,
            language = "en",
            voice_type = "cloned",
            default_engine = "qwen",
        }, ct);
        await EnsureOkAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<Profile>(cancellationToken: ct))!;
    }

    /// <summary>Upload one reference audio sample (wav/mp3/m4a/flac…) with its transcript.</summary>
    public static async Task UploadSampleAsync(string profileId, string filePath, string referenceText, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        var fileContent = new ByteArrayContent(bytes);
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent(referenceText), "reference_text");
        var resp = await Http.PostAsync($"/profiles/{profileId}/samples", form, ct);
        await EnsureOkAsync(resp, ct);
    }

    public static Task ToggleFavoriteAsync(string generationId, CancellationToken ct = default)
        => Http.PostAsync($"/history/{generationId}/favorite", null, ct);

    // ── stories ────────────────────────────────────────────────────────────

    public sealed record Story(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("item_count")] int? ItemCount);

    public sealed record StoryItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("generation_id")] string GenerationId,
        [property: JsonPropertyName("start_time_ms")] int StartTimeMs,
        [property: JsonPropertyName("track")] int? Track,
        [property: JsonPropertyName("duration_ms")] int? DurationMs);

    public sealed record StoryDetail(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("items")] List<StoryItem> Items);

    public static async Task<List<Story>> GetStoriesAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<Story>>("/stories", ct) ?? new();

    public static async Task<Story> CreateStoryAsync(string name, string? desc, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/stories", new { name, description = desc }, ct);
        await EnsureOkAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<Story>(cancellationToken: ct))!;
    }

    public static Task DeleteStoryAsync(string id, CancellationToken ct = default)
        => Http.DeleteAsync($"/stories/{id}", ct);

    public static async Task<StoryDetail?> GetStoryAsync(string id, CancellationToken ct = default)
    {
        try { return await Http.GetFromJsonAsync<StoryDetail>($"/stories/{id}", ct); }
        catch { return null; }
    }

    public static async Task AddStoryItemAsync(string storyId, string generationId, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync($"/stories/{storyId}/items", new { generation_id = generationId }, ct);
        await EnsureOkAsync(resp, ct);
    }

    public static Task DeleteStoryItemAsync(string storyId, string itemId, CancellationToken ct = default)
        => Http.DeleteAsync($"/stories/{storyId}/items/{itemId}", ct);

    /// <summary>Server-side mixdown of the whole story → WAV bytes.</summary>
    public static Task<byte[]> ExportStoryAudioAsync(string storyId, CancellationToken ct = default)
        => Http.GetByteArrayAsync($"/stories/{storyId}/export-audio", ct);

    // ── effects + captures (shown when the Native toggle is off) ───────────

    public sealed record EffectPreset(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description);

    public static async Task<List<EffectPreset>> GetEffectPresetsAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<EffectPreset>>("/effects/presets", ct) ?? new();

    public sealed record Capture(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("transcript_raw")] string? TranscriptRaw,
        [property: JsonPropertyName("transcript_refined")] string? TranscriptRefined,
        [property: JsonPropertyName("duration_ms")] int? DurationMs,
        [property: JsonPropertyName("created_at")] string? CreatedAt);

    private sealed record CapturePage([property: JsonPropertyName("items")] List<Capture> Items);

    public static async Task<List<Capture>> GetCapturesAsync(CancellationToken ct = default)
    {
        // tolerate either a bare list or an {items:[…]} page
        try { return await Http.GetFromJsonAsync<List<Capture>>("/captures", ct) ?? new(); }
        catch
        {
            try { return (await Http.GetFromJsonAsync<CapturePage>("/captures", ct))?.Items ?? new(); }
            catch { return new(); }
        }
    }

    public static Task DeleteCaptureAsync(string id, CancellationToken ct = default)
        => Http.DeleteAsync($"/captures/{id}", ct);

    public static async Task<Generation> GenerateAsync(string profileId, string text, string? engine, string? instruct, string? modelSize, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/generate", new
        {
            profile_id = profileId,
            text,
            engine,
            instruct = string.IsNullOrWhiteSpace(instruct) ? null : instruct,
            model_size = modelSize,
        }, ct);
        await EnsureOkAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<Generation>(cancellationToken: ct))!;
    }

    public static async Task<Generation?> GetGenerationAsync(string id, CancellationToken ct = default)
    {
        try { return await Http.GetFromJsonAsync<Generation>($"/history/{id}", ct); }
        catch { return null; }
    }

    public static async Task<List<Generation>> GetHistoryAsync(int limit = 50, CancellationToken ct = default)
        => (await Http.GetFromJsonAsync<HistoryPage>($"/history?limit={limit}", ct))?.Items ?? new();

    public static Task DeleteGenerationAsync(string id, CancellationToken ct = default)
        => Http.DeleteAsync($"/history/{id}", ct);

    public static Task CancelGenerationAsync(string id, CancellationToken ct = default)
        => Http.PostAsync($"/generate/{id}/cancel", null, ct);

    public static Task RetryGenerationAsync(string id, CancellationToken ct = default)
        => Http.PostAsync($"/generate/{id}/retry", null, ct);

    public static Task<byte[]> GetAudioAsync(string generationId, CancellationToken ct = default)
        => Http.GetByteArrayAsync($"/audio/{generationId}", ct);

    public static async Task<List<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
        => (await Http.GetFromJsonAsync<ModelsStatus>("/models/status", ct))?.Models ?? new();

    public static async Task DownloadModelAsync(string modelName, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/models/download", new { model_name = modelName }, ct);
        await EnsureOkAsync(resp, ct);
    }

    public static Task UnloadModelAsync(string modelName, CancellationToken ct = default)
        => Http.PostAsync($"/models/{modelName}/unload", null, ct);

    /// <summary>Delete a downloaded engine from disk (unload it first if loaded).</summary>
    public static Task DeleteModelAsync(string modelName, CancellationToken ct = default)
        => Http.DeleteAsync($"/models/{modelName}", ct);

    public sealed record DownloadTask(
        [property: JsonPropertyName("model_name")] string ModelName,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("progress")] double? Progress,
        [property: JsonPropertyName("current")] long? Current,
        [property: JsonPropertyName("total")] long? Total,
        [property: JsonPropertyName("filename")] string? Filename);

    private sealed record ActiveTasks([property: JsonPropertyName("downloads")] List<DownloadTask> Downloads);

    /// <summary>Snapshot of running/errored downloads — the reliable progress source
    /// (progress %, bytes, filename, and error details all live here).</summary>
    public static async Task<List<DownloadTask>> GetActiveDownloadsAsync(CancellationToken ct = default)
        => (await Http.GetFromJsonAsync<ActiveTasks>("/tasks/active", ct))?.Downloads ?? new();

    /// <summary>Cancel a running download, or dismiss an errored/stale task.</summary>
    public static async Task CancelDownloadAsync(string modelName, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/models/download/cancel", new { model_name = modelName }, ct);
        await EnsureOkAsync(resp, ct);
    }

    private static readonly HttpClient SseHttp = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{VoiceBoxNative.Port}"),
        Timeout = Timeout.InfiniteTimeSpan, // SSE streams stay open for the whole download
    };

    /// <summary>Stream a model's download progress (SSE /models/progress/{name}).
    /// Calls onProgress(fraction 0..1 or -1 for unknown, phase) until complete/error.</summary>
    public static async Task StreamModelProgressAsync(string modelName, Action<double, string> onProgress, CancellationToken ct = default)
    {
        using var resp = await SseHttp.GetAsync($"/models/progress/{modelName}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("data:")) line = line[5..].Trim();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                double pct = -1;
                if (root.TryGetProperty("progress", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number)
                    pct = p.GetDouble();
                else if (root.TryGetProperty("total", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.Number && t.GetDouble() > 0
                         && root.TryGetProperty("current", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number)
                    pct = c.GetDouble() / t.GetDouble() * 100.0;
                var phase = root.TryGetProperty("status", out var s) ? s.GetString() ?? "downloading" : "downloading";
                onProgress(pct < 0 ? -1 : Math.Clamp(pct / 100.0, 0, 1), phase);
                if (phase is "complete" or "error") return;
            }
            catch { /* keepalive / non-JSON lines */ }
        }
    }
}
