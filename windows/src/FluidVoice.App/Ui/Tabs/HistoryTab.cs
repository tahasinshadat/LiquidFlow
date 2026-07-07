using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>Transcription history: search, copy, re-paste, delete, export audio (TranscriptionHistoryView.swift).</summary>
public sealed class HistoryTab : StackPanel
{
    private readonly TextBox _search = new();

    public HistoryTab()
    {
        HistoryStore.HistoryChanged += () => Dispatcher.BeginInvoke(() => Build(_search.Text));
        Build("");
    }

    private void Build(string query)
    {
        Children.Clear();

        Children.Add(Theme.Heading("History"));

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _search.Text = query;
        _search.Padding = new Thickness(6);
        _search.MinWidth = 240;
        _search.TextChanged += (_, _) => Rebuild();
        var clearBtn = new Button { Content = "Clear all", Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Right };
        clearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Delete all transcription history?", "FluidVoice", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                HistoryStore.ClearAll();
        };
        DockPanel.SetDock(clearBtn, Dock.Right);
        top.Children.Add(clearBtn);
        top.Children.Add(_search);
        Children.Add(top);

        // history settings
        var opts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        opts.Children.Add(Theme.Toggle("Save history", Settings.Current.SaveTranscriptionHistory, v => { Settings.Current.SaveTranscriptionHistory = v; Settings.Current.Save(); }));
        var audioToggle = Theme.Toggle("Save audio", Settings.Current.SaveAudioWithTranscriptionHistory, v => { Settings.Current.SaveAudioWithTranscriptionHistory = v; Settings.Current.Save(); });
        audioToggle.Margin = new Thickness(16, 4, 0, 4);
        opts.Children.Add(audioToggle);
        Children.Add(opts);

        var entries = HistoryStore.Search(query).Take(200).ToList();
        if (entries.Count == 0)
        {
            Children.Add(new TextBlock { Text = "No transcriptions yet.", Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 12, 0, 0) });
            return;
        }

        foreach (var entry in entries)
            Children.Add(EntryCard(entry));
    }

    private void Rebuild() => Build(_search.Text);

    private Border EntryCard(TranscriptionHistoryEntry entry)
    {
        var panel = new StackPanel();
        var meta = $"{entry.Timestamp:g}  ·  {entry.AppName}  ·  {entry.WordCount} words";
        if (entry.WasAIProcessed) meta += "  ·  AI";
        panel.Children.Add(new TextBlock { Text = meta, Foreground = Theme.SubtleBrush, FontSize = 11 });
        panel.Children.Add(new TextBlock
        {
            Text = entry.ProcessedText, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copyBtn = new Button { Content = "Copy", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        copyBtn.Click += (_, _) => ClipboardService.SetText(entry.ProcessedText);
        var pasteBtn = new Button { Content = "Paste", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        pasteBtn.Click += (_, _) =>
        {
            var target = FocusTracker.Capture();
            Task.Run(() => TypingService.TypeTextInstantly(entry.ProcessedText, target));
        };
        var delBtn = new Button { Content = "Delete", Padding = new Thickness(10, 4, 10, 4) };
        delBtn.Click += (_, _) => HistoryStore.DeleteEntries(new[] { entry.Id });
        buttons.Children.Add(copyBtn);
        buttons.Children.Add(pasteBtn);
        buttons.Children.Add(delBtn);
        panel.Children.Add(buttons);

        return Theme.Card2(panel);
    }
}
