using System.Text.RegularExpressions;
using FluidVoice.Core;

namespace FluidVoice.Text;

/// <summary>
/// Local post-processing pipeline, applied to every transcript before AI/typing,
/// in the exact mac order (ASRService.swift:1507-1513):
///   1. remove filler words   2. custom dictionary   3. spoken punctuation.
/// Plus applyGAAVFormatting (post-LLM cleanup) and continuous-dictation helpers.
/// </summary>
public static class TranscriptFormatter
{
    private static List<(Regex Pattern, string Replacement)>? _dictionaryCache;
    private static readonly object CacheSync = new();

    static TranscriptFormatter()
    {
        Settings.Changed += _ => InvalidateDictionaryCache();
    }

    public static string Process(string rawText, string? appName = null, string? windowTitle = null)
    {
        var text = rawText.Trim();
        if (text.Length == 0) return text;
        text = RemoveFillerWords(text);
        text = ApplyCustomDictionary(text);
        text = ApplySnippets(text);
        if (Settings.Current.AutoConvertPunctuationEnabled)
            text = SpokenPunctuation.Apply(text, appName, windowTitle);
        return text;
    }

    /// <summary>Voice snippets: a spoken trigger word/phrase expands to its saved text.</summary>
    public static string ApplySnippets(string text)
    {
        var snippets = Settings.Current.Snippets;
        if (snippets.Count == 0) return text;
        foreach (var s in snippets)
        {
            var trigger = s.Trigger.Trim();
            if (trigger.Length == 0 || string.IsNullOrEmpty(s.Text)) continue;
            try
            {
                text = Regex.Replace(text, $@"\b{Regex.Escape(trigger)}\b",
                    s.Text.Replace("$", "$$"), RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warn("formatter", $"Bad snippet trigger '{trigger}': {ex.Message}");
            }
        }
        return text;
    }

    /// <summary>Split on spaces; drop words whose punctuation-trimmed lowercase form is a filler (ASRService.swift:3333).</summary>
    public static string RemoveFillerWords(string text)
    {
        if (!Settings.Current.RemoveFillerWordsEnabled) return text;
        var fillers = Settings.Current.FillerWords
            .Select(f => f.Trim().ToLowerInvariant())
            .Where(f => f.Length > 0)
            .ToHashSet();
        if (fillers.Count == 0) return text;
        var words = text.Split(' ')
            .Where(w => !fillers.Contains(w.Trim('.', ',', '!', '?', ';', ':').ToLowerInvariant()));
        return string.Join(" ", words);
    }

    /// <summary>Word-boundary, case-insensitive trigger→replacement, regexes cached (ASRService.swift:3348-3413).</summary>
    public static string ApplyCustomDictionary(string text)
    {
        var cache = GetDictionaryCache();
        if (cache.Count == 0) return text;
        foreach (var (pattern, replacement) in cache)
            text = pattern.Replace(text, replacement);
        // deletion rules can leave a doubled or edge space — tidy it (only the dictation
        // pipeline calls this, and its output is the final text, so trimming is safe here)
        text = Regex.Replace(text, "[ \\t]{2,}", " ").Trim();
        return text;
    }

    private static List<(Regex, string)> GetDictionaryCache()
    {
        lock (CacheSync)
        {
            if (_dictionaryCache is not null) return _dictionaryCache;
            var cache = new List<(Regex, string)>();
            foreach (var entry in Settings.Current.CustomDictionaryEntries)
            {
                // A delete entry removes the trigger word; otherwise it needs a replacement.
                if (!entry.Delete && string.IsNullOrWhiteSpace(entry.Replacement)) continue;
                foreach (var trigger in entry.Triggers)
                {
                    var trimmed = trigger.Trim();
                    if (trimmed.Length == 0) continue;
                    try
                    {
                        if (entry.Delete)
                        {
                            // remove the word plus one adjacent space so no double-gap is left
                            var regex = new Regex($@"\s?\b{Regex.Escape(trimmed)}\b",
                                RegexOptions.IgnoreCase | RegexOptions.Compiled);
                            cache.Add((regex, ""));
                        }
                        else
                        {
                            var regex = new Regex($@"\b{Regex.Escape(trimmed)}\b",
                                RegexOptions.IgnoreCase | RegexOptions.Compiled);
                            cache.Add((regex, entry.Replacement.Replace("$", "$$")));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("formatter", $"Bad dictionary trigger '{trimmed}': {ex.Message}");
                    }
                }
            }
            _dictionaryCache = cache;
            return cache;
        }
    }

    public static void InvalidateDictionaryCache()
    {
        lock (CacheSync) _dictionaryCache = null;
    }

    /// <summary>Post-LLM cleanup (applyGAAVFormatting, ASRService.swift:3420-3435).</summary>
    public static string ApplyGaavFormatting(string text)
    {
        if (text.Length == 0) return text;
        var result = text;
        if (Settings.Current.GaavRemoveTrailingPeriodEnabled && result.EndsWith('.'))
            result = result[..^1];
        if (Settings.Current.GaavLowercaseFirstLetterEnabled && result.Length > 0 && char.IsUpper(result[0]))
            result = char.ToLowerInvariant(result[0]) + result[1..];
        return result;
    }

    /// <summary>Smart caps + spacing for chained dictation (applyContinuousDictationFormatting, ASRService.swift:3443-3475).</summary>
    public static string ApplyContinuousFormatting(string text, string precedingText)
    {
        if (text.Length == 0) return text;
        bool spacing = Settings.Current.ContinuousDictationSpacingEnabled;
        bool smartCaps = Settings.Current.ContextAwareCapitalizationEnabled;
        if (!spacing && !smartCaps) return text;

        var result = text;
        if (smartCaps)
        {
            var trimmedPrev = precedingText.TrimEnd();
            char? boundary = trimmedPrev.Length > 0 ? trimmedPrev[^1] : null;
            bool capitalize = boundary is null || boundary is '.' or '!' or '?' or '\n';
            var first = result.FirstOrDefault(char.IsLetter);
            if (first != default)
            {
                int idx = result.IndexOf(first);
                var replaced = capitalize ? char.ToUpperInvariant(first) : char.ToLowerInvariant(first);
                result = result[..idx] + replaced + result[(idx + 1)..];
            }
        }
        if (spacing)
        {
            if (precedingText.Length > 0 && !char.IsWhiteSpace(precedingText[^1]) &&
                result.Length > 0 && !char.IsWhiteSpace(result[0]))
                result = " " + result;
            if (result.Length > 0 && !char.IsWhiteSpace(result[^1]))
                result += " ";
        }
        return result;
    }
}
