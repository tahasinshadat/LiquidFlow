using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using FluidVoice.Core;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>A shared, floating scratchpad editor with persistent note tabs.</summary>
public sealed class NoteWindow : Window
{
    private const string InteractiveTag = "scratchpad-interactive";
    private static NoteWindow? _current;

    private readonly List<Note> _open = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _editor;
    private readonly TextBlock _hint;
    private readonly Border _lineHighlight;
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Note? _active;
    private bool _loading;

    public static void OpenNote(Note? note)
    {
        if (_current is null)
        {
            _current = new NoteWindow();
            _current.Closed += (_, _) => _current = null;
            if (!App.UiCapture.CaptureMode)
            {
                var owner = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault(window => window.IsVisible);
                if (owner is not null) _current.Owner = owner;
            }
        }

        _current.ShowNote(note ?? new Note());
        if (!_current.IsVisible) _current.Show();
        if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }

    private NoteWindow()
    {
        Title = "LiquidFlow Scratchpad";
        Width = 760;
        Height = 500;
        MinWidth = 560;
        MinHeight = 380;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        Background = Theme.BgBrush;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0, 1, 0, 0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });
        WindowFx.Apply(this);

        if (App.UiCapture.CaptureMode)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000;
            Top = 120;
            ShowActivated = false;
        }

        _lineHighlight = new Border
        {
            Height = 28,
            Margin = new Thickness(16, 18, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Theme.IsDark
                ? Color.FromRgb(45, 44, 39)
                : Color.FromRgb(248, 245, 237)),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Theme.TextBrush,
            FontSize = 15,
            Padding = new Thickness(24, 18, 24, 72),
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SpellCheck = { IsEnabled = true },
        };
        TextBlock.SetLineHeight(_editor, 27);
        TextBlock.SetLineStackingStrategy(_editor, LineStackingStrategy.BlockLineHeight);

        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "your hotkey";
        _hint = new TextBlock
        {
            Text = $"{hotkey} to dictate",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(26, 21, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };

        _editor.TextChanged += (_, _) =>
        {
            _hint.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateLineHighlight();
            if (_loading) return;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };
        _editor.SelectionChanged += (_, _) => UpdateLineHighlight();
        _editor.SizeChanged += (_, _) => UpdateLineHighlight();
        _editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateLineHighlight()));
        _editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SaveActive();
                e.Handled = true;
            }
        };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveActive();
        };

        var header = BuildHeader();
        var rail = BuildRail();
        var editorHost = BuildEditorHost();

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        DockPanel.SetDock(rail, Dock.Left);
        body.Children.Add(rail);
        body.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 10, 10),
            Background = Theme.SurfaceBrush,
            CornerRadius = new CornerRadius(10),
            Child = editorHost,
        });

        Content = new Border
        {
            Background = Theme.BgBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = body,
        };
    }

    private UIElement BuildHeader()
    {
        var header = new DockPanel
        {
            Height = 50,
            Margin = new Thickness(11, 2, 8, 2),
            LastChildFill = true,
        };

        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        windowButtons.Children.Add(ChromeButton("", "Maximize or restore", ToggleMaximize));
        windowButtons.Children.Add(ChromeButton("", "Close scratchpad", Close));
        DockPanel.SetDock(windowButtons, Dock.Right);
        header.Children.Add(windowButtons);

        var brand = new Border
        {
            Width = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Image
            {
                Source = WindowFx.AppIconLarge,
                Width = 23,
                Height = 23,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);

        var tabsScroll = new ScrollViewer
        {
            Content = _tabs,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        header.Children.Add(tabsScroll);

        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractive(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else
            {
                try { DragMove(); }
                catch { }
            }
        };
        return header;
    }

    private UIElement BuildRail()
    {
        var rail = new DockPanel
        {
            Width = 52,
            Margin = new Thickness(5, 4, 5, 10),
            LastChildFill = false,
        };
        var top = new StackPanel();
        top.Children.Add(RailButton("", "Open recent notes", OpenRecentNotes));
        top.Children.Add(RailButton("", "New note", () => ShowNote(new Note())));
        top.Children.Add(RailButton("", "Load all notes", OpenRecentNotes));
        DockPanel.SetDock(top, Dock.Top);
        rail.Children.Add(top);

        var bottom = new StackPanel();
        bottom.Children.Add(RailButton("", "Polish selected text", null));
        bottom.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            Margin = new Thickness(8, 4, 8, 0),
            Child = new TextBlock
            {
                Text = "Aa",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.SubtleBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        DockPanel.SetDock(bottom, Dock.Bottom);
        rail.Children.Add(bottom);
        return rail;
    }

    private UIElement BuildEditorHost()
    {
        var host = new Grid { ClipToBounds = true };
        host.Children.Add(_lineHighlight);
        host.Children.Add(_editor);
        host.Children.Add(_hint);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 12),
        };
        actions.Children.Add(EditorAction("", "Undo", () =>
        {
            if (_editor.CanUndo) _editor.Undo();
        }));
        actions.Children.Add(EditorAction("", "Delete note", DeleteActive));

        var copy = new Border
        {
            Tag = InteractiveTag,
            Background = Theme.InkBrush,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(17, 9, 17, 9),
            Cursor = Cursors.Hand,
            Margin = new Thickness(7, 0, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Theme.InkText),
                        Margin = new Thickness(0, 1, 8, 0),
                    },
                    new TextBlock
                    {
                        Text = "Copy",
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Theme.InkText),
                    },
                },
            },
        };
        copy.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SaveActive();
            ClipboardService.SetText(_editor.Text);
        };
        actions.Children.Add(copy);
        host.Children.Add(actions);
        return host;
    }

    private Border ChromeButton(string glyph, string tooltip, Action action)
    {
        var button = new Border
        {
            Tag = InteractiveTag,
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = Theme.Glyph(glyph, 13, Theme.TextBrush),
        };
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Theme.SidebarSelected);
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private Border RailButton(string glyph, string tooltip, Action? action)
    {
        var button = ChromeButton(glyph, tooltip, action ?? (() => { }));
        button.Width = 36;
        button.Height = 36;
        button.Margin = new Thickness(8, 0, 8, 3);
        button.Cursor = action is null ? Cursors.Arrow : Cursors.Hand;
        return button;
    }

    private Border EditorAction(string glyph, string tooltip, Action action)
    {
        var button = ChromeButton(glyph, tooltip, action);
        button.Background = new SolidColorBrush(Theme.SidebarSelected);
        button.Margin = new Thickness(7, 0, 0, 0);
        button.MouseLeave += (_, _) => button.Background = new SolidColorBrush(Theme.SidebarSelected);
        return button;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OpenRecentNotes()
    {
        SaveActive(false);
        foreach (var note in NotesStore.All.Take(10).Reverse())
            if (_open.All(openNote => openNote.Id != note.Id)) _open.Insert(0, note);
        if (_active is null && _open.Count > 0)
        {
            _active = _open[0];
            LoadActive();
        }
        RenderTabs();
    }

    private void ShowNote(Note note)
    {
        SaveActive(false);
        if (_open.All(openNote => openNote.Id != note.Id)) _open.Add(note);
        _active = _open.First(openNote => openNote.Id == note.Id);
        LoadActive();
        RenderTabs();
    }

    private void LoadActive()
    {
        _loading = true;
        _editor.Text = _active?.Body ?? "";
        _loading = false;
        _hint.Visibility = _editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _editor.CaretIndex = _editor.Text.Length;
        Dispatcher.BeginInvoke(() =>
        {
            _editor.Focus();
            UpdateLineHighlight();
        }, DispatcherPriority.Input);
    }

    private void RenderTabs()
    {
        _tabs.Children.Clear();
        foreach (var note in _open.ToList())
        {
            var active = note.Id == _active?.Id;
            var tab = new Grid { Height = 46, Margin = new Thickness(0, 0, 2, 0) };
            tab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            tab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });

            var hit = new Border
            {
                Tag = InteractiveTag,
                Background = Brushes.Transparent,
                Padding = new Thickness(11, 3, 8, 2),
                Cursor = Cursors.Hand,
                ToolTip = active ? "Click to rename" : "Open note",
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = TitleOf(note),
                FontSize = 12.5,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? Theme.TextBrush : Theme.SubtleBrush,
                MaxWidth = 145,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var close = new TextBlock
            {
                Text = "×",
                FontSize = 15,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(8, -1, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Close tab",
            };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                CloseTab(note);
            };
            row.Children.Add(close);
            hit.Child = row;
            hit.MouseEnter += (_, _) => hit.Background = new SolidColorBrush(Color.FromArgb(18, Theme.Text.R, Theme.Text.G, Theme.Text.B));
            hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
            hit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (note.Id == _active?.Id) BeginRename(note, hit);
                else ShowNote(note);
            };
            Grid.SetRow(hit, 0);
            tab.Children.Add(hit);

            var underline = new Border
            {
                Height = 2,
                Margin = new Thickness(9, 0, 9, 0),
                Background = active ? Theme.TextBrush : Brushes.Transparent,
            };
            Grid.SetRow(underline, 1);
            tab.Children.Add(underline);
            _tabs.Children.Add(tab);
        }

        var plus = ChromeButton("", "New note", () => ShowNote(new Note()));
        plus.Width = 36;
        plus.Height = 36;
        plus.Margin = new Thickness(3, 5, 0, 0);
        _tabs.Children.Add(plus);
    }

    private void BeginRename(Note note, Border tab)
    {
        var box = new TextBox
        {
            Tag = InteractiveTag,
            Text = TitleOf(note),
            FontSize = 12.5,
            MinWidth = 105,
            MaxWidth = 180,
            Padding = new Thickness(7, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var finished = false;
        void Finish(bool save)
        {
            if (finished) return;
            finished = true;
            var title = box.Text.Trim();
            if (save && title.Length > 0)
            {
                note.Title = title;
                note.CustomTitle = true;
                NotesStore.Save(note);
            }
            RenderTabs();
            _editor.Focus();
        }
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(false);
            }
        };
        box.LostFocus += (_, _) => Finish(true);
        tab.Child = box;
        box.Focus();
        box.SelectAll();
    }

    private void CloseTab(Note note)
    {
        if (note.Id == _active?.Id) SaveActive(false);
        var index = _open.IndexOf(note);
        _open.RemoveAll(openNote => openNote.Id == note.Id);
        if (_open.Count == 0)
        {
            Close();
            return;
        }
        if (_active?.Id == note.Id)
        {
            _active = _open[Math.Clamp(index - 1, 0, _open.Count - 1)];
            LoadActive();
        }
        RenderTabs();
    }

    private static string SuggestedTitle(string body)
    {
        var first = body.Replace("\r", "").Split('\n').FirstOrDefault(line => line.Trim().Length > 0)?.Trim();
        if (string.IsNullOrWhiteSpace(first)) return "Untitled";
        return first.Length <= 48 ? first : first[..45] + "…";
    }

    private static string TitleOf(Note note) =>
        string.IsNullOrWhiteSpace(note.Title) ? SuggestedTitle(note.Body) : note.Title;

    private void SaveActive(bool refreshTabs = true)
    {
        if (_active is null) return;
        var body = _editor.Text;
        var title = _active.CustomTitle ? _active.Title : SuggestedTitle(body);
        if (string.IsNullOrWhiteSpace(body) && !_active.CustomTitle) return;
        if (body == _active.Body && title == _active.Title) return;

        _active.Body = body;
        _active.Title = title;
        NotesStore.Save(_active);
        if (refreshTabs) RenderTabs();
    }

    private void DeleteActive()
    {
        if (_active is null) return;
        var deleting = _active;
        NotesStore.Delete(deleting.Id);
        _open.RemoveAll(note => note.Id == deleting.Id);
        if (_open.Count == 0)
        {
            Close();
            return;
        }
        _active = _open[^1];
        LoadActive();
        RenderTabs();
    }

    private void UpdateLineHighlight()
    {
        if (_editor.Text.Length == 0 || !_editor.IsLoaded)
        {
            _lineHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var caret = Math.Clamp(_editor.CaretIndex, 0, _editor.Text.Length);
                var rect = _editor.GetRectFromCharacterIndex(caret, true);
                if (rect.IsEmpty || rect.Top < 0 || rect.Top > _editor.ActualHeight)
                {
                    _lineHighlight.Visibility = Visibility.Collapsed;
                    return;
                }
                _lineHighlight.Height = Math.Max(27, rect.Height + 7);
                _lineHighlight.Margin = new Thickness(14, Math.Max(8, rect.Top - 3), 14, 0);
                _lineHighlight.Visibility = Visibility.Visible;
            }
            catch
            {
                _lineHighlight.Visibility = Visibility.Collapsed;
            }
        }, DispatcherPriority.Background);
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, InteractiveTag)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _saveDebounce.Stop();
        SaveActive(false);
        base.OnClosing(e);
    }
}
