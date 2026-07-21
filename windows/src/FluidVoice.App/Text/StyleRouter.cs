using FluidVoice.Core;

namespace FluidVoice.Text;

/// <summary>
/// Routes the focused app to a writing-style context (personal / work / email / other) and
/// turns the user's per-context style choice into a tone instruction for AI cleanup.
/// Settings → Style; the four contexts mirror the Style page tabs.
/// </summary>
public static class StyleRouter
{
    private static readonly string[] Personal = { "discord", "whatsapp", "telegram", "signal", "messenger", "instagram", "snapchat" };
    private static readonly string[] Work = { "slack", "teams", "ms-teams", "zoom", "webex", "linkedin", "mattermost" };
    private static readonly string[] Email = { "outlook", "olk", "thunderbird", "mailspring", "em client", "hxoutlook", "mail" };

    /// <summary>"personal" | "work" | "email" | "other" for a lowercase process name.</summary>
    public static string CategoryFor(string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return "other";
        var id = appId.ToLowerInvariant();
        if (Personal.Any(id.Contains)) return "personal";
        if (Work.Any(id.Contains)) return "work";
        if (Email.Any(id.Contains)) return "email";
        return "other";
    }

    public static string ChoiceFor(string category) => category switch
    {
        "personal" => Settings.Current.StylePersonal,
        "work" => Settings.Current.StyleWork,
        "email" => Settings.Current.StyleEmail,
        _ => Settings.Current.StyleOther,
    };

    /// <summary>Tone line appended to the cleanup system prompt, or "" for the default (formal).</summary>
    public static string ToneInstructionFor(string? appId)
    {
        var choice = ChoiceFor(CategoryFor(appId));
        return choice switch
        {
            "casual" => "\n\nTone for this app: casual — normal capitalization, lighter punctuation (drop optional commas and trailing periods on short messages).",
            "very-casual" => "\n\nTone for this app: very casual — all lowercase, minimal punctuation.",
            "excited" => "\n\nTone for this app: upbeat — normal capitalization with tasteful exclamation points.",
            _ => "", // formal = the default cleanup behavior
        };
    }

    public static string DisplayName(string choice) => choice switch
    {
        "very-casual" => "very casual",
        "excited" => "Excited!",
        "casual" => "Casual",
        _ => "Formal.",
    };
}
