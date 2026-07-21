using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// Scratchpad: quick notes you want to come back to. Hero + "Start new note" + a Recents
/// grid of note cards; notes open in the floating NoteWindow (dictate straight into it).
/// </summary>
public sealed class ScratchpadTab : StackPanel
{
    private readonly WrapPanel _recents = new();

    public ScratchpadTab()
    {
        Children.Add(BuildHeader());
        Children.Add(BuildHero());
        Children.Add(BuildRecentsHeader());
        Children.Add(_recents);
        RebuildRecents();
        Loaded += (_, _) => NotesStore.Changed += OnNotesChanged;
        Unloaded += (_, _) => NotesStore.Changed -= OnNotesChanged;
    }

    private UIElement BuildHeader()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 42) };
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(new TextBlock
        {
            Text = "Add to Flow Bar",
            FontSize = 14,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        controls.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(8, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Keep Scratchpad within reach while you work",
        });
        var pinned = new CheckBox
        {
            IsChecked = Settings.Current.ScratchpadPinned,
            Content = null,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        pinned.Checked += (_, _) =>
        {
            Settings.Current.ScratchpadPinned = true;
            Settings.Current.Save("scratchpad");
        };
        pinned.Unchecked += (_, _) =>
        {
            Settings.Current.ScratchpadPinned = false;
            Settings.Current.Save("scratchpad");
        };
        controls.Children.Add(pinned);

        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "your hotkey";
        var shortcut = new Border
        {
            Background = new SolidColorBrush(Theme.SidebarSelected),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = Cursors.Hand,
            ToolTip = "Open a floating note",
            Child = new TextBlock
            {
                Text = $"{hotkey} to dictate",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.SubtleBrush,
            },
        };
        shortcut.MouseLeftButtonUp += (_, _) => NoteWindow.OpenNote(null);
        controls.Children.Add(shortcut);
        DockPanel.SetDock(controls, Dock.Right);
        row.Children.Add(controls);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "Scratchpad",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
        });
        var chip = Theme.Pill("Beta", Theme.InkBrush, new System.Windows.Media.SolidColorBrush(Theme.InkText), 11);
        chip.Margin = new Thickness(12, 4, 0, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(chip);
        row.Children.Add(left);
        return row;
    }

    private void OnNotesChanged() => Dispatcher.BeginInvoke(RebuildRecents);

    private UIElement BuildHero()
    {
        var content = new StackPanel { Margin = new Thickness(44, 36, 44, 36), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(new TextBlock
        {
            Text = "For quick thoughts you want to come back to",
            FontFamily = Theme.DisplaySerif,
            FontSize = 30,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Drop a to-do list, polish a message before you send it, brain dump an idea. Scratchpad is your safe space to save, create, and explore.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        });
        var start = PageChrome.HeroPill("Start new note");
        start.MouseLeftButtonUp += (_, _) => NoteWindow.OpenNote(null);
        content.Children.Add(start);
        var hero = PageChrome.DarkHero(content);
        ((Border)hero).MinHeight = 230;
        ((Border)hero).Margin = new Thickness(0, 0, 0, 30);
        return hero;
    }

    private UIElement BuildRecentsHeader()
    {
        var dock = new DockPanel { Margin = new Thickness(2, 0, 2, 14) };
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        icons.Children.Add(PageChrome.IconButton("", "Search notes", null));
        icons.Children.Add(PageChrome.IconButton("", "New note", () => NoteWindow.OpenNote(null)));
        icons.Children.Add(PageChrome.IconButton("", "Notes are stored locally", null));
        DockPanel.SetDock(icons, Dock.Right);
        dock.Children.Add(icons);
        dock.Children.Add(new TextBlock
        {
            Text = "Recents",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var host = new StackPanel();
        host.Children.Add(dock);
        host.Children.Add(Theme.Divider(0, 16));
        return host;
    }

    private void RebuildRecents()
    {
        _recents.Children.Clear();
        var notes = NotesStore.All;
        if (notes.Count == 0)
        {
            _recents.Children.Add(new TextBlock
            {
                Text = "No notes found",
                FontSize = 15,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(4, 30, 0, 30),
            });
            return;
        }
        foreach (var n in notes.Take(12))
            _recents.Children.Add(NoteCard(n));
    }

    private UIElement NoteCard(Note note)
    {
        var panel = new StackPanel();
        var lines = note.Body.Split('\n');
        var title = string.IsNullOrWhiteSpace(note.Title) ? (lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "Untitled") : note.Title;
        panel.Children.Add(new TextBlock
        {
            Text = title.Trim(),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var preview = string.Join("\n", lines.SkipWhile(l => l.Trim().Length == 0).Skip(1).Take(4));
        panel.Children.Add(new TextBlock
        {
            Text = preview,
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 72,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(new TextBlock
        {
            Text = note.UpdatedAt.ToString("MMM d"),
            FontSize = 11.5,
            Foreground = Theme.SubtleBrush,
        });

        var card = new Border
        {
            Width = 320,
            MinHeight = 168,
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16, 18, 14),
            Margin = new Thickness(0, 0, 16, 16),
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => NoteWindow.OpenNote(note);
        card.MouseEnter += (_, _) => card.BorderBrush = Theme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Theme.HairlineBrush;
        return card;
    }
}
