using FluidVoice.Core;

namespace FluidVoice.Text;

/// <summary>
/// Auto-learn: watches what AI cleanup fixes relative to the raw transcript and records
/// recurring single-word "name / term" corrections. When the same correction has been seen
/// enough times it is promoted into the custom dictionary, so future transcripts get it right
/// even without AI. Inspired by OpenWhispr's correction learner; the diff/filters below are a
/// clean-room implementation (word-level LCS alignment + normalized edit-distance gate).
/// </summary>
public static class CorrectionLearner
{
    /// <summary>Compare raw STT to the AI-cleaned text and record any vocabulary corrections.</summary>
    public static void Observe(string rawTranscript, string finalText)
    {
        try
        {
            if (!Settings.Current.AutoLearnCorrections) return;
            if (string.IsNullOrWhiteSpace(rawTranscript) || string.IsNullOrWhiteSpace(finalText)) return;

            var pairs = ExtractCorrections(rawTranscript, finalText);
            if (pairs.Count == 0) return;

            var s = Settings.Current;
            bool changed = false;
            foreach (var (from, to) in pairs)
            {
                var existing = s.LearnedCorrections.FirstOrDefault(c =>
                    c.From.Equals(from, StringComparison.OrdinalIgnoreCase) &&
                    c.To.Equals(to, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    s.LearnedCorrections.Add(new LearnedCorrection { From = from, To = to, Count = 1 });
                    changed = true;
                }
                else if (!existing.Dismissed)
                {
                    existing.Count++;
                    changed = true;
                    if (!existing.Promoted && existing.Count >= Math.Max(1, s.AutoLearnThreshold))
                        changed |= Promote(existing);
                }
            }
            if (changed) s.Save("autolearn");
        }
        catch { /* learning is best-effort; never break dictation */ }
    }

    /// <summary>Add a learned correction to the custom dictionary (idempotent).</summary>
    public static bool Promote(LearnedCorrection c)
    {
        var s = Settings.Current;
        c.Promoted = true;
        bool already = s.CustomDictionaryEntries.Any(e =>
            e.Replacement.Equals(c.To, StringComparison.OrdinalIgnoreCase) &&
            e.Triggers.Any(t => t.Equals(c.From, StringComparison.OrdinalIgnoreCase)));
        if (already) return true;
        s.CustomDictionaryEntries.Add(new CustomDictionaryEntry
        {
            Triggers = new List<string> { c.From },
            Replacement = c.To,
        });
        return true;
    }

    // ---- diff ----

    /// <summary>
    /// Word-level substitutions from <paramref name="original"/>→<paramref name="corrected"/> that look
    /// like real vocabulary fixes (a near-miss spelling of the same word), not grammar edits.
    /// </summary>
    public static List<(string From, string To)> ExtractCorrections(string original, string corrected)
    {
        var a = Tokenize(original);
        var b = Tokenize(corrected);
        var result = new List<(string, string)>();
        if (a.Count == 0 || b.Count == 0) return result;

        // too much churn = a rewrite, not corrections
        var (align, changedFrac) = AlignSubstitutions(a, b);
        if (changedFrac > 0.5) return result;

        var dict = Settings.Current.CustomDictionaryEntries
            .SelectMany(e => e.Triggers).Where(t => t.Length > 0)
            .Select(t => t.ToLowerInvariant()).ToHashSet();

        foreach (var (from, to) in align)
        {
            var cleanFrom = StripPunct(from);
            var cleanTo = StripPunct(to);
            if (cleanFrom.Length < 3 || cleanTo.Length < 3) continue;
            if (cleanFrom.Equals(cleanTo, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsWordLike(cleanFrom) || !IsWordLike(cleanTo)) continue;
            if (dict.Contains(cleanFrom.ToLowerInvariant())) continue;

            // only near-miss substitutions (phonetic misspellings) — filters unrelated word swaps
            int dist = Levenshtein(cleanFrom.ToLowerInvariant(), cleanTo.ToLowerInvariant());
            double norm = (double)dist / Math.Max(cleanFrom.Length, cleanTo.Length);
            if (norm is <= 0 or > 0.65) continue;

            // vocabulary signal: the corrected word is a proper noun / capitalized term,
            // or the words are close but not a trivial function-word swap
            bool looksLikeTerm = char.IsUpper(cleanTo[0]) || cleanTo.Length >= 6;
            if (!looksLikeTerm) continue;

            result.Add((cleanFrom, cleanTo));
        }
        return result;
    }

    /// <summary>LCS-based alignment; returns the substituted (from,to) pairs and the fraction of words that changed.</summary>
    private static (List<(string From, string To)> Subs, double ChangedFrac) AlignSubstitutions(List<string> a, List<string> b)
    {
        int n = a.Count, m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i].Equals(b[j], StringComparison.OrdinalIgnoreCase)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var subs = new List<(string, string)>();
        int changed = 0, x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x].Equals(b[y], StringComparison.OrdinalIgnoreCase)) { x++; y++; continue; }
            // a deletion+insertion at the same spot = a substitution (word replaced by a similar word)
            if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                // a[x] was removed; if b[y] is also new here, treat as substitution
                if (y < m && !ExistsLater(a, x + 1, b[y])) { subs.Add((a[x], b[y])); y++; }
                x++; changed++;
            }
            else { y++; changed++; }
        }
        changed += (n - x) + (m - y);
        return (subs, (double)changed / Math.Max(n, m));
    }

    private static bool ExistsLater(List<string> words, int from, string target)
    {
        for (int i = from; i < words.Count; i++)
            if (words[i].Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static List<string> Tokenize(string s) =>
        s.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string StripPunct(string w) => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '—', '–', '-');

    private static bool IsWordLike(string w) => w.All(c => char.IsLetter(c) || c == '\'' || c == '-');

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[m];
    }
}
