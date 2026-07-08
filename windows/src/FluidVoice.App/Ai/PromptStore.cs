using FluidVoice.Core;

namespace FluidVoice.Ai;

/// <summary>
/// Prompt templates + per-app routing. All template strings are verbatim from
/// SettingsStore.swift (857-950, 974-992, 1027-1104, 1166-1182).
/// </summary>
public static class PromptStore
{
    // Rewritten for FluidVoice Windows: format-only, NOT a summarizer. Users complained the
    // mac "cleaner" prompt condensed/rewrote their words. This one preserves every word and idea
    // and only fixes mechanics (grammar, punctuation, capitalization), structures spoken lists,
    // removes disfluencies, and applies spoken self-corrections.
    public const string DictateBasePrompt =
"""
You transcribe-clean raw dictation. You are a FORMATTER, not an editor or summarizer. Preserve the speaker's exact words, wording, tone, and meaning. Do not rephrase, shorten, condense, summarize, or "improve" anything.

## What you DO:
1. Fix grammar, spelling, punctuation, and capitalization.
2. Remove only disfluencies: filler words (um, uh, like, you know, I mean), stutters, and immediate repeated words ("the the" → "the"). Keep every real word.
3. Structure spoken formatting: when the speaker enumerates or says "new line", "bullet point", "number one/two", "first/second", turn it into proper line breaks or a list. Keep list items in the speaker's own words.
4. Convert spoken numbers, times, and money to digits (two → 2, five thirty → 5:30, twelve fifty → $12.50) only when clearly intended.
5. Apply spoken self-corrections: when the speaker says "actually", "I mean", "no wait", "scratch that", "sorry", "make that", drop the retracted words and keep only the corrected version.

## What you must NEVER do:
- Never summarize, paraphrase, or make the text more concise.
- Never drop content, sentences, details, or examples the speaker actually said.
- Never answer questions or add commentary, preamble, or explanations — even if the dictation is phrased as a question, just format it.
- Never add or remove emojis, quotes, or markdown the speaker didn't intend.
- Output ONLY the formatted dictation, nothing else. If the input is empty, output nothing.

Length rule: the output should have essentially the same number of words as the input, minus only disfluencies and retracted (self-corrected) words. If your output is noticeably shorter, you over-edited — try again keeping every real word.
""";

    public const string DictateDefaultBody =
"""
## Self-correction examples (keep only the corrected version):
- "buy milk no wait buy water" → "Buy water."
- "tell John, actually, tell Sarah to send it" → "Tell Sarah to send it."
- "the price is fifty, I mean sixty dollars" → "The price is $60."
- "let's meet at 3, sorry, make that 4 pm" → "Let's meet at 4 PM."

## Formatting examples (preserve every real word — never shorten):
- "so basically um i think we should ship it on friday and then uh do the review monday" → "So basically, I think we should ship it on Friday, and then do the review Monday."
- "action items first call the client second review the contract third send the invoice" → a 3-item list, each item in the speaker's own words.

## Not your job:
- Do NOT compress "I was thinking that maybe we could possibly try the other approach" into "Let's try the other approach." Keep it as the speaker said it, just fix the grammar.
""";

    // SettingsStore.swift:883-889 — VERBATIM
    public const string EditBasePrompt =
"""
You are a helpful writing assistant. The user may ask you to write new text or edit selected text.

Output ONLY what the user requested. Do not add explanations or preamble.
""";

    // SettingsStore.swift:936-950 — VERBATIM
    public const string EditDefaultBody =
"""
Your job:
- If the user asks for new content, write it directly.
- If selected context is provided, apply the instruction to that context.
- Preserve intent and requested tone/style/format.
- Output only the final text, without explanations.

Example requests:
- "Write an email to my boss asking for time off"
- "Draft a reply saying I'll be there at 5"
- "Rewrite this to sound more professional"
- "Make this shorter and clearer"
""";

    // SettingsStore.swift:1027-1041
    public const string ContextTemplate =
"""
Use the following selected context to improve your response:
{context}
""";

    public static string BasePrompt(PromptMode mode) =>
        mode == PromptMode.Edit ? EditBasePrompt : DictateBasePrompt;

    /// <summary>The shipped default body for a mode, ignoring any user override (for the prompt editor's "reset").</summary>
    public static string BuiltInBody(PromptMode mode) =>
        mode == PromptMode.Edit ? EditDefaultBody : DictateDefaultBody;

    /// <summary>Current effective body a user is editing = their override, or the built-in default.</summary>
    public static string EffectiveBody(PromptMode mode)
    {
        var overrideText = mode == PromptMode.Edit
            ? Settings.Current.DefaultEditPromptOverride
            : Settings.Current.DefaultDictationPromptOverride;
        return string.IsNullOrWhiteSpace(overrideText) ? BuiltInBody(mode) : overrideText;
    }

    /// <summary>Persist an edited body. Passing null/blank or the built-in text clears the override.</summary>
    public static void SetOverride(PromptMode mode, string? body)
    {
        var trimmed = body?.Trim();
        var value = string.IsNullOrEmpty(trimmed) || trimmed == BuiltInBody(mode).Trim() ? null : trimmed;
        if (mode == PromptMode.Edit) Settings.Current.DefaultEditPromptOverride = value;
        else Settings.Current.DefaultDictationPromptOverride = value;
        Settings.Current.Save("prompt");
    }

