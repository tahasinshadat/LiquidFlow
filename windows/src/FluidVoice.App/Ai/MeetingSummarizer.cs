using FluidVoice.Core;

namespace FluidVoice.Ai;

/// <summary>Turns a meeting transcript into structured notes using the configured AI provider
/// (same LLM plumbing as dictation cleanup). Returns null when no provider is configured.</summary>
public static class MeetingSummarizer
{
    // Keep the prompt well under typical context limits; summarize the most recent portion of
    // very long meetings rather than failing the call.
    private const int MaxTranscriptChars = 16000;

    public static bool IsAvailable
    {
        get
        {
            var id = Settings.Current.SelectedProviderID;
            return !string.IsNullOrEmpty(id) && ProviderCatalog.IsConfigured(id);
        }
    }

    public static async Task<string?> SummarizeAsync(string transcript, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        var providerId = Settings.Current.SelectedProviderID;
        if (string.IsNullOrEmpty(providerId) || !ProviderCatalog.IsConfigured(providerId)) return null;
        var model = ProviderCatalog.SelectedModelFor(providerId);
        if (model is null) return null;

        var clipped = transcript.Length > MaxTranscriptChars
            ? "…" + transcript[^MaxTranscriptChars..]
            : transcript;

        const string system =
            "You are a meeting-notes assistant. From the transcript, produce concise notes with " +
            "these markdown sections, each with a bold heading:\n" +
            "**Summary** — 2-4 sentences.\n" +
            "**Key Points** — bullet list.\n" +
            "**Decisions** — bullet list, or 'None'.\n" +
            "**Action Items** — bullet list (include the owner if named), or 'None'.\n" +
            "Be faithful to the transcript and do not invent details. The transcript may be rough " +
            "(auto-captured from mixed audio); infer speakers only when clear.";

        var messages = new List<LlmMessage>
        {
            new("system", system),
            new("user", $"Transcript:\n\n{clipped}"),
        };

        var response = await LlmClient.CallAsync(new LlmRequest
        {
            ProviderId = providerId,
            Model = model,
            Messages = messages,
            Temperature = providerId == ProviderCatalog.FluidLocalId ? 0.2 : 0.4,
            TimeoutSeconds = 180,
        }, ct);

        return string.IsNullOrWhiteSpace(response.Content) ? null : response.Content.Trim();
    }

    /// <summary>Short descriptive meeting title from the transcript, or null (no provider,
    /// empty transcript, or an unusable response — callers keep their fallback title).</summary>
    public static async Task<string?> TitleAsync(string transcript, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        var providerId = Settings.Current.SelectedProviderID;
        if (string.IsNullOrEmpty(providerId) || !ProviderCatalog.IsConfigured(providerId)) return null;
        var model = ProviderCatalog.SelectedModelFor(providerId);
        if (model is null) return null;

        // The opening usually states the topic; the end covers what it became about.
        var clipped = transcript.Length > 6000
            ? transcript[..3000] + " … " + transcript[^3000..]
            : transcript;

        const string system =
            "You name meetings. Reply with only a short descriptive title for the meeting " +
            "transcript, 3 to 7 words, in plain text: no quotes, no trailing punctuation, and " +
            "never a generic name like 'Meeting' or 'Discussion' alone.";

        var response = await LlmClient.CallAsync(new LlmRequest
        {
            ProviderId = providerId,
            Model = model,
            Messages = new List<LlmMessage>
            {
                new("system", system),
                new("user", $"Transcript:\n\n{clipped}"),
            },
            Temperature = providerId == ProviderCatalog.FluidLocalId ? 0.2 : 0.3,
            TimeoutSeconds = 60,
        }, ct);

        var title = response.Content.Trim().Split('\n').FirstOrDefault()?.Trim().Trim('"', '\'', '“', '”', '.', ':') ?? "";
        if (title.Length is 0 or > 80) return null; // over-long output = the model rambled, keep the fallback
        return title;
    }
}
