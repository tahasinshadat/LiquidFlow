using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// Insights → "Your voice": a locally computed voice profile — progress toward the next
/// refresh, a profile card, catchphrase / most-used / most-corrected cards, and a peak
/// time &amp; place card. Everything derives from on-device history; nothing leaves the PC.
/// </summary>
public static class VoicePane
{
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","so","to","of","in","on","for","with","at","by","from","up",
        "is","are","was","were","be","been","it","its","this","that","these","those","i","you","he",
        "she","we","they","me","my","your","our","their","them","him","her","as","if","then","than",
        "just","like","okay","ok","yeah","yes","no","not","do","does","did","have","has","had","can",
        "could","will","would","should","about","into","out","over","all","also","there","here","what",
        "when","where","which","who","how","why","because","going","get","got","make","made","want",
        "one","two","three","really","very","some","any","more","most","other","right","now","well",
    };

    public static UIElement Build()
    {
        var entries = HistoryStore.Entries;
        var total = HistoryStore.TotalWords;
        var host = new StackPanel();

        // ---- refresh progress ----
        int intoCycle = total % 1000;
        int remaining = Math.Max(1, 1000 - intoCycle);
        var track = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 8) };
        track.Children.Add(new Border { Background = new SolidColorBrush(Theme.SidebarSelected), CornerRadius = new CornerRadius(4) });
        var fillHost = new Grid();
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, intoCycle / 1000.0), GridUnitType.Star) });
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, remaining / 1000.0), GridUnitType.Star) });
        var fill = new Border { Background = Theme.PurpleBrush, CornerRadius = new CornerRadius(4) };
        Grid.SetColumn(fill, 0);
        fillHost.Children.Add(fill);
        track.Children.Add(fillHost);
        host.Children.Add(track);

        var meta = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };
        var created = entries.Count > 0 ? entries[^1].Timestamp : DateTime.Today;
        meta.Children.Add(new TextBlock
        {
            Text = $"Created {created:MMM d, yyyy}",
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
        });
        var next = new TextBlock
        {
            Text = $"Next update in {remaining:N0} more words  ⓘ",
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(next, Dock.Right);
        meta.Children.Add(next);
        host.Children.Add(meta);

        // ---- profile card ----
        var (profileName, blurb) = Profile(entries);
        var profile = new DockPanel();
        var avatar = new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(20),
            Background = Theme.GreenSoftBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0),
            Child = new TextBlock
            {
                Text = string.Concat(profileName.Split(' ').Take(2).Select(w => w[0])),
                FontFamily = Theme.DisplaySerif,
                FontSize = 34,
                Foreground = Theme.GreenBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(avatar, Dock.Right);
        profile.Children.Add(avatar);
        var pcopy = new StackPanel();
        pcopy.Children.Add(new TextBlock { Text = profileName, FontFamily = Theme.DisplaySerif, FontSize = 27, Foreground = Theme.TextBrush });
        pcopy.Children.Add(new TextBlock
        {
            Text = "VOICE PROFILE",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 6, 0, 12),
        });
        pcopy.Children.Add(new TextBlock
        {
            Text = blurb,
            FontSize = 14.5,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        });
        profile.Children.Add(pcopy);
        host.Children.Add(Theme.Panel(profile, new Thickness(28, 24, 28, 24), new Thickness(0, 0, 0, 22)));

        // ---- two-column stats ----
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(QuoteCard($"“{Catchphrase(entries)}”", "CATCHPHRASE"));
        left.Children.Add(QuoteCard($"“{MostUsedWord(entries)}”", "MOST USED WORD"));
        left.Children.Add(QuoteCard($"“{MostCorrectedWord()}”", "MOST CORRECTED WORD"));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(PeakCard(entries));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        host.Children.Add(grid);

        host.Children.Add(new TextBlock
        {
            Text = "Your dictations are private and only stored locally. Never shared or sent anywhere. This report is computed from local data.",
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 0),
        });
        return host;
    }

    private static UIElement QuoteCard(string big, string caption)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock
        {
            Text = big,
            FontFamily = Theme.DisplaySerif,
            FontStyle = FontStyles.Italic,
            FontSize = 24,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        p.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
        });
        return Theme.Panel(p, new Thickness(24, 20, 24, 20), new Thickness(0, 0, 0, 18));
    }

    private static UIElement PeakCard(IReadOnlyList<TranscriptionHistoryEntry> entries)
    {
        var (label, appName) = PeakTime(entries);
        var p = new StackPanel();
        p.Children.Add(new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(12),
            Background = Theme.GreenSoftBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
            Child = new TextBlock
            {
                Text = appName.Length > 0 ? appName[..1].ToUpperInvariant() : "•",
                FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Theme.GreenBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        p.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = Theme.DisplaySerif,
            FontSize = 27,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });
        p.Children.Add(new TextBlock
        {
            Text = "YOUR PEAK TIME & PLACE",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 0, 0, 14),
        });
        p.Children.Add(new TextBlock
        {
            Text = appName.Length > 0
                ? $"{label} is when you dictate most, usually into {appName} — deep-work hours where your voice does the typing."
                : "Dictate a bit more and your peak time and favorite app will show up here.",
            FontSize = 14.5,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        });
        return Theme.Panel(p, new Thickness(26, 24, 26, 24), new Thickness(0));
    }

    // ---- local analytics ----

    private static IEnumerable<string> Words(IReadOnlyList<TranscriptionHistoryEntry> entries) =>
        entries.SelectMany(e => e.ProcessedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '—', '–'))
            .Where(w => w.Length >= 3 && w.All(char.IsLetter));

    private static string MostUsedWord(IReadOnlyList<TranscriptionHistoryEntry> entries)
    {
        var top = Words(entries).Where(w => !Stop.Contains(w))
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return top?.Key ?? "hello";
    }

    private static string MostCorrectedWord()
    {
        var top = Settings.Current.LearnedCorrections.OrderByDescending(c => c.Count).FirstOrDefault();
        if (top is not null) return top.From.ToLowerInvariant();
        var dict = Settings.Current.CustomDictionaryEntries.FirstOrDefault();
        return dict?.Triggers.FirstOrDefault()?.ToLowerInvariant() ?? "—";
    }

    private static string Catchphrase(IReadOnlyList<TranscriptionHistoryEntry> entries)
    {
        // most frequent word pair with at least one non-stopword
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var words = e.ProcessedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\''))
                .Where(w => w.Length > 0).ToList();
            for (int i = 0; i + 1 < words.Count; i++)
            {
                if (Stop.Contains(words[i]) && Stop.Contains(words[i + 1])) continue;
                if (!words[i].All(char.IsLetter) || !words[i + 1].All(char.IsLetter)) continue;
                var key = $"{words[i].ToLowerInvariant()} {words[i + 1].ToLowerInvariant()}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
        var best = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
        return best.Value >= 3 ? best.Key : MostUsedWord(entries);
    }

    private static (string Label, string App) PeakTime(IReadOnlyList<TranscriptionHistoryEntry> entries)
    {
        if (entries.Count == 0) return ("Anytime", "");
        var peak = entries.GroupBy(e => (e.Timestamp.DayOfWeek, e.Timestamp.Hour))
            .OrderByDescending(g => g.Count()).First();
        var (day, hour) = peak.Key;
        var t = hour switch { 0 => "12 a.m.", 12 => "12 p.m.", < 12 => $"{hour} a.m.", _ => $"{hour - 12} p.m." };
        var app = peak.GroupBy(e => e.AppName).Where(g => g.Key.Length > 0)
            .OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "";
        return ($"{day} at {t}", app);
    }

    private static (string Name, string Blurb) Profile(IReadOnlyList<TranscriptionHistoryEntry> entries)
    {
        var topApp = HistoryStore.TopApps(1).FirstOrDefault().App?.ToLowerInvariant() ?? "";
        string name = topApp switch
        {
            var a when a.Contains("code") || a.Contains("rider") || a.Contains("terminal") || a.Contains("devenv") => "Syntax Navigator",
            var a when a.Contains("claude") || a.Contains("cursor") || a.Contains("chatgpt") => "Prompt Architect",
            var a when a.Contains("slack") || a.Contains("discord") || a.Contains("teams") => "Conversation Conductor",
            var a when a.Contains("word") || a.Contains("notion") || a.Contains("obsidian") => "Document Dynamo",
            _ => "Idea Wrangler",
        };
        string blurb = entries.Count == 0
            ? "Start dictating and LiquidFlow will sketch how your voice works — favorite words, phrasing, and where you talk the most."
            : $"Voice helps you move faster where you work most. Whether it's steering {HistoryStore.TopApps(1).FirstOrDefault().App ?? "your apps"} or thinking out loud, your dictations often untangle technical puzzles. The more you dictate, the sharper this profile gets.";
        return (name, blurb);
    }
}
