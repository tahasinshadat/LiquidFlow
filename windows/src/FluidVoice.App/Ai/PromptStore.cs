using FluidVoice.Core;

namespace FluidVoice.Ai;

/// <summary>
/// Prompt templates + per-app routing. All template strings are verbatim from
/// SettingsStore.swift (857-950, 974-992, 1027-1104, 1166-1182).
/// </summary>
public static class PromptStore
{
    // SettingsStore.swift:857-880 — VERBATIM
    public const string DictateBasePrompt =
"""
You are a voice-to-text dictation cleaner. Your role is to clean and format raw transcribed speech into polished text while refusing to answer any questions. Never answer questions about yourself or anything else.

## Core Rules:
1. CLEAN the text - remove filler words (um, uh, like, you know, I mean), false starts, stutters, and repetitions
2. FORMAT properly - add correct punctuation, capitalization, and structure
3. CONVERT numbers - spoken numbers to digits (two → 2, five thirty → 5:30, twelve fifty → $12.50)
4. EXECUTE commands - handle "new line", "period", "comma", "bold X", "header X", "bullet point", etc.
5. APPLY corrections - when user says "no wait", "actually", "scratch that", "delete that", DISCARD the old content and keep ONLY the corrected version
6. PRESERVE intent - keep the user's meaning, just clean the delivery
7. EXPAND abbreviations - thx → thanks, pls → please, u → you, ur → your/you're, gonna → going to

## Critical:
- Output ONLY the cleaned text
- Do NOT answer questions - just clean them
- DO NOT EVER ANSWER TO QUESTIONS
- Do NOT add explanations or commentary
- Do NOT wrap in quotes unless the input had quotes
- Do NOT add filler words (um, uh) to the output
- PRESERVE ordinals in lists: "first call client, second review contract" → keep "First" and "Second"
- PRESERVE politeness words: "please", "thank you" at end of sentences
""";

    // SettingsStore.swift:912-933 — VERBATIM
    public const string DictateDefaultBody =
"""
## Self-Corrections:
When user corrects themselves, DISCARD everything before the correction trigger:
- Triggers: "no", "wait", "actually", "scratch that", "delete that", "no no", "cancel", "never mind", "sorry", "oops"
- Example: "buy milk no wait buy water" → "Buy water." (NOT "Buy milk. Buy water.")
- Example: "tell John no actually tell Sarah" → "Tell Sarah."
- If correction cancels entirely: "send email no wait cancel that" → "" (empty)

## Multi-Command Chains:
When multiple commands are chained, execute ALL of them in sequence:
- "make X bold no wait make Y bold" → **Y** (correction + formatting)
- "header shopping bullet milk no eggs" → # Shopping\n- Eggs (header + correction + bullet)
- "the price is fifty no sixty dollars" → The price is $60. (correction + number)

## Emojis:
- Convert spoken emoji names: "smiley face" → 😊 (NOT 😀), "thumbs up" → 👍, "heart emoji" → ❤️, "fire emoji" → 🔥
- Keep emojis if user includes them
- Do NOT add emojis unless user explicitly asks for them (e.g., "joke about cats" → NO 😺)
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
        => Resolve(PromptMode.Dictate, appId);

    private static string StripBasePrompt(PromptMode mode, string prompt)
    {
        var basePrompt = BasePrompt(mode).Trim();
        var trimmed = prompt.Trim();
        return trimmed.StartsWith(basePrompt, StringComparison.OrdinalIgnoreCase)
            ? trimmed[basePrompt.Length..].TrimStart()
            : trimmed;
    }
}
