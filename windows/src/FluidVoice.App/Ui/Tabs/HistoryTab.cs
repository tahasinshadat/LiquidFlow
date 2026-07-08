using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Transcription history: search, copy, re-paste, delete (TranscriptionHistoryView.swift).
/// The chrome (heading, search box, toggles) is built once and never reparented; only the
/// grouped entries list rebuilds on search/history change — so typing keeps focus and
/// nothing throws "already the logical child of another element".
/// </summary>
public sealed class HistoryTab : StackPanel
{
    private readonly StackPanel _entriesHost = new();
    private string _query = "";

    public HistoryTab()
    {
        Children.Add(Theme.Heading("History"));

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var search = new TextBox
        {
            Padding = new Thickness(9, 7, 9, 7),
            MinWidth = 260,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search transcriptions",
        };
        search.TextChanged += (_, _) => { _query = search.Text; RenderEntries(); };
        var clearBtn = new Button { Content = "Clear all", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right };
        clearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Delete all transcription history?", "LiquidFlow", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                HistoryStore.ClearAll();
        };
        DockPanel.SetDock(clearBtn, Dock.Right);
        top.Children.Add(clearBtn);
        top.Children.Add(search);
        Children.Add(top);

        var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        opts.Children.Add(Theme.Toggle("Save history", Settings.Current.SaveTranscriptionHistory, v => { Settings.Current.SaveTranscriptionHistory = v; Settings.Current.Save(); }));
        var audioToggle = Theme.Toggle("Save audio", Settings.Current.SaveAudioWithTranscriptionHistory, v => { Settings.Current.SaveAudioWithTranscriptionHistory = v; Settings.Current.Save(); });
        audioToggle.Margin = new Thickness(18, 4, 0, 4);
        opts.Children.Add(audioToggle);
        Children.Add(opts);

        Children.Add(_entriesHost);
        RenderEntries();

        HistoryStore.HistoryChanged += OnHistoryChanged;
        Unloaded += (_, _) => HistoryStore.HistoryChanged -= OnHistoryChanged;
    }

    private void OnHistoryChanged() => Dispatcher.BeginInvoke(RenderEntries);

    private void RenderEntries()
    {
        _entriesHost.Children.Clear();

        var entries = HistoryStore.Search(_query).Take(200).ToList();
        if (entries.Count == 0)
        {
            _entriesHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_query) ? "No transcriptions yet." : "No matches.",
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(2, 12, 0, 0),
            });
            return;
        }

        // grouped by day bucket (OpenWhispr-style): Today / Yesterday / This week / This month / month-year
        string? currentGroup = null;
        foreach (var entry in entries)
        {
            var group = DateGroup(entry.Timestamp);
            if (group != currentGroup)
            {
                currentGroup = group;
                var eyebrow = Theme.Eyebrow(group);
                eyebrow.Margin = new Thickness(2, 14, 0, 8);
                _entriesHost.Children.Add(eyebrow);
            }
            _entriesHost.Children.Add(EntryCard(entry));
        }
    }

    private static string DateGroup(DateTime ts)
    {
        var today = DateTime.Today;
        var day = ts.Date;
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        if (day > today.AddDays(-7)) return "This week";
        if (day.Year == today.Year && day.Month == today.Month) return "This month";
        return ts.ToString("MMMM yyyy");
    }

    private Border EntryCard(TranscriptionHistoryEntry entry)
    {
        var panel = new StackPanel();
        var when = entry.Timestamp.Date >= DateTime.Today.AddDays(-1)
            ? entry.Timestamp.ToString("t")   // inside Today/Yesterday groups the date is redundant
            : entry.Timestamp.ToString("g");
        var meta = $"{when}  ·  {entry.AppName}  ·  {entry.WordCount} words";
        if (entry.WasAIProcessed) meta += "  ·  AI";
        if (entry.WasCancelled) meta += "  ·  cancelled";
        panel.Children.Add(new TextBlock { Text = meta, Foreground = Theme.SubtleBrush, FontSize = 11 });
        panel.Children.Add(new TextBlock
        {
            Text = entry.ProcessedText, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copyBtn = new Button { Content = "Copy", Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 6, 0) };
        copyBtn.Click += (_, _) => ClipboardService.SetText(entry.ProcessedText);
        var pasteBtn = new Button { Content = "Paste", Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 6, 0) };
        pasteBtn.Click += (_, _) =>
        {
            var target = FocusTracker.Capture();
            Task.Run(() => TypingService.TypeTextInstantly(entry.ProcessedText, target));
        };
        var delBtn = new Button { Content = "Delete", Padding = new Thickness(11, 4, 11, 4) };
        delBtn.Click += (_, _) => HistoryStore.DeleteEntries(new[] { entry.Id });
        buttons.Children.Add(copyBtn);
        buttons.Children.Add(pasteBtn);
        buttons.Children.Add(delBtn);
        panel.Children.Add(buttons);

        return Theme.Card2(panel);
    }
}
