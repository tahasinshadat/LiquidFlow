using System.Security.Cryptography;
using System.Text;
using FluidVoice.Core;

namespace FluidVoice.Ai;

public sealed record ProviderInfo(string Id, string Name, string BaseUrl, string DefaultModel, bool IsLocal);

/// <summary>
/// AI provider catalog (ModelRepository.swift:18-59) + verification fingerprints
/// (SHA256 of "baseURL|apiKey", SettingsStore+CommandMode.swift:121-128).
/// "fluid-local" is the open, llama.cpp-served substitute for the proprietary
/// Fluid Intelligence runtime.
/// </summary>
public static class ProviderCatalog
{
    public const string FluidLocalId = "fluid-local";

    public static readonly IReadOnlyList<ProviderInfo> BuiltIn = new List<ProviderInfo>
    {
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4.1", false),
        new("anthropic", "Anthropic", "https://api.anthropic.com/v1", "claude-sonnet-4-20250514", false),
        new("xai", "xAI", "https://api.x.ai/v1", "grok-3-fast", false),
        new("groq", "Groq", "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", false),
        new("cerebras", "Cerebras", "https://api.cerebras.ai/v1", "gpt-oss-120b", false),
        new("google", "Google", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", false),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-oss-20b", false),
        new("ollama", "Ollama", "http://localhost:11434/v1", "", true),
        new("lmstudio", "LM Studio", "http://localhost:1234/v1", "", true),
        new(FluidLocalId, "Fluid Local AI (open)", "", "", true), // base URL resolved from LocalAiServer
    };

    public static IEnumerable<ProviderInfo> All()
    {
        foreach (var p in BuiltIn) yield return p;
        foreach (var c in Settings.Current.CustomProviders)
            yield return new ProviderInfo(c.Id, c.Name, c.BaseUrl, "", LlmClient.IsLocalEndpoint(c.BaseUrl));
    }

    public static ProviderInfo? ById(string id) => All().FirstOrDefault(p => p.Id == id);

    public static string DisplayName(string id) => ById(id)?.Name ?? id;

    public static string BaseUrlFor(string id)
    {
        if (id == FluidLocalId) return LocalAiServer.BaseUrl;
        return ById(id)?.BaseUrl ?? "";
    }

    public static string? ApiKeyFor(string id) => CredentialStore.GetApiKey(id);

    public static string Fingerprint(string baseUrl, string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{baseUrl}|{apiKey}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Provider is usable: verified fingerprint matches current base URL + key (gate spec §3.2).</summary>
    public static bool IsConfigured(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var s = Settings.Current;
        if (id == FluidLocalId)
            return LocalAiServer.IsModelInstalled() &&
                   s.VerifiedProviderFingerprints.TryGetValue(id, out var flFp) && flFp == FluidLocalId;

        if (!s.VerifiedProviderFingerprints.TryGetValue(id, out var stored)) return false;
        var baseUrl = BaseUrlFor(id);
        var key = ApiKeyFor(id) ?? "";
        if (!LlmClient.IsLocalEndpoint(baseUrl) && key.Trim().Length == 0) return false;
        return Fingerprint(baseUrl, key) == stored;
    }

    public static void MarkVerified(string id)
    {
        var s = Settings.Current;
        s.VerifiedProviderFingerprints[id] = id == FluidLocalId
            ? FluidLocalId
            : Fingerprint(BaseUrlFor(id), ApiKeyFor(id) ?? "");
        s.Save("VerifiedProviderFingerprints");
    }

    public static string? SelectedModelFor(string providerId)
    {
        var s = Settings.Current;
        if (s.SelectedModelByProvider.TryGetValue(providerId, out var m) && !string.IsNullOrWhiteSpace(m))
            return m;
        if (providerId == FluidLocalId) return LocalAiServer.ModelName;
        var def = ById(providerId)?.DefaultModel;
        return string.IsNullOrWhiteSpace(def) ? null : def;
    }

    /// <summary>Effective provider for command mode (SettingsStore+CommandMode.swift:21-31). Fluid Local has no tool support → excluded.</summary>
    public static string EffectiveCommandModeProviderId()
    {
        var s = Settings.Current;
        string candidate = s.CommandModeLinkedToGlobal ? s.SelectedProviderID : s.CommandModeSelectedProviderID;
        if (candidate == FluidLocalId) return "";
        return IsConfigured(candidate) ? candidate : "";
    }

    public static string EffectiveRewriteModeProviderId()
    {
        var s = Settings.Current;
        string candidate = s.RewriteModeLinkedToGlobal ? s.SelectedProviderID : s.RewriteModeSelectedProviderID;
        return IsConfigured(candidate) ? candidate : "";
    }

    public static string? EffectiveCommandModeModel()
    {
        var s = Settings.Current;
        var providerId = EffectiveCommandModeProviderId();
        if (providerId.Length == 0) return null;
        if (!s.CommandModeLinkedToGlobal && !string.IsNullOrWhiteSpace(s.CommandModeSelectedModel))
            return s.CommandModeSelectedModel;
        return SelectedModelFor(providerId);
    }

    public static string? EffectiveRewriteModeModel()
    {
        var s = Settings.Current;
        var providerId = EffectiveRewriteModeProviderId();
        if (providerId.Length == 0) return null;
        if (!s.RewriteModeLinkedToGlobal && !string.IsNullOrWhiteSpace(s.RewriteModeSelectedModel))
            return s.RewriteModeSelectedModel;
        return SelectedModelFor(providerId);
    }
}
