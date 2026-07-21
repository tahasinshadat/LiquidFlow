using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluidVoice.Core;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Floating scratchpad note: chromeless rounded always-on-top window with note tabs, a
/// left mini-rail, an autosaving editor (dictate straight into it with your hotkey), and
/// a Copy pill. One shared window; notes open as tabs.
/// </summary>
public sealed class NoteWindow : Window
{
    private static NoteWindow? _current;

    private readonly List<Note> _open = new();
    private Note? _active;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _editor;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _expanded;

    /// <summary>Open (or focus) the shared note window, showing <paramref name="note"/> (null = new note).</summary>
    public static void OpenNote(Note? note)
    {
        if (_current is null)
        {
            _current = new NoteWindow();
            _current.Closed += (_, _) => _current = null;
        }
        _current.ShowNote(note ?? new Note());
        _current.Show();
        _current.Activate();
    }

    private NoteWindow()
    {
        Width = 740;
        Height = 470;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Theme.TextBrush,
            FontSize = 14.5,
            Padding = new Thickness(18, 14, 18, 14),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "your hotkey";
        _hint = new TextBlock
        {
            Text = $"{hotkey}  to dictate",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(20, 16, 0, 0),
            IsHitTestVisible = false,
        };
        _editor.TextChanged += (_, _) =>
        {
            _hint.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveActive(); };

        // ---- header: tabs + expand/close ----
        var header = new DockPanel { Margin = new Thickness(12, 10, 10, 6), LastChildFill = true };
        var winBtns = new StackPanel { Orientation = Orientation.Horizontal };
        winBtns.Children.Add(PageChrome.IconButton("", "Expand", ToggleExpand));
        winBtns.Children.Add(PageChrome.IconButton("", "Close", Close));
        DockPanel.SetDock(winBtns, Dock.Right);
        header.Children.Add(winBtns);

        var brand = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = Theme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0),
        };
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);

        var tabsScroll = new ScrollViewer
        {
            Content = _tabs,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        header.Children.Add(tabsScroll);

        // ---- left mini-rail ----
        var rail = new StackPanel { Margin = new Thickness(8, 6, 4, 6) };
        rail.Children.Add(PageChrome.IconButton("", "All notes (open the Scratchpad tab)", () =>
        {
            foreach (var n in NotesStore.All.Take(8).Reverse())
                if (_open.All(o => o.Id != n.Id)) _open.Insert(0, n);
            RenderTabs();
        }));
        rail.Children.Add(PageChrome.IconButton("", "New note", () => ShowNote(new Note())));
        rail.Children.Add(PageChrome.IconButton("", "Search (use the Scratchpad tab)", null));

        // ---- editor host with hint + bottom-right actions ----
        var editorHost = new Grid();
        editorHost.Children.Add(new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = _editor,
        });
        editorHost.Children.Add(_hint);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 12),
        };
        actions.Children.Add(PageChrome.IconButton("", "Delete note", DeleteActive));
        var copy = new Border
        {
            Background = Theme.InkBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(Theme.InkText), Margin = new Thickness(0, 1, 8, 0) },
                    new TextBlock { Text = "Copy", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.InkText) },
                },
            },
        };
        copy.MouseLeftButtonUp += (_, _) => { SaveActive(); ClipboardService.SetText(_editor.Text); };
        actions.Children.Add(copy);
        editorHost.Children.Add(actions);

        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        DockPanel.SetDock(rail, Dock.Left);
        body.Children.Add(rail);
        body.Children.Add(new Border { Child = editorHost, Margin = new Thickness(0, 0, 12, 12) });

        Content = new Border
        {
            Background = new SolidColorBrush(Theme.Surface),
            CornerRadius = new CornerRadius(14),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.3, Color = Colors.Black },
            Child = body,
        };
        header.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
    }

    private void ToggleExpand()
    {
        _expanded = !_expanded;
        Width = _expanded ? 1060 : 740;
        Height = _expanded ? 660 : 470;
    }

    private void ShowNote(Note note)
    {
        SaveActive();
        if (_open.All(n => n.Id != note.Id)) _open.Add(note);
        _active = _open.First(n => n.Id == note.Id);
        _editor.Text = _active.Body;
        _hint.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderTabs();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
    }

    private void RenderTabs()
    {
        _tabs.Children.Clear();
        foreach (var note in _open.ToList())
        {
            bool on = note.Id == _active?.Id;
            var chip = new Border
            {
                Background = on ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 5, 8, 5),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = TitleOf(note),
                FontSize = 12.5,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Theme.TextBrush,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var close = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(8, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (note.Id == _active?.Id) SaveActive();
                _open.RemoveAll(n => n.Id == note.Id);
                if (_active?.Id == note.Id) { _active = _open.FirstOrDefault(); _editor.Text = _active?.Body ?? ""; }
                if (_open.Count == 0) { Close(); return; }
                RenderTabs();
            };
            row.Children.Add(close);
            chip.Child = row;
            chip.MouseLeftButtonUp += (_, _) => ShowNote(note);
            _tabs.Children.Add(chip);
        }
        var plus = PageChrome.IconButton("", "New note", () => ShowNote(new Note()));
        _tabs.Children.Add(plus);
    }

    private static string TitleOf(Note n)
    {
        if (!string.IsNullOrWhiteSpace(n.Title)) return n.Title;
        var first = n.Body.Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
        return string.IsNullOrWhiteSpace(first) ? "Untitled" : first.Trim();
    }

    private void SaveActive()
    {
        if (_active is null) return;
        var body = _editor.Text;
        if (body == _active.Body && !string.IsNullOrWhiteSpace(_active.Title)) return;
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(_active.Title)) return; // don't persist empty new notes
        _active.Body = body;
        _active.Title = TitleOf(_active);
        NotesStore.Save(_active);
        RenderTabs();
    }

    private void DeleteActive()
    {
        if (_active is null) return;
        NotesStore.Delete(_active.Id);
        _open.RemoveAll(n => n.Id == _active.Id);
        _active = _open.FirstOrDefault();
        _editor.Text = _active?.Body ?? "";
        if (_open.Count == 0) { Close(); return; }
        RenderTabs();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveActive();
        base.OnClosing(e);
    }
}
