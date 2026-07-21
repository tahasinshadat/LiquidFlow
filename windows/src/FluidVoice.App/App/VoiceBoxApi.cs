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
        [property: JsonPropertyName("default_engine")] string? DefaultEngine);

    public sealed record Generation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("profile_id")] string? ProfileId,
        [property: JsonPropertyName("profile_name")] string? ProfileName,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("engine")] string? Engine,
        [property: JsonPropertyName("created_at")] string? CreatedAt);

    private sealed record HistoryPage([property: JsonPropertyName("items")] List<Generation> Items);

    public sealed record ModelInfo(
        [property: JsonPropertyName("model_name")] string ModelName,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("downloaded")] bool Downloaded,
        [property: JsonPropertyName("downloading")] bool Downloading,
        [property: JsonPropertyName("loaded")] bool Loaded,
        [property: JsonPropertyName("size_mb")] double? SizeMb);

    private sealed record ModelsStatus([property: JsonPropertyName("models")] List<ModelInfo> Models);

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
        resp.EnsureSuccessStatusCode();
    }

    public static Task DeleteProfileAsync(string id, CancellationToken ct = default)
        => Http.DeleteAsync($"/profiles/{id}", ct);

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
        resp.EnsureSuccessStatusCode();
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

    public static Task<byte[]> GetAudioAsync(string generationId, CancellationToken ct = default)
        => Http.GetByteArrayAsync($"/audio/{generationId}", ct);

    public static async Task<List<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
        => (await Http.GetFromJsonAsync<ModelsStatus>("/models/status", ct))?.Models ?? new();

    public static async Task DownloadModelAsync(string modelName, CancellationToken ct = default)
    {
        var resp = await Http.PostAsJsonAsync("/models/download", new { model_name = modelName }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public static Task UnloadModelAsync(string modelName, CancellationToken ct = default)
        => Http.PostAsync($"/models/{modelName}/unload", null, ct);
}