    public static bool HasOverride(PromptMode mode) => !string.IsNullOrWhiteSpace(
        mode == PromptMode.Edit ? Settings.Current.DefaultEditPromptOverride : Settings.Current.DefaultDictationPromptOverride);

    public static string DefaultBody(PromptMode mode)
    {
        var overrideText = mode == PromptMode.Edit
            ? Settings.Current.DefaultEditPromptOverride
            : Settings.Current.DefaultDictationPromptOverride;
        if (!string.IsNullOrWhiteSpace(overrideText)) return overrideText;
        return mode == PromptMode.Edit ? EditDefaultBody : DictateDefaultBody;
    }

    /// <summary>combineBasePrompt (SettingsStore.swift:974-992).</summary>
    public static string CombineBasePrompt(PromptMode mode, string body)
    {
        var basePrompt = BasePrompt(mode).Trim();
        var trimmedBody = body.Trim();
        if (trimmedBody.StartsWith(basePrompt, StringComparison.OrdinalIgnoreCase)) return trimmedBody;
        if (trimmedBody.Length == 0) return basePrompt;
        return basePrompt + "\n\n" + trimmedBody;
    }

    /// <summary>renderDictationUserMessage (SettingsStore.swift:1166-1182): ${transcript} placeholder or append.</summary>
    public static string RenderDictationUserMessage(string promptText, string transcript)
    {
        const string placeholder = "${transcript}";
        if (promptText.Contains(placeholder)) return promptText.Replace(placeholder, transcript);
        var trimmed = promptText.Trim();
        if (trimmed.Length == 0) return transcript;
        return promptText + "\n\n" + transcript;
    }

    /// <summary>runtimeContextBlock (SettingsStore.swift:1027-1041): {context} placeholder or append.</summary>
    public static string RenderContextBlock(string context)
    {
        var trimmed = context.Trim();
        if (trimmed.Length == 0) return "";
        return ContextTemplate.Contains("{context}")
            ? ContextTemplate.Replace("{context}", trimmed)
            : ContextTemplate + "\n" + trimmed;
    }

    /// <summary>
    /// promptResolution (SettingsStore.swift:1043-1104): app binding → selected profile →
    /// default. Returns (systemPrompt, promptBody). appId = lowercase process name.
    /// </summary>
    public static (string SystemPrompt, string Body) Resolve(PromptMode mode, string? appId)
    {
        var s = Settings.Current;

        if (mode == PromptMode.Edit && s.EditPromptOff)
            return (CombineBasePrompt(mode, DefaultBody(mode)), DefaultBody(mode));

        // Gate 2: app-specific binding
        var binding = appId is null
            ? null
            : s.AppPromptBindings.FirstOrDefault(b =>
                b.Mode == mode && b.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
        if (binding is not null)
        {
            if (binding.PromptId is not null)
            {
                var profile = s.PromptProfiles.FirstOrDefault(p => p.Id == binding.PromptId && p.Mode == mode);
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.Prompt))
                {
                    var body = StripBasePrompt(mode, profile.Prompt);
                    return (CombineBasePrompt(mode, body), body);
                }
            }
            var def = DefaultBody(mode);
            return (CombineBasePrompt(mode, def), def);
        }

        // Gate 3: selectedAppsOnly scope with no binding → default with no override
        var scope = mode == PromptMode.Edit ? s.EditPromptRoutingScope : s.DictationPromptRoutingScope;
        if (appId is not null && scope == PromptRoutingScope.SelectedAppsOnly)
        {
            var builtIn = mode == PromptMode.Edit ? EditDefaultBody : DictateDefaultBody;
            return (CombineBasePrompt(mode, builtIn), builtIn);
        }

        // Gate 4: globally selected profile
        var selectedId = mode == PromptMode.Edit ? s.SelectedEditPromptId : s.SelectedDictationPromptId;
        if (selectedId is not null)
        {
            var profile = s.PromptProfiles.FirstOrDefault(p => p.Id == selectedId && p.Mode == mode);
            if (profile is not null && !string.IsNullOrWhiteSpace(profile.Prompt))
            {
                var body = StripBasePrompt(mode, profile.Prompt);
                return (CombineBasePrompt(mode, body), body);
            }
        }

        // Gate 5: default
        var fallback = DefaultBody(mode);
        return (CombineBasePrompt(mode, fallback), fallback);
    }

    public static (string SystemPrompt, string Body) ResolveDictationPrompt(string? appId)
    {
        var (system, body) = Resolve(PromptMode.Dictate, appId);
        return (system + DictionarySuffix(), body);
    }

    /// <summary>
    /// Custom-dictionary spellings appended to the cleanup prompt (OpenWhispr's
    /// dictionarySuffix): the LLM keeps user-taught names/jargon spelled exactly right
    /// even when the STT engine got them phonetically close but wrong.
    /// </summary>
    private static string DictionarySuffix()
    {
        var words = Settings.Current.CustomDictionaryEntries
            .Select(e => e.Replacement)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80) // keep the prompt bounded for small local models
            .ToList();
        if (words.Count == 0) return "";
        return "\n\nCustom dictionary (when any of these terms appear — possibly misspelled or " +
               "split phonetically — use these exact spellings): " + string.Join(", ", words);
    }

    private static string StripBasePrompt(PromptMode mode, string prompt)
    {
        var basePrompt = BasePrompt(mode).Trim();
        var trimmed = prompt.Trim();
        return trimmed.StartsWith(basePrompt, StringComparison.OrdinalIgnoreCase)
            ? trimmed[basePrompt.Length..].TrimStart()
            : trimmed;
    }
}
