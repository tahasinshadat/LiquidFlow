using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>Today-usage stats (StatsView.swift): words, time saved, sessions, streak, totals.</summary>
public sealed class HomeTab : StackPanel
{
    public HomeTab()
    {
        Build();
        HistoryStore.HistoryChanged += () => Dispatcher.BeginInvoke(Build);
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(new TextBlock
        {
            Text = "FluidVoice", FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 4),
        });
        Children.Add(Theme.Caption("Local-first voice dictation for Windows. Press your hotkey anywhere to dictate."));

        var wordsToday = HistoryStore.WordsToday;
        var timeSaved = HistoryStore.FormatMinutes(HistoryStore.TimeSavedMinutes(wordsToday));

        var cards = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 8, 0, 12) };
        cards.Children.Add(StatCard("Words today", wordsToday.ToString()));
        cards.Children.Add(StatCard("Time saved", timeSaved));
        cards.Children.Add(StatCard("Sessions today", HistoryStore.TranscriptionsToday.ToString()));
        cards.Children.Add(StatCard("Streak", $"{HistoryStore.CurrentStreakDays}🔥"));
        Children.Add(cards);

        var totals = new UniformGrid { Rows = 1, Columns = 3 };
        totals.Children.Add(StatCard("Total words", HistoryStore.TotalWords.ToString("N0")));
        totals.Children.Add(StatCard("Transcriptions", HistoryStore.Entries.Count.ToString("N0")));
        totals.Children.Add(StatCard("AI enhancement", $"{HistoryStore.AiEnhancementRate * 100:0}%"));
        Children.Add(totals);

        // top apps
        var apps = HistoryStore.TopApps();
        if (apps.Count > 0)
        {
            var panel = new StackPanel();
            panel.Children.Add(Theme.Heading("Top apps"));
            foreach (var (app, count) in apps)
                panel.Children.Add(new TextBlock
                {
                    Text = $"{app} — {count}", Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 2, 0, 2),
                });
            Children.Add(Theme.Card2(panel));
        }
    }

    private static Border StatCard(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Theme.AccentBrush });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Theme.SubtleBrush });
        return new Border
        {
            Background = Theme.CardBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(4),
            Child = panel,
        };
    }
}
