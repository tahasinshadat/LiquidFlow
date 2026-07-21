using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using FluidVoice.Ai;
using FluidVoice.Core;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Detached Scratchpad workspace: persistent note tabs, a collapsible searchable notes rail,
/// rich-text formatting, and AI transforms that use the currently active provider/model.
/// </summary>
public sealed class NoteWindow : Window
{
    private const string InteractiveTag = "scratchpad-interactive";
    private const double ExpandedSidebarWidth = 270;
    private const double CollapsedSidebarWidth = 64;

    private enum BottomTool { None, Transforms, Formatting }

    private static NoteWindow? _current;

    private readonly List<Note> _open = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _noteList = new();
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly RichTextBox _editor;
    private readonly FrameworkElement _hint;
    private readonly Border _sidebarHost;
    private readonly ColumnDefinition _sidebarColumn;
    private readonly Border _toolPanel;
    private readonly StackPanel _editorActions;
    private readonly TextBox _transformPrompt;
    private readonly TextBlock _transformStatus;
    private TextBlock? _transformPromptPlaceholder;

    private Note? _active;
    private bool _loading;
    private bool _sidebarExpanded = true;
    private bool _transforming;
    private string _search = "";
    private TextBox? _searchBox;
    private BottomTool _tool;
    private CancellationTokenSource? _transformCts;
    private bool _enlarged;
    private Rect _restoreBounds;
    private TextBlock? _maxGlyph;

    public static void OpenNote(Note? note)
    {
        if (_current is null)
        {
            _current = new NoteWindow();
            _current.Closed += (_, _) => _current = null;
        }

        _current.ShowNote(note ?? new Note());
        if (!_current.IsVisible) _current.Show();
        if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }

    private NoteWindow()
    {
        Title = "LiquidFlow Scratchpad";
        Width = 980;
        Height = 700;
        MinWidth = 720;
        MinHeight = 500;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        Background = Theme.BgBrush;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
            Width = 900;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000;
            Top = 120;
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        _editor = new RichTextBox
        {
            Tag = InteractiveTag,
            Document = NewDocument(),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Theme.TextBrush,
            FontFamily = Theme.UiFont,
            FontSize = 15,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsUndoEnabled = true,
            SpellCheck = { IsEnabled = true },
            SelectionBrush = new SolidColorBrush(Theme.Accent),
            SelectionOpacity = 0.3,
        };

        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "your hotkey";
        var hint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(25, 22, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        hint.Children.Add(new Border
        {
            Background = new SolidColorBrush(Theme.SidebarSelected),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock { Text = hotkey, FontSize = 13, Foreground = Theme.SubtleBrush },
        });
        hint.Children.Add(new TextBlock
        {
            Text = " to dictate",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _hint = hint;

        _transformPrompt = new TextBox
        {
            Tag = InteractiveTag,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 13.5,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _transformStatus = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _transformPrompt.TextChanged += (_, _) =>
        {
            if (_transformPromptPlaceholder is not null)
                _transformPromptPlaceholder.Visibility = _transformPrompt.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        _transformPrompt.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                _ = RunTransformAsync(_transformPrompt.Text);
            }
        };

        _sidebarColumn = new ColumnDefinition { Width = new GridLength(ExpandedSidebarWidth) };
        _sidebarHost = new Border { Background = Theme.BgBrush };
        _toolPanel = new Border
        {
            Tag = InteractiveTag,
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 16, 12),
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.14, Color = Colors.Black },
        };
        _editorActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 12),
        };

        WireEditor();

