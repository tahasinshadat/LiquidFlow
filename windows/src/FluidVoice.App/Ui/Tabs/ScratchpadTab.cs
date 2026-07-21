using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// Scratchpad: quick notes you want to come back to. Hero + "Start new note" + a Recents
/// history; notes open in the detached notes workspace (dictate straight into it).
/// </summary>
public sealed class ScratchpadTab : StackPanel
{
    private readonly StackPanel _recents = new();
    private string _search = "";

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
        var content = new StackPanel { Margin = PageChrome.HeroPadding, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
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
        hero.Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private UIElement BuildRecentsHeader()
    {
        var dock = new DockPanel { Margin = new Thickness(2, 0, 2, 14), MinHeight = 38 };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var searchHost = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 0, 8, 0),
            Width = 250,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var searchIcon = Theme.Glyph("\uE721", 15, Theme.SubtleBrush);
        Grid.SetColumn(searchIcon, 0);
        searchGrid.Children.Add(searchIcon);
        var search = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 13.5,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search your notes",
        };
        var placeholder = new TextBlock
        {
            Text = "Search your notes",
            FontSize = 13.5,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        search.TextChanged += (_, _) =>
        {
            _search = search.Text;
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RebuildRecents();
        };
        Grid.SetColumn(search, 1);
        Grid.SetColumn(placeholder, 1);
        searchGrid.Children.Add(search);
        searchGrid.Children.Add(placeholder);
        searchHost.Child = searchGrid;
        tools.Children.Add(searchHost);
        tools.Children.Add(PageChrome.IconButton("\uE710", "New note", () => NoteWindow.OpenNote(null)));
        tools.Children.Add(PageChrome.IconButton("\uE753", "Notes are stored locally", null));
        DockPanel.SetDock(tools, Dock.Right);
        dock.Children.Add(tools);
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
        var notes = NotesStore.All
            .Where(note => _search.Trim().Length == 0 ||
                           note.Title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase) ||
                           note.Body.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (notes.Count == 0)
        {
            _recents.Children.Add(new TextBlock
            {
                Text = _search.Length > 0 ? "No matching notes" : "No notes found",
                FontSize = 15,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(4, 30, 0, 30),
            });
            return;
        }
        foreach (var n in notes.Take(20))
            _recents.Children.Add(NoteCard(n));
    }

    private UIElement NoteCard(Note note)
    {
        var lines = note.Body.Split('\n');
        var title = string.IsNullOrWhiteSpace(note.Title) ? (lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "Untitled") : note.Title;
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(PageChrome.IconButton("\uE70F", "Edit note", () => NoteWindow.OpenNote(note)));
        actions.Children.Add(PageChrome.DangerIconButton("\uE718", note.IsPinned ? "Unpin note" : "Pin note", () =>
        {
            note.IsPinned = !note.IsPinned;
            NotesStore.Save(note);
        }, active: note.IsPinned));
        actions.Children.Add(PageChrome.DangerIconButton("\uE74D", "Delete note", () => NotesStore.Delete(note.Id)));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock
        {
            Text = title.Trim(),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8),
        });
        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        var contentLines = lines.SkipWhile(line => line.Trim().Length == 0);
        if (!note.CustomTitle && contentLines.FirstOrDefault()?.Trim() == title.Trim())
            contentLines = contentLines.Skip(1);
        var preview = string.Join("\n", contentLines.Take(5));
        var previewBlock = new TextBlock
        {
            Text = preview,
            FontSize = 13,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 88,
            Margin = new Thickness(0, 4, 0, 16),
        };
        Grid.SetRow(previewBlock, 1);
        panel.Children.Add(previewBlock);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = note.UpdatedAt.ToString("MMM d"),
            FontSize = 11.5,
            Foreground = Theme.SubtleBrush,
        });
        var time = new TextBlock
        {
            Text = note.UpdatedAt.ToString("h:mm tt"),
            FontSize = 11.5,
            Foreground = Theme.SubtleBrush,
        };
        Grid.SetColumn(time, 1);
        footer.Children.Add(time);
        Grid.SetRow(footer, 2);
        panel.Children.Add(footer);

        var card = new Border
        {
            MinHeight = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 12, 14, 15),
            Margin = new Thickness(0, 0, 0, 14),
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => NoteWindow.OpenNote(note);
        card.MouseEnter += (_, _) => card.BorderBrush = Theme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Theme.HairlineBrush;
        return card;
    }
}
