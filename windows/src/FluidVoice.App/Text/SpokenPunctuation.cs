using System.Text;

namespace FluidVoice.Text;

/// <summary>
/// Exact port of SpokenPunctuationFormatter (ASRService+SpokenPunctuationFormatting.swift):
/// tokenizes into alphanumeric word runs vs text runs, matches spoken phrases against a
/// specificity-ordered rule table, then renders with spacing classes and quote toggles,
/// removing generated comma noise between punctuation pairs.
/// </summary>
public static class SpokenPunctuation
{
    private enum Spacing { RightAttached, LeftAttached, NoSpaceAround, SpaceAround, ToggleDoubleQuote, ToggleSingleQuote }

    private sealed record PhraseRule(
        string[] Words, string Symbol, Spacing Spacing,
        bool RequiresSymbolContext = false, bool RequiresDotContext = false,
        bool RequiresSlashPathContext = false, bool RequiresAtSignApp = false);

    private abstract record Token
    {
        public sealed record Word(string Original, string Normalized) : Token;
        public sealed record Text(string Value) : Token;

        public string? NormalizedWord => (this as Word)?.Normalized;
        public string Raw => this switch { Word w => w.Original, Text t => t.Value, _ => "" };
        public bool IsHorizontalWhitespace => this is Text t && t.Value.Length > 0 && t.Value.All(IsHws);
    }

    private abstract record Part
    {
        public sealed record Text(string Value) : Part;
        public sealed record Punct(string Symbol, Spacing Spacing) : Part;
        public bool IsHorizontalWhitespace => this is Text t && t.Value.Length > 0 && t.Value.All(IsHws);
    }

    private static bool IsHws(char c) => c is ' ' or '\t' or ' ';
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static readonly Dictionary<string, List<PhraseRule>> RulesByFirstWord = BuildRules();

    private static readonly HashSet<char> SymbolCommaCleanupChars = new("+=%-—–/\\@#$&*_|~^<>");
    private static readonly HashSet<char> PathContextChars = new("./\\:@_~");
    private static readonly HashSet<char> PunctPairCommaCleanupChars = new("+=%-—–/\\@#$&*_|~^<>()[]{}\"'`.?!:;");

    private static readonly HashSet<string> DotSuffixWords = new()
    {
        "ai","app","c","ca","co","com","cpp","css","dev","edu","go","gov","h","hpp","html","in","io","js",
        "json","md","me","mm","net","org","plist","py","rb","rs","sh","swift","ts","txt","uk","us","xml","yaml","yml","zip",
    };

    private static readonly HashSet<string> DotPrefixWords = new()
    {
        "api","app","cdn","docs","file","ftp","http","https","localhost","server","staging","v1","v2","v3","web","www",
    };

    private static readonly HashSet<string> DotRejectedPreviousWords = new()
    {
        "a","an","my","our","that","the","their","this","your",
    };

    private static readonly HashSet<string> SlashPathContextWords = new()
    {
        "api","applications","bin","desktop","documents","downloads","etc","file","files","folder","home","http",
        "https","lib","library","local","path","private","src","source","sources","tmp","url","user","users",
        "usr","var","volumes","www",
    };

    // apps where "at sign" converts (coding/chat apps; ASRService+SpokenPunctuationFormatting.swift:26-47,
    // with Windows equivalents added for the mac-only terminals)
    private static readonly string[] AtSignAppNeedles =
    {
        "codex", "chatgpt", "claude", "cursor", "windsurf", "visual studio", "vscode", "code", "terminal",
        "windowsterminal", "cmd", "powershell", "pwsh", "warp", "slack", "discord", "teams",
    };

    public static string Apply(string text, string? appName = null, string? windowTitle = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var tokens = Tokenize(text);
        if (!tokens.Any(t => t.NormalizedWord is not null)) return text;

        bool isAtSignApp = IsAtSignApp(appName, windowTitle);
        var output = new List<Part>();
        int index = 0;
        while (index < tokens.Count)
        {
            var match = MatchRule(tokens, index, isAtSignApp);
            if (match is { } m)
            {
                output.Add(new Part.Punct(m.Rule.Symbol, m.Rule.Spacing));
                index = m.EndIndex;
            }
            else
            {
                output.Add(new Part.Text(tokens[index].Raw));
                index++;
            }
        }
        return Render(RemoveDuplicateSentencePunctuation(RemoveGeneratedCommaNoise(output)));
    }