        var header = BuildHeader();
        var editorHost = BuildEditorHost();
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(_sidebarColumn);
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_sidebarHost, 0);
        bodyGrid.Children.Add(_sidebarHost);
        var editorCard = new Border
        {
            Margin = new Thickness(0, 0, 10, 10),
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = editorHost,
        };
        Grid.SetColumn(editorCard, 1);
        bodyGrid.Children.Add(editorCard);

        var shell = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        shell.Children.Add(header);
        shell.Children.Add(bodyGrid);

        Content = new Border
        {
            Background = Theme.BgBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = shell,
        };

        // The whole window drags from any non-interactive surface (header, sidebar, gaps) —
        // buttons, the editor, and inputs are opted out via InteractiveTag.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractive(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else try { DragMove(); } catch { }
        };

        RenderSidebar();
        RenderToolPanel();
        Loaded += (_, _) => NotesStore.Changed += OnNotesChanged;
        Unloaded += (_, _) => NotesStore.Changed -= OnNotesChanged;
    }

    private static FlowDocument NewDocument()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(28, 22, 28, 90),
            FontFamily = Theme.UiFont,
            FontSize = 15,
            Foreground = Theme.TextBrush,
            LineHeight = 25,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        document.Resources.Add(typeof(Paragraph), paragraphStyle);
        return document;
    }

    private void WireEditor()
    {
        _editor.TextChanged += (_, _) =>
        {
            _hint.Visibility = PlainText().Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_loading) return;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };
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
    }

    private UIElement BuildHeader()
    {
        var header = new DockPanel
        {
            Height = 62,
            Margin = new Thickness(12, 2, 10, 2),
            LastChildFill = true,
            Background = Brushes.Transparent, // hit-testable so empty header space drags the window
        };

        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var maximize = ChromeButton("\uE922", "Enlarge or restore", ToggleMaximize);
        _maxGlyph = maximize.Child as TextBlock;
        windowButtons.Children.Add(maximize);
        windowButtons.Children.Add(ChromeButton("\uE711", "Close Scratchpad", Close));
        DockPanel.SetDock(windowButtons, Dock.Right);
        header.Children.Add(windowButtons);

        var brand = new Border
        {
            Width = 42,
            Height = 42,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Image
            {
                Source = WindowFx.AppIconLarge,
                Width = 25,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);

        header.Children.Add(new ScrollViewer
        {
            Content = _tabs,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalAlignment = VerticalAlignment.Stretch,
        });

        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractive(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else try { DragMove(); } catch { }
        };
        return header;
    }

    private UIElement BuildEditorHost()
    {
        var host = new Grid { ClipToBounds = true };
        host.Children.Add(_editor);
        host.Children.Add(_hint);

        _editorActions.Children.Add(EditorAction("\uE7A7", "Undo", () =>
        {
            if (_editor.CanUndo) _editor.Undo();
        }));
        _editorActions.Children.Add(EditorAction("\uE74D", "Delete note", DeleteActive, danger: true));
        _editorActions.Children.Add(CopyButton());
        host.Children.Add(_editorActions);
        host.Children.Add(_toolPanel);
        return host;
    }

    private Border CopyButton()
    {
        var button = new Border
        {
            Tag = InteractiveTag,
            Background = Theme.InkBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(17, 9, 17, 9),
            Cursor = Cursors.Hand,
            Margin = new Thickness(7, 0, 0, 0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.InkText),
            Margin = new Thickness(0, 1, 8, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = "Copy",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.InkText),
        });
        button.Child = row;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SaveActive();
            ClipboardService.SetText(PlainText());
        };
        return button;
    }

    private void RenderSidebar()
    {
        _sidebarColumn.Width = new GridLength(_sidebarExpanded ? ExpandedSidebarWidth : CollapsedSidebarWidth);
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 4, 8, 10) };

        var bottom = new StackPanel();
        bottom.Children.Add(SidebarAction("\uE945", "Transforms", () => ToggleTool(BottomTool.Transforms), _tool == BottomTool.Transforms));
        bottom.Children.Add(SidebarAction("Aa", "Formatting", () => ToggleTool(BottomTool.Formatting), _tool == BottomTool.Formatting, mdl2: false));
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        var top = new StackPanel();
        top.Children.Add(SidebarAction("\uE8A7", _sidebarExpanded ? "Collapse Notes" : "Expand Notes", ToggleSidebar));
        top.Children.Add(SidebarAction("\uE70F", "New note", () => ShowNote(new Note())));
        top.Children.Add(BuildSearch());
        top.Children.Add(Theme.Divider(8, 8));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        var scroll = new ScrollViewer
        {
            Content = _noteList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        dock.Children.Add(scroll);
        _sidebarHost.Child = dock;
        RenderNoteList();
    }

    private UIElement BuildSearch()
    {
        if (!_sidebarExpanded)
            return SidebarAction("\uE721", "Search notes", () =>
            {
                _sidebarExpanded = true;
                RenderSidebar();
                Dispatcher.BeginInvoke(() => _searchBox?.Focus(), DispatcherPriority.Input);
            });

        var border = new Border
        {
            Tag = InteractiveTag,
            Height = 48,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 0, 10, 0),
            Background = Brushes.Transparent,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Theme.Glyph("\uE721", 15, Theme.SubtleBrush);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        _searchBox = new TextBox
        {
            Tag = InteractiveTag,
            Text = _search,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 13.5,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search notes",
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _search = _searchBox.Text;
            RenderNoteList();
        };
        Grid.SetColumn(_searchBox, 1);
        grid.Children.Add(_searchBox);
        if (_search.Length == 0)
        {
            var placeholder = new TextBlock
            {
                Text = "Search notes…",
                FontSize = 13.5,
                Foreground = Theme.SubtleBrush,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            Grid.SetColumn(placeholder, 1);
            grid.Children.Add(placeholder);
            _searchBox.TextChanged += (_, _) => placeholder.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        border.Child = grid;
        return border;
    }

    private Border SidebarAction(string glyph, string label, Action action, bool selected = false, bool mdl2 = true)
    {
        var button = new Border
        {
            Tag = InteractiveTag,
            Height = 50,
            CornerRadius = new CornerRadius(9),
            Background = selected ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = _sidebarExpanded ? null : label,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        if (_sidebarExpanded) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = mdl2 ? new FontFamily("Segoe MDL2 Assets") : Theme.UiFont,
            FontSize = mdl2 ? 16 : 13,
            FontWeight = mdl2 ? FontWeights.Normal : FontWeights.SemiBold,
            Foreground = selected ? Theme.TextBrush : Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        if (_sidebarExpanded)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        button.Child = grid;
        button.MouseEnter += (_, _) =>
        {
            if (!selected) button.Background = new SolidColorBrush(Color.FromArgb(18, Theme.Text.R, Theme.Text.G, Theme.Text.B));
        };
        button.MouseLeave += (_, _) => button.Background = selected ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        return button;
    }

    private void RenderNoteList()
    {
        _noteList.Children.Clear();
        if (!_sidebarExpanded) return;

        var notes = NotesStore.All.ToList();
        foreach (var open in _open)
            if (notes.All(note => note.Id != open.Id)) notes.Insert(0, open);

        if (_search.Trim().Length > 0)
        {
            var term = _search.Trim();
            notes = notes.Where(note =>
                    TitleOf(note).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    note.Body.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var note in notes)
        {
            var selected = note.Id == _active?.Id;
            var button = new Border
            {
                Tag = InteractiveTag,
                Background = selected ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 10, 9),
                Margin = new Thickness(2, 2, 2, 2),
                Cursor = Cursors.Hand,
            };
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = TitleOf(note),
                FontSize = 13.5,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Theme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = RelativeTime(note.UpdatedAt),
                FontSize = 11.5,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(0, 2, 0, 0),
            });
            button.Child = text;
            button.MouseEnter += (_, _) =>
            {
                if (!selected) button.Background = new SolidColorBrush(Color.FromArgb(18, Theme.Text.R, Theme.Text.G, Theme.Text.B));
            };
            button.MouseLeave += (_, _) => button.Background = selected ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent;
            button.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowNote(note); };
            _noteList.Children.Add(button);
        }

        if (notes.Count == 0)
            _noteList.Children.Add(new TextBlock
            {
                Text = _search.Length > 0 ? "No matching notes" : "No notes yet",
                FontSize = 12.5,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(12, 14, 0, 0),
            });
    }

    private void ToggleSidebar()
    {
        _sidebarExpanded = !_sidebarExpanded;
        RenderSidebar();
    }

    private void ToggleTool(BottomTool tool)
    {
        _tool = _tool == tool ? BottomTool.None : tool;
        RenderSidebar();
        RenderToolPanel();
        _editor.Focus();
    }

    private void RenderToolPanel()
    {
        _toolPanel.Child = null;
        _toolPanel.Visibility = _tool == BottomTool.None ? Visibility.Collapsed : Visibility.Visible;
        _toolPanel.Height = _tool == BottomTool.Transforms ? 116 : 70;
        _editorActions.Margin = new Thickness(0, 0, 14, _tool == BottomTool.None ? 12 : _toolPanel.Height + 18);
        ApplyEditorPadding();
        if (_tool == BottomTool.Formatting) _toolPanel.Child = BuildFormattingBar();
        if (_tool == BottomTool.Transforms) _toolPanel.Child = BuildTransformPanel();
    }

    /// <summary>Comfortable page padding; RichTextBox silently resets Document.PagePadding whenever
    /// a document is attached, so this must be re-applied after every document swap/load.</summary>
    private void ApplyEditorPadding()
    {
        _editor.Document.PagePadding = new Thickness(28, 22, 28, _tool == BottomTool.None ? 90 : _toolPanel.Height + 92);
    }

    private UIElement BuildFormattingBar()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(FormatButton("B", "Bold (Ctrl+B)", () => Execute(EditingCommands.ToggleBold), FontWeights.SemiBold));
        row.Children.Add(FormatButton("I", "Italic (Ctrl+I)", () => Execute(EditingCommands.ToggleItalic), italic: true));
        row.Children.Add(FormatButton("U", "Underline (Ctrl+U)", () => Execute(EditingCommands.ToggleUnderline), underline: true));
        row.Children.Add(FormatButton("<>", "Code", ToggleCode));
        row.Children.Add(ToolDivider());
        row.Children.Add(FormatButton("•≡", "Bulleted list", () => Execute(EditingCommands.ToggleBullets)));
        row.Children.Add(FormatButton("1≡", "Numbered list", () => Execute(EditingCommands.ToggleNumbering)));
        row.Children.Add(FormatButton("✓≡", "Checklist", InsertChecklist));
        row.Children.Add(ToolDivider());
        row.Children.Add(FormatButton("❞", "Quote", ToggleQuote));
        row.Children.Add(FormatButton("→≡", "Increase indent", () => Execute(EditingCommands.IncreaseIndentation)));
        row.Children.Add(FormatButton("←≡", "Decrease indent", () => Execute(EditingCommands.DecreaseIndentation)));
        return new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    private UIElement BuildTransformPanel()
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var quick = new DockPanel { Margin = new Thickness(0, 0, 0, 11), LastChildFill = true };
        _transformStatus.VerticalAlignment = VerticalAlignment.Center;
        _transformStatus.Margin = new Thickness(10, 0, 2, 0);
        DockPanel.SetDock(_transformStatus, Dock.Right);
        quick.Children.Add(_transformStatus);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(TransformChip("Polish", "Polish this text: improve clarity and conciseness while preserving its meaning and tone."));
        chips.Children.Add(TransformChip("More professional", "Rewrite this text in a more professional, polished tone without making it stiff."));
        chips.Children.Add(TransformChip("More casual", "Rewrite this text in a natural, more casual tone while preserving the meaning."));
        quick.Children.Add(chips);
        Grid.SetRow(quick, 0);
        grid.Children.Add(quick);

        var divider = Theme.Divider();
        Grid.SetRow(divider, 1);
        grid.Children.Add(divider);

        var composer = new Border
        {
            Tag = InteractiveTag,
            Height = 38,
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Theme.Field),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var composerGrid = new Grid();
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var promptHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        promptHost.Children.Add(_transformPrompt);
        _transformPromptPlaceholder = new TextBlock
        {
            Text = "Tell LiquidFlow how to transform this note…",
            FontSize = 13.5,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _transformPromptPlaceholder.Visibility = _transformPrompt.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        promptHost.Children.Add(_transformPromptPlaceholder);
        Grid.SetColumn(promptHost, 0);
        composerGrid.Children.Add(promptHost);
        var send = new Border
        {
            Tag = InteractiveTag,
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            Background = Theme.InkBrush,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Theme.Glyph("\uE72A", 12, new SolidColorBrush(Theme.InkText)),
        };
        send.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = RunTransformAsync(_transformPrompt.Text); };
        Grid.SetColumn(send, 1);
        composerGrid.Children.Add(send);
        composer.Child = composerGrid;
        Grid.SetRow(composer, 2);
        grid.Children.Add(composer);
        return grid;
    }

    private Border TransformChip(string label, string instruction)
    {
        var chip = new Border
        {
            Tag = InteractiveTag,
            Background = new SolidColorBrush(Theme.SidebarSelected),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        chip.MouseEnter += (_, _) => chip.BorderBrush = Theme.AccentBrush;
        chip.MouseLeave += (_, _) => chip.BorderBrush = Brushes.Transparent;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = RunTransformAsync(instruction); };
        return chip;
    }

    private Border FormatButton(string label, string tooltip, Action action,
        FontWeight? weight = null, bool italic = false, bool underline = false)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 16,
            FontWeight = weight ?? FontWeights.Normal,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            TextDecorations = underline ? TextDecorations.Underline : null,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Tag = InteractiveTag,
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = text,
        };
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Theme.SidebarSelected);
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); _editor.Focus(); };
        return button;
    }

    private static Border ToolDivider() => new()
    {
        Width = 1,
        Height = 24,
        Background = Theme.HairlineBrush,
        Margin = new Thickness(5, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Execute(RoutedCommand command)
    {
        if (command.CanExecute(null, _editor)) command.Execute(null, _editor);
    }

    private void ToggleCode()
    {
        var current = _editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
        _editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
            current?.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) == true ? Theme.UiFont : new FontFamily("Consolas"));
    }

    private void InsertChecklist()
    {
        _editor.Selection.Text = "☐ ";
    }

    private void ToggleQuote()
    {
        var paragraph = _editor.Selection.Start.Paragraph;
        if (paragraph is null) return;
        paragraph.Margin = paragraph.Margin.Left > 0 ? new Thickness(0) : new Thickness(24, 0, 0, 0);
        paragraph.Foreground = paragraph.Margin.Left > 0 ? Theme.SubtleBrush : Theme.TextBrush;
    }

    private async Task RunTransformAsync(string instruction)
    {
        instruction = instruction.Trim();
        if (instruction.Length == 0 || _transforming) return;

        var selected = new TextRange(_editor.Selection.Start, _editor.Selection.End).Text.Trim();
        var source = selected.Length > 0 ? selected : PlainText();
        if (source.Length == 0)
        {
            _transformStatus.Text = "Write something first";
            return;
        }

        var providerId = Settings.Current.SelectedProviderID;
        var model = ProviderCatalog.SelectedModelFor(providerId);
        if (!ProviderCatalog.IsConfigured(providerId) || string.IsNullOrWhiteSpace(model))
        {
            _transformStatus.Text = "Configure AI Enhancement first";
            return;
        }

        _transformCts?.Cancel();
        _transformCts?.Dispose();
        _transformCts = new CancellationTokenSource();
        _transforming = true;
        _editor.IsReadOnly = true;
        _transformStatus.Text = $"Working with {ProviderCatalog.DisplayName(providerId)}…";
        try
        {
            var response = await LlmClient.CallAsync(new LlmRequest
            {
                ProviderId = providerId,
                Model = model,
                Messages = new List<LlmMessage>
                {
                    new("system", "You are a note editor. Follow the user's transformation instruction precisely. Preserve facts and intent. Return only the transformed text, with no commentary or markdown fences."),
                    new("user", $"Instruction: {instruction}\n\nText to transform:\n{source}"),
                },
                Temperature = 0.35,
                MaxTokens = LlmClient.IsReasoningModel(model) ? 32_000 : null,
                TimeoutSeconds = 60,
                Stream = false,
            }, _transformCts.Token);

            var result = response.Content.Trim();
            if (result.Length == 0) throw new InvalidOperationException("The AI returned an empty response.");
            if (selected.Length > 0) _editor.Selection.Text = result;
            else SetPlainText(result);
            SaveActive();
            _transformPrompt.Clear();
            _transformStatus.Text = "Done";
        }
        catch (OperationCanceledException)
        {
            _transformStatus.Text = "Canceled";
        }
        catch (Exception ex)
        {
            _transformStatus.Text = ex.Message;
            Log.Warn("scratchpad", $"Transform failed: {ex.Message}");
        }
        finally
        {
            _transforming = false;
            _editor.IsReadOnly = false;
            _editor.Focus();
        }
    }

    private Border ChromeButton(string glyph, string tooltip, Action action)
    {
        var button = new Border
        {
            Tag = InteractiveTag,
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = Theme.Glyph(glyph, 14, Theme.TextBrush),
        };
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Theme.SidebarSelected);
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        return button;
    }

    private Border EditorAction(string glyph, string tooltip, Action action, bool danger = false)
    {
        var button = ChromeButton(glyph, tooltip, action);
        button.Width = 38;
        button.Height = 38;
        button.Background = Theme.SurfaceBrush;
        button.BorderBrush = Theme.HairlineBrush;
        button.BorderThickness = new Thickness(1);
        button.Margin = new Thickness(7, 0, 0, 0);
        button.Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = 0.12 };
        button.MouseLeave += (_, _) => button.Background = Theme.SurfaceBrush;
        if (danger && button.Child is TextBlock glyphBlock)
        {
            // hovering destructive actions goes red so the intent is unmistakable
            button.MouseEnter += (_, _) =>
            {
                button.Background = new SolidColorBrush(Color.FromArgb(26, Theme.Danger.R, Theme.Danger.G, Theme.Danger.B));
                button.BorderBrush = new SolidColorBrush(Theme.Danger);
                glyphBlock.Foreground = new SolidColorBrush(Theme.Danger);
            };
            button.MouseLeave += (_, _) =>
            {
                button.BorderBrush = Theme.HairlineBrush;
                glyphBlock.Foreground = Theme.TextBrush;
            };
        }
        return button;
    }

    /// <summary>Grow to ~90% of the current monitor's work area (not OS-maximize) and back.</summary>
    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        if (_enlarged)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _enlarged = false;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var area = ScreenWorkAreaDip();
            var width = Math.Max(MinWidth, area.Width * 0.9);
            var height = Math.Max(MinHeight, area.Height * 0.9);
            Left = area.Left + (area.Width - width) / 2;
            Top = area.Top + (area.Height - height) / 2;
            Width = width;
            Height = height;
            _enlarged = true;
        }
        if (_maxGlyph is not null) _maxGlyph.Text = _enlarged ? "" : "";
    }

    /// <summary>Work area of the monitor this window sits on, in WPF device-independent pixels.</summary>
    private Rect ScreenWorkAreaDip()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            {
                var fromDevice = target.TransformFromDevice;
                var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
                var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
                return new Rect(topLeft, bottomRight);
            }
            return new Rect(area.Left, area.Top, area.Width, area.Height);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private void OnNotesChanged() => Dispatcher.BeginInvoke(() =>
    {
        RenderNoteList();
        RenderTabs();
    });

    private void ShowNote(Note note)
    {
        SaveActive(false);
        if (_open.All(open => open.Id != note.Id)) _open.Add(note);
        _active = _open.First(open => open.Id == note.Id);
        LoadActive();
        RenderTabs();
        RenderNoteList();
    }

    private void LoadActive()
    {
        _loading = true;
        _editor.Document = NewDocument();
        if (_active is { RichTextXaml.Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(_active.RichTextXaml));
                new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Load(stream, DataFormats.Xaml);
            }
            catch (Exception ex)
            {
                Log.Warn("scratchpad", $"Could not load rich formatting: {ex.Message}");
                SetPlainText(_active.Body);
            }
        }
        else
        {
            SetPlainText(_active?.Body ?? "");
        }
        ApplyEditorPadding();
        _loading = false;
        _hint.Visibility = PlainText().Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _editor.CaretPosition = _editor.Document.ContentEnd;
        Dispatcher.BeginInvoke(() => _editor.Focus(), DispatcherPriority.Input);
    }

    private void RenderTabs()
    {
        _tabs.Children.Clear();
        foreach (var note in _open.ToList())
        {
            var active = note.Id == _active?.Id;
            var hit = new Border
            {
                Tag = InteractiveTag,
                Height = 48,
                Background = Brushes.Transparent,
                Padding = new Thickness(11, 3, 8, 2),
                Cursor = Cursors.Hand,
                ToolTip = active ? "Click to rename" : "Open note",
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = TitleOf(note),
                FontSize = 13,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? Theme.TextBrush : Theme.SubtleBrush,
                MaxWidth = 150,
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
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseTab(note); };
            row.Children.Add(close);
            hit.Child = row;
            hit.MouseEnter += (_, _) => hit.Background = new SolidColorBrush(Color.FromArgb(18, Theme.Text.R, Theme.Text.G, Theme.Text.B));
            hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
            hit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (active) BeginRename(note, hit);
                else ShowNote(note);
            };
            _tabs.Children.Add(hit);
        }

        var plus = ChromeButton("\uE710", "New note", () => ShowNote(new Note()));
        plus.Margin = new Thickness(3, 5, 0, 0);
        _tabs.Children.Add(plus);
    }

    private void BeginRename(Note note, Border tab)
    {
        var box = new TextBox
        {
            Tag = InteractiveTag,
            Text = TitleOf(note),
            FontSize = 13,
            MinWidth = 110,
            MaxWidth = 185,
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
            RenderNoteList();
            _editor.Focus();
        }
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
            else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
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
        _open.RemoveAll(open => open.Id == note.Id);
        if (_open.Count == 0)
        {
            ShowNote(new Note());
            return;
        }
        if (_active?.Id == note.Id)
        {
            _active = _open[Math.Clamp(index - 1, 0, _open.Count - 1)];
            LoadActive();
        }
        RenderTabs();
        RenderNoteList();
    }

    private static string SuggestedTitle(string body)
    {
        var first = body.Replace("\r", "").Split('\n').FirstOrDefault(line => line.Trim().Length > 0)?.Trim();
        if (string.IsNullOrWhiteSpace(first)) return "Untitled";
        return first.Length <= 48 ? first : first[..45] + "…";
    }

    private static string TitleOf(Note note) =>
        string.IsNullOrWhiteSpace(note.Title) ? SuggestedTitle(note.Body) : note.Title;

    private static string RelativeTime(DateTime time)
    {
        var age = DateTime.Now - time;
        if (age.TotalMinutes < 1) return "less than a minute ago";
        if (age.TotalMinutes < 60) return $"{Math.Max(1, (int)age.TotalMinutes)} minutes ago";
        if (age.TotalHours < 24) return $"about {Math.Max(1, (int)age.TotalHours)} hours ago";
        if (age.TotalDays < 7) return $"{Math.Max(1, (int)age.TotalDays)} days ago";
        return time.ToString("MMM d");
    }

    private string PlainText()
    {
        var text = new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Text;
        return text.TrimEnd('\r', '\n');
    }

    private void SetPlainText(string text)
    {
        new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Text = text;
        NormalizeParagraphMargins();
    }

    private void NormalizeParagraphMargins()
    {
        foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
            paragraph.Margin = new Thickness(0);
    }

    private string SerializeDocument()
    {
        using var stream = new MemoryStream();
        new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Save(stream, DataFormats.Xaml);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void SaveActive(bool refreshUi = true)
    {
        if (_active is null) return;
        var body = PlainText();
        var title = _active.CustomTitle ? _active.Title : SuggestedTitle(body);
        if (string.IsNullOrWhiteSpace(body) && !_active.CustomTitle) return;
        var xaml = SerializeDocument();
        if (body == _active.Body && title == _active.Title && xaml == _active.RichTextXaml) return;

        _active.Body = body;
        _active.Title = title;
        _active.RichTextXaml = xaml;
        NotesStore.Save(_active);
        if (refreshUi)
        {
            RenderTabs();
            RenderNoteList();
        }
    }

    private void DeleteActive()
    {
        if (_active is null) return;
        var deleting = _active;
        NotesStore.Delete(deleting.Id);
        _open.RemoveAll(note => note.Id == deleting.Id);
        if (_open.Count == 0)
        {
            ShowNote(new Note());
            return;
        }
        _active = _open[^1];
        LoadActive();
        RenderTabs();
        RenderNoteList();
    }

    /// <summary>Capture seam for the formatting and transform reference states.</summary>
    public void SetToolForCapture(string tool)
    {
        _tool = tool.Equals("formatting", StringComparison.OrdinalIgnoreCase)
            ? BottomTool.Formatting
            : tool.Equals("transforms", StringComparison.OrdinalIgnoreCase)
                ? BottomTool.Transforms
                : BottomTool.None;
        RenderSidebar();
        RenderToolPanel();
    }

    /// <summary>Capture seam for the compact notes rail.</summary>
    public void SetSidebarCollapsedForCapture(bool collapsed)
    {
        _sidebarExpanded = !collapsed;
        RenderSidebar();
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
        _transformCts?.Cancel();
        _transformCts?.Dispose();
        SaveActive(false);
        base.OnClosing(e);
    }
}
