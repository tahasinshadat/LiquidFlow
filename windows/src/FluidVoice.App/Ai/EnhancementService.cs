using FluidVoice.Core;

namespace FluidVoice.Ai;

/// <summary>
/// AI dictation post-processing entry point (DictationPostProcessingService.swift +
/// DictationAIPostProcessingGate.swift). Fully implemented together with LlmClient.
/// </summary>
public static class EnhancementService
{
    public static string? LastUsedModelDescription { get; private set; }

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
            Temperature = 0.2,           // dictation temperature (AIProvider.swift)
            TimeoutSeconds = 120,        // dictation override (DictationPostProcessingService.swift:129)
        }, ct);

        LastUsedModelDescription = $"{ProviderCatalog.DisplayName(providerId)} · {model}";
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidOperationException("Empty response from AI provider");
        return response.Content;
    }
}