    /// <summary>
    /// Windows addition: Whisper transcribes "dictation period" as "dictation period."
    /// (it punctuates the spoken word itself), which would render as "dictation..".
    /// Strip STT-added sentence punctuation directly adjacent to a generated symbol.
    /// </summary>
    private static List<Part> RemoveDuplicateSentencePunctuation(List<Part> parts)
    {
        const string dupChars = ".,!?;:";
        var result = new List<Part>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is Part.Text t && t.Value.Length > 0)
            {
                var value = t.Value;
                // generated punct BEFORE this text: strip leading duplicates ("." then ".")
                if (PreviousSignificantIsPunct(result))
                    value = value.TrimStart(dupChars.ToCharArray());
                // generated punct AFTER this text: strip trailing duplicates ("great!" then "!")
                if (NextSignificantIsPunct(parts, i))
                    value = value.TrimEnd(dupChars.ToCharArray());
                if (value.Length == 0) continue;
                result.Add(new Part.Text(value));
            }
            else
            {
                result.Add(parts[i]);
            }
        }
        return result;

        static bool PreviousSignificantIsPunct(List<Part> soFar)
        {
            for (int c = soFar.Count - 1; c >= 0; c--)
            {
                if (soFar[c].IsHorizontalWhitespace) continue;
                return soFar[c] is Part.Punct p && p.Symbol.Length > 0 && ".,!?;:".Contains(p.Symbol[0]);
            }
            return false;
        }

        static bool NextSignificantIsPunct(List<Part> all, int index)
        {
            for (int c = index + 1; c < all.Count; c++)
            {
                if (all[c].IsHorizontalWhitespace) continue;
                return all[c] is Part.Punct p && p.Symbol.Length > 0 && ".,!?;:".Contains(p.Symbol[0]);
            }
            return false;
        }
    }

    private static bool IsAtSignApp(string? appName, string? windowTitle)
    {
        var haystack = ((appName ?? "") + " " + (windowTitle ?? "")).ToLowerInvariant();
        return AtSignAppNeedles.Any(haystack.Contains);
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var current = new StringBuilder();
        bool buildingWord = false;

        void Flush()
        {
            if (current.Length == 0) return;
            var s = current.ToString();
            tokens.Add(buildingWord ? new Token.Word(s, s.ToLowerInvariant()) : new Token.Text(s));
            current.Clear();
        }

        foreach (var c in text)
        {
            bool isWord = IsWordChar(c);
            if (current.Length == 0)
            {
                current.Append(c);
                buildingWord = isWord;
            }
            else if (isWord == buildingWord)
            {
                current.Append(c);
            }
            else
            {
                Flush();
                current.Append(c);
                buildingWord = isWord;
            }
        }
        Flush();
        return tokens;
    }

    private static (PhraseRule Rule, int EndIndex)? MatchRule(List<Token> tokens, int index, bool isAtSignApp)
    {
        var firstWord = tokens[index].NormalizedWord;
        if (firstWord is null || !RulesByFirstWord.TryGetValue(firstWord, out var candidates)) return null;

        foreach (var rule in candidates)
        {
            int cursor = index;
            bool matched = true;
            for (int wi = 0; wi < rule.Words.Length; wi++)
            {
                if (wi > 0)
                {
                    if (cursor >= tokens.Count || !tokens[cursor].IsHorizontalWhitespace) { matched = false; break; }
                    while (cursor < tokens.Count && tokens[cursor].IsHorizontalWhitespace) cursor++;
                }
                if (cursor >= tokens.Count || tokens[cursor].NormalizedWord != rule.Words[wi]) { matched = false; break; }
                cursor++;
            }
            if (!matched) continue;
            if (rule.RequiresSymbolContext && !HasSymbolContext(tokens, index, cursor)) continue;
            if (rule.RequiresDotContext && !HasDotContext(tokens, index, cursor)) continue;
            if (rule.RequiresSlashPathContext && !HasSlashPathContext(tokens, index, cursor)) continue;
            if (rule.RequiresAtSignApp && !isAtSignApp) continue;
            return (rule, cursor);
        }
        return null;
    }

    private static int? SignificantIndexBefore(List<Token> tokens, int index)
    {
        for (int cursor = index - 1; cursor >= 0; cursor--)
            if (!tokens[cursor].IsHorizontalWhitespace) return cursor;
        return null;
    }

    private static int? SignificantIndexAtOrAfter(List<Token> tokens, int index)
    {
        for (int cursor = index; cursor < tokens.Count; cursor++)
            if (!tokens[cursor].IsHorizontalWhitespace) return cursor;
        return null;
    }

    private static bool IsSymbolContextToken(Token token) => token switch
    {
        Token.Word w => RulesByFirstWord.TryGetValue(w.Normalized, out var rules) &&
                        rules.Any(r => r.Symbol != "," && r.Symbol != "."),
        Token.Text t => t.Value.Any(SymbolCommaCleanupChars.Contains),
        _ => false,
    };

    private static bool IsShortSymbolOperand(Token token) => token switch
    {
        Token.Word w => w.Normalized.Length <= 2 || w.Normalized.All(IsAsciiDigit),
        Token.Text t => t.Value.Any(SymbolCommaCleanupChars.Contains),
        _ => false,
    };

    private static bool HasSymbolContext(List<Token> tokens, int start, int end)
    {
        var pi = SignificantIndexBefore(tokens, start);
        var ni = SignificantIndexAtOrAfter(tokens, end);
        if (pi is { } p && ni is { } n)
            return IsSymbolContextToken(tokens[p]) || IsSymbolContextToken(tokens[n]) ||
                   (IsShortSymbolOperand(tokens[p]) && IsShortSymbolOperand(tokens[n]));
        if (pi is { } p2) return IsSymbolContextToken(tokens[p2]);
        if (ni is { } n2) return IsSymbolContextToken(tokens[n2]);
        return false;
    }

    private static bool IsPathSymbolText(Token token) =>
        token is Token.Text t && t.Value.Any(PathContextChars.Contains);

    private static bool HasDotContext(List<Token> tokens, int start, int end)
    {
        var pi = SignificantIndexBefore(tokens, start);
        var ni = SignificantIndexAtOrAfter(tokens, end);
        var prev = pi is { } p ? tokens[p] : null;
        var next = ni is { } n ? tokens[n] : null;

        if ((prev is not null && IsPathSymbolText(prev)) || (next is not null && IsPathSymbolText(next))) return true;

        var prevWord = prev?.NormalizedWord;
        var nextWord = next?.NormalizedWord;
        if (prevWord is not null && nextWord is not null)
        {
            if (DotSuffixWords.Contains(nextWord)) return !DotRejectedPreviousWords.Contains(prevWord);
            if (DotPrefixWords.Contains(prevWord)) return true;
            return IsShortSymbolOperand(tokens[pi ?? start]) && IsShortSymbolOperand(tokens[ni ?? end]);
        }
        if (prevWord is not null) return DotPrefixWords.Contains(prevWord);
        if (nextWord is not null) return DotSuffixWords.Contains(nextWord);
        return false;
    }

    private static bool IsSlashPathContextToken(Token token) => token switch
    {
        Token.Word w => SlashPathContextWords.Contains(w.Normalized) || DotSuffixWords.Contains(w.Normalized) ||
                        (w.Normalized.Length > 0 && w.Normalized.All(IsAsciiDigit)),
        _ => IsPathSymbolText(token),
    };

    private static bool IsSpokenSlashToken(Token token) =>
        token.NormalizedWord is "slash" or "forwardslash";

    private static bool HasSlashContextTokenBefore(List<Token> tokens, int index) =>
        SignificantIndexBefore(tokens, index) is { } i && IsSlashPathContextToken(tokens[i]);

    private static bool HasSlashContextTokenAtOrAfter(List<Token> tokens, int index) =>
        SignificantIndexAtOrAfter(tokens, index) is { } i && IsSlashPathContextToken(tokens[i]);

    private static bool HasSlashPathContext(List<Token> tokens, int start, int end)
    {
        if (HasSlashContextTokenBefore(tokens, start) || HasSlashContextTokenAtOrAfter(tokens, end)) return true;

        if (SignificantIndexBefore(tokens, start) is { } prevIdx && IsSpokenSlashToken(tokens[prevIdx]))
            return HasSlashContextTokenBefore(tokens, prevIdx);

        if (SignificantIndexAtOrAfter(tokens, end) is { } nextIdx && IsSpokenSlashToken(tokens[nextIdx]))
            return HasSlashContextTokenAtOrAfter(tokens, nextIdx + 1);

        return false;
    }

    // ---- comma-noise cleanup ----

    private static List<Part> RemoveGeneratedCommaNoise(List<Part> parts)
    {
        if (!parts.Any(p => p is Part.Punct { Symbol: "," })) return parts;
        var result = new List<Part>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is Part.Punct { Symbol: "," } && ShouldRemoveGeneratedComma(parts, i)) continue;
            result.Add(parts[i]);
        }
        return result;
    }

    private static bool ShouldRemoveGeneratedComma(List<Part> parts, int index)
    {
        Part? prev = null, next = null;
        for (int c = index - 1; c >= 0; c--)
            if (!parts[c].IsHorizontalWhitespace) { prev = parts[c]; break; }
        for (int c = index + 1; c < parts.Count; c++)
            if (!parts[c].IsHorizontalWhitespace) { next = parts[c]; break; }

        if (prev is Part.Punct pp && next is Part.Punct np &&
            pp.Symbol.Length > 0 && np.Symbol.Length > 0 &&
            PunctPairCommaCleanupChars.Contains(pp.Symbol[0]) && PunctPairCommaCleanupChars.Contains(np.Symbol[0]))
            return true;

        if (next is Part.Punct { Symbol: "%" } && prev is Part.Text pt && pt.Value.Length > 0 && IsAsciiDigit(pt.Value[^1]))
            return true;

        return false;
    }

    // ---- rendering ----

    private static string Render(List<Part> parts)
    {
        var result = new StringBuilder();
        int index = 0;
        bool openDoubleQuote = true;
        bool openSingleQuote = true;

        while (index < parts.Count)
        {
            switch (parts[index])
            {
                case Part.Text t:
                    result.Append(t.Value);
                    index++;
                    break;
                case Part.Punct p:
                    var spacing = p.Spacing switch
                    {
                        Spacing.ToggleDoubleQuote => Toggle(ref openDoubleQuote),
                        Spacing.ToggleSingleQuote => Toggle(ref openSingleQuote),
                        var s => s,
                    };
                    switch (spacing)
                    {
                        case Spacing.RightAttached:
                            TrimTrailingHws(result);
                            result.Append(p.Symbol);
                            index++;
                            break;
                        case Spacing.LeftAttached:
                            result.Append(p.Symbol);
                            index = SkipWhitespace(parts, index);
                            break;
                        case Spacing.NoSpaceAround:
                            TrimTrailingHws(result);
                            result.Append(p.Symbol);
                            index = SkipWhitespace(parts, index);
                            break;
                        case Spacing.SpaceAround:
                            TrimTrailingHws(result);
                            if (result.Length > 0 && result[^1] != '\n') result.Append(' ');
                            result.Append(p.Symbol);
                            index = SkipWhitespace(parts, index);
                            if (HasFollowingNonWhitespace(parts, index)) result.Append(' ');
                            break;
                        default:
                            index++;
                            break;
                    }
                    break;
            }
        }
        return result.ToString();

        static Spacing Toggle(ref bool open)
        {
            var s = open ? Spacing.LeftAttached : Spacing.RightAttached;
            open = !open;
            return s;
        }
    }

    private static void TrimTrailingHws(StringBuilder sb)
    {
        while (sb.Length > 0 && IsHws(sb[^1])) sb.Length--;
    }

    private static int SkipWhitespace(List<Part> parts, int index)
    {
        int next = index + 1;
        while (next < parts.Count && parts[next] is Part.Text t && t.Value.Length > 0 && t.Value.All(IsHws))
            next++;
        return next;
    }

    private static bool HasFollowingNonWhitespace(List<Part> parts, int index)
    {
        for (int i = index; i < parts.Count; i++)
        {
            switch (parts[i])
            {
                case Part.Text t when t.Value.Any(c => !IsHws(c)): return true;
                case Part.Punct: return true;
            }
        }
        return false;
    }

    // ---- rule table (verbatim from makeRules(), ASRService+SpokenPunctuationFormatting.swift:145-366) ----

    private static Dictionary<string, List<PhraseRule>> BuildRules()
    {
        var all = new List<PhraseRule>();
        void Add(string symbol, Spacing spacing, string[] phrases,
            bool symbolCtx = false, bool dotCtx = false, bool slashCtx = false, bool atApp = false)
        {
            foreach (var phrase in phrases)
            {
                var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant()).ToArray();
                if (words.Length > 0)
                    all.Add(new PhraseRule(words, symbol, spacing, symbolCtx, dotCtx, slashCtx, atApp));
            }
        }

        Add(",", Spacing.RightAttached, new[] { "comma" });
        Add(".", Spacing.RightAttached, new[] { "period", "full stop" });
        Add(".", Spacing.NoSpaceAround, new[] { "dot" }, dotCtx: true);
        Add("?", Spacing.RightAttached, new[] { "question mark", "questionmark" });
        Add("!", Spacing.RightAttached, new[] { "exclamation mark", "exclamation point", "bang" });
        Add(":", Spacing.RightAttached, new[] { "colon" });
        Add(";", Spacing.RightAttached, new[] { "semicolon", "semi colon" });
        Add("...", Spacing.RightAttached, new[] { "ellipsis", "dot dot dot", "three dots" });
        Add("/", Spacing.NoSpaceAround, new[] { "slash", "forward slash", "forwardslash" }, slashCtx: true);
        Add("\\", Spacing.NoSpaceAround, new[] { "backslash", "back slash" });
        Add("-", Spacing.NoSpaceAround, new[] { "hyphen" });
        Add("-", Spacing.SpaceAround, new[] { "dash", "minus sign" });
        Add("—", Spacing.SpaceAround, new[] { "em dash", "long dash" });
        Add("–", Spacing.SpaceAround, new[] { "en dash" });
        Add("(", Spacing.LeftAttached, new[] { "open parenthesis", "open parentheses", "left parenthesis", "left parentheses", "open paren", "left paren" });
        Add(")", Spacing.RightAttached, new[] { "close parenthesis", "close parentheses", "right parenthesis", "right parentheses", "close paren", "right paren" });
        Add("[", Spacing.LeftAttached, new[] { "open bracket", "left bracket", "open square bracket", "left square bracket" });
        Add("]", Spacing.RightAttached, new[] { "close bracket", "right bracket", "close square bracket", "right square bracket" });
        Add("{", Spacing.LeftAttached, new[] { "open brace", "left brace", "open curly brace", "left curly brace", "open curly bracket", "left curly bracket" });
        Add("}", Spacing.RightAttached, new[] { "close brace", "right brace", "close curly brace", "right curly brace", "close curly bracket", "right curly bracket" });
        Add("<", Spacing.LeftAttached, new[] { "open angle bracket", "left angle bracket", "less than sign" });
        Add(">", Spacing.RightAttached, new[] { "close angle bracket", "right angle bracket", "greater than sign" });
        Add("\"", Spacing.ToggleDoubleQuote, new[] { "quote", "quotes", "quotation mark", "double quote" });
        Add("\"", Spacing.LeftAttached, new[] { "open quote", "opening quote", "open double quote", "opening double quote" });
        Add("\"", Spacing.RightAttached, new[] { "close quote", "closing quote", "close double quote", "closing double quote" });
        Add("'", Spacing.ToggleSingleQuote, new[] { "single quote" });
        Add("'", Spacing.NoSpaceAround, new[] { "apostrophe" });
        Add("@", Spacing.NoSpaceAround, new[] { "at the rate" });
        Add("@", Spacing.NoSpaceAround, new[] { "at sign", "commercial at" }, atApp: true);
        Add("&", Spacing.SpaceAround, new[] { "ampersand", "and sign" });
        Add("+", Spacing.SpaceAround, new[] { "plus sign" });
        Add("+", Spacing.SpaceAround, new[] { "plus" }, symbolCtx: true);
        Add("=", Spacing.SpaceAround, new[] { "equals sign", "equal sign" });
        Add("=", Spacing.SpaceAround, new[] { "equal", "equals" }, symbolCtx: true);
        Add("%", Spacing.RightAttached, new[] { "percent sign", "percentage sign", "percent" });
        Add("$", Spacing.LeftAttached, new[] { "dollar sign", "dollar" });
        Add("#", Spacing.NoSpaceAround, new[] { "hash", "hash sign", "hashtag", "pound sign", "number sign" });
        Add("*", Spacing.NoSpaceAround, new[] { "asterisk", "star symbol" });
        Add("_", Spacing.NoSpaceAround, new[] { "underscore" });
        Add("|", Spacing.NoSpaceAround, new[] { "pipe", "vertical bar" });
        Add("~", Spacing.NoSpaceAround, new[] { "tilde" });
        Add("^", Spacing.NoSpaceAround, new[] { "caret" });
        Add("`", Spacing.NoSpaceAround, new[] { "backtick", "back tick" });

        return all
            .GroupBy(r => r.Words[0])
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Words.Length)
                      .ThenByDescending(r => string.Join(" ", r.Words).Length)
                      .ToList());
    }
}
