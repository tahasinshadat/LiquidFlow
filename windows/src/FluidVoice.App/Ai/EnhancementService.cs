using FluidVoice.Core;

namespace FluidVoice.Ai;

/// <summary>
/// AI dictation post-processing entry point (DictationPostProcessingService.swift +
/// DictationAIPostProcessingGate.swift). Fully implemented together with LlmClient.
/// </summary>
public static class EnhancementService
{
    public static string? LastUsedModelDescription { get; private set; }

    private static readonly string[] RefusalMarkers =
    {
        "i can't", "i cannot", "i can not", "i'm sorry", "i am sorry", "i apologize",
        "as an ai", "i'm unable", "i am unable", "cannot assist", "can't assist",
        "cannot help with", "can't help with", "i won't", "i will not",
    };

    /// <summary>
    /// Sanity gate for enhanced output (small local models sometimes answer or refuse
    /// instead of cleaning). Rejects refusal-style openings and wild length divergence
    /// so chatter never gets typed into the user's document.
    /// </summary>
    public static bool LooksLikeBadEnhancement(string input, string output)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0) return true;

        var head = trimmed.Length > 60 ? trimmed[..60].ToLowerInvariant() : trimmed.ToLowerInvariant();
        if (RefusalMarkers.Any(head.Contains))
        {
            // ...unless the user actually dictated a refusal-ish sentence themselves
            var inputHead = input.Length > 60 ? input[..60].ToLowerInvariant() : input.ToLowerInvariant();
            if (!RefusalMarkers.Any(inputHead.Contains)) return true;
        }

        // Length divergence. Formatting only strips disfluencies + retracted words, so the
        // output should stay close to the input length. Catch summarizing (too short) and
        // hallucinated expansion (too long) → fall back to the raw transcript.
        int inWords = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int outWords = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (inWords >= 8 && outWords < inWords * 0.6) return true;   // summarized/condensed
        if (inWords >= 5 && outWords > inWords * 2.0) return true;    // expanded/answered

        return false;
    }

    /// <summary>Gate: AI runs iff a provider is selected+configured and routing allows this app.</summary>
    public static bool IsConfiguredForDictation(string? appId)
    {
        var s = Settings.Current;
        if (s.DictationPromptOff) return false;
        if (string.IsNullOrEmpty(s.SelectedProviderID)) return false;
        if (s.DictationPromptRoutingScope == PromptRoutingScope.SelectedAppsOnly)
        {
            if (appId is null) return false;
            if (!s.AppPromptBindings.Any(b => b.Mode == PromptMode.Dictate &&
                    b.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return ProviderCatalog.IsConfigured(s.SelectedProviderID);
    }

    public static async Task<string?> EnhanceDictationAsync(string transcript, string? appId, CancellationToken ct)
    {
        var s = Settings.Current;
        var providerId = s.SelectedProviderID;
        var model = ProviderCatalog.SelectedModelFor(providerId);
        if (model is null) throw new InvalidOperationException("No model selected for the AI provider");

        var (systemPrompt, promptBody) = PromptStore.ResolveDictationPrompt(appId);
        var userMessage = PromptStore.RenderDictationUserMessage(promptBody, transcript);

        var messages = new List<LlmMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new LlmMessage("system", systemPrompt));
        messages.Add(new LlmMessage("user", userMessage));

        var response = await LlmClient.CallAsync(new LlmRequest
        {
            ProviderId = providerId,
            Model = model,
            Messages = messages,
            // dictation temperature 0.2 (AIProvider.swift); 0 for the small local models,
            // which drift into answering/refusing at any temperature above greedy
            Temperature = providerId == ProviderCatalog.FluidLocalId ? 0.0 : 0.2,
            TimeoutSeconds = 120,        // dictation override (DictationPostProcessingService.swift:129)
        }, ct);

        LastUsedModelDescription = $"{ProviderCatalog.DisplayName(providerId)} · {model}";
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidOperationException("Empty response from AI provider");
        return response.Content;
    }
}
