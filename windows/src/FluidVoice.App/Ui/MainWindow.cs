using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Audio;
using FluidVoice.Core;
using FluidVoice.Modes;
using FluidVoice.Stt;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Main window, styled after Wispr Flow: cream canvas, icon-only left rail,
/// a floating white sheet for content, and a transcript-feed Home page.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly DictationCoordinator? _coordinator;
    private readonly CommandModeService? _commandService;
    public Action? OpenCommandWindow;
    public Action? OpenRewriteWindow;

    private readonly ScrollViewer _content = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private readonly Dictionary<string, Border> _navItems = new();
    private string _current = "";
    private string _feedFilter = "";

    private sealed record NavEntry(string Glyph, string Title, Func<UIElement> Page);
    private readonly List<NavEntry> _entries;

    public MainWindow(CommandModeService? commandService = null, DictationCoordinator? coordinator = null)
    {
        _commandService = commandService;
        _coordinator = coordinator;
        Title = "FluidVoice";
        Width = 1120;
        Height = 760;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Theme.Bg);
        ShowInTaskbar = true;
        WindowFx.Apply(this);

        _entries = new List<NavEntry>
        {
            new("", "Home", BuildHomePage),
            new("", "Insights", () => new HomeTab()),
            new("", "Dictionary", () => new DictionaryTab()),
            new("", "History", () => new HistoryTab()),
            new("", "AI Settings", () => Stack(new SpeechModelsTab(), new AiTab())),
            new("", "Command Mode", BuildCommandModePage),
            new("", "Write Mode", BuildWriteModePage),
            new("", "File Transcription", BuildFileTranscriptionPage),
            // rail pins these to the bottom (Wispr-style)
            new("", "Preferences", () => Stack(new GeneralTab(), new FormattingTab())),
            new("", "Feedback", BuildFeedbackPage),
        };

        var root = new Grid { Background = new SolidColorBrush(Theme.Bg) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ----- icon-only rail -----
        var rail = new DockPanel { Margin = new Thickness(8, 14, 2, 14), LastChildFill = false };
        var topGroup = new StackPanel();
        var bottomGroup = new StackPanel();
        foreach (var e in _entries)
            (e.Title is "Preferences" or "Feedback" ? bottomGroup : topGroup).Children.Add(NavItem(e));
        DockPanel.SetDock(topGroup, Dock.Top);
        DockPanel.SetDock(bottomGroup, Dock.Bottom);
        rail.Children.Add(topGroup);
        rail.Children.Add(bottomGroup);
        Grid.SetColumn(rail, 0);
        root.Children.Add(rail);

        // ----- floating white sheet -----
        var sheet = new Border
        {
            Background = Theme.SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 10, 10, 10),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
        };
        _content.Padding = new Thickness(0);
        sheet.Child = _content;
        Grid.SetColumn(sheet, 1);
        root.Children.Add(sheet);

        Content = root;
        SmoothScroll.Attach(_content);
        Navigate("Home");
        HistoryStore.HistoryChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (_current == "Home") Navigate("Home");
        });
        Settings.Changed += _ => Dispatcher.BeginInvoke(() =>
        {
            Background = new SolidColorBrush(Theme.Bg);
            root.Background = new SolidColorBrush(Theme.Bg);
            sheet.Background = Theme.SurfaceBrush;
            sheet.BorderBrush = Theme.HairlineBrush;
        });
    }

    public void SelectTab(string title) => Navigate(
        title switch { "Welcome" => "Home", "General" => "Preferences", "Stats" => "Insights", _ => title });

    private Border NavItem(NavEntry entry)
    {
        var icon = new TextBlock
        {
            Text = entry.Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = new SolidColorBrush(Theme.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = new Border
        {
            Child = icon,
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = entry.Title,
        };
        item.MouseLeftButtonUp += (_, _) => Navigate(entry.Title);
        item.MouseEnter += (_, _) => { if (_current != entry.Title) item.Background = new SolidColorBrush(Theme.SidebarSelected) { Opacity = 0.6 }; };
        item.MouseLeave += (_, _) => { if (_current != entry.Title) item.Background = Brushes.Transparent; };
        _navItems[entry.Title] = item;
        return item;
    }

    private void Navigate(string title)
    {
        var entry = _entries.FirstOrDefault(e => e.Title == title) ?? _entries[0];
        _current = entry.Title;
        foreach (var (name, border) in _navItems)
            border.Background = name == entry.Title ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent;

        var page = new StackPanel { Margin = new Thickness(40, 30, 40, 34), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        if (entry.Title != "Home") page.Children.Add(PageHeader(entry.Title));
        page.Children.Add(entry.Page());
        _content.Content = page;
        _content.ScrollToTop();
    }

    private static UIElement PageHeader(string title) => new TextBlock
    {
        Text = title,
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Theme.Text),
        Margin = new Thickness(0, 0, 0, 20),
    };

    private static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    // =================== Home (Wispr-style: welcome + transcript feed + stats) ===================

    private UIElement BuildHomePage()
    {
        var page = new StackPanel();

        page.Children.Add(new TextBlock
        {
            Text = $"Welcome back, {FirstName()}",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            Margin = new Thickness(0, 0, 0, 22),
        });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

        var main = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };

        var model = SpeechModels.Selected();
        bool setupDone = model.IsDownloaded && Settings.Current.SetupTested;
        if (!setupDone)
        {
            main.Children.Add(BigCard(BuildQuickSetup(model)));
            main.Children.Add(BigCard(BuildHowToUse()));
        }
        main.Children.Add(BuildFeed());
        Grid.SetColumn(main, 0);
        columns.Children.Add(main);

        var side = new StackPanel();
        side.Children.Add(BigCard(BuildStatsPanel()));
        Grid.SetColumn(side, 1);
        columns.Children.Add(side);
        page.Children.Add(columns);
        return page;
    }

    private static string FirstName()
    {
        var name = Environment.UserName;
        if (string.IsNullOrWhiteSpace(name)) return "there";
        name = name.Split(' ', '.', '_', '-')[0];
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private UIElement BuildStatsPanel()
    {
        var panel = new StackPanel();
        void Stat(string value, string label, bool first = false)
        {
            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontFamily = Theme.StatSerif,
                FontSize = 27,
                Foreground = new SolidColorBrush(Theme.Text),
                Margin = new Thickness(0, first ? 0 : 14, 0, 0),
            });
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Theme.SubtleText) });
        }
        var total = HistoryStore.TotalWords;
        Stat(total >= 1000 ? $"{total / 1000.0:0.#}K" : total.ToString(), "total words", first: true);
        Stat(HistoryStore.FormatMinutes(HistoryStore.TimeSavedMinutes(HistoryStore.WordsToday)), "time saved today");
        Stat(HistoryStore.CurrentStreakDays.ToString(), "day streak");
        return panel;
    }

    private UIElement BuildFeed()
    {
        var panel = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(2, 6, 2, 8) };
        var label = new TextBlock
        {
            Text = "TODAY",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.SubtleText),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(label, Dock.Left);
        header.Children.Add(label);

        var searchBox = new TextBox
        {
            Width = 220,
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = string.IsNullOrEmpty(_feedFilter) ? Visibility.Collapsed : Visibility.Visible,
            Text = _feedFilter,
        };
        searchBox.TextChanged += (_, _) => { _feedFilter = searchBox.Text; RebuildFeedRows(); };
        var searchBtn = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Theme.SubtleText),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Search past transcripts",
        };
        searchBtn.MouseLeftButtonUp += (_, _) =>
        {
            searchBox.Visibility = searchBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (searchBox.Visibility == Visibility.Visible) searchBox.Focus();
            else { _feedFilter = ""; searchBox.Text = ""; RebuildFeedRows(); }
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(searchBox);
        right.Children.Add(searchBtn);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        panel.Children.Add(header);

        _feedRows = new StackPanel();
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = _feedRows,
        });
        RebuildFeedRows();
        return panel;
    }

    private StackPanel? _feedRows;

    private void RebuildFeedRows()
    {
        if (_feedRows is null) return;
        _feedRows.Children.Clear();
        var entries = (string.IsNullOrWhiteSpace(_feedFilter) ? HistoryStore.Entries.ToList() : HistoryStore.Search(_feedFilter))
            .Take(30).ToList();
        if (entries.Count == 0)
        {
            _feedRows.Children.Add(new TextBlock
            {
                Text = "No transcripts yet — press your hotkey and start talking.",
                Foreground = new SolidColorBrush(Theme.SubtleText),
                Margin = new Thickness(16, 18, 16, 18),
            });
            return;
        }
        for (int i = 0; i < entries.Count; i++)
        {
            _feedRows.Children.Add(FeedRow(entries[i]));
            if (i < entries.Count - 1)
                _feedRows.Children.Add(new Border { Height = 1, Background = Theme.HairlineBrush });
        }
    }

    private UIElement FeedRow(TranscriptionHistoryEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 12, 12), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = new TextBlock
        {
            Text = entry.Timestamp.ToString("h:mm tt").ToLowerInvariant(),
            Foreground = new SolidColorBrush(Theme.SubtleText),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetColumn(time, 0);
        grid.Children.Add(time);

        var textCol = new StackPanel();
        textCol.Children.Add(new TextBlock
        {
            Text = entry.ProcessedText,
            Foreground = new SolidColorBrush(Theme.Text),
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
        });
        if (entry.WasCancelled)
            textCol.Children.Add(new TextBlock
            {
                Text = "cancelled — not typed",
                Foreground = new SolidColorBrush(Theme.SubtleText),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
        Grid.SetColumn(textCol, 1);
        grid.Children.Add(textCol);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0, VerticalAlignment = VerticalAlignment.Top };
        UIElement ActionIcon(string glyph, string tip, Action click)
        {
            var b = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
                Margin = new Thickness(2, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
                    Foreground = new SolidColorBrush(Theme.SubtleText),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Theme.SidebarSelected);
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return b;
        }
        actions.Children.Add(ActionIcon("", "Copy", () => ClipboardService.SetText(entry.ProcessedText)));
        actions.Children.Add(ActionIcon("", "Delete", () => HistoryStore.DeleteEntries(new[] { entry.Id })));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        grid.MouseEnter += (_, _) => actions.Opacity = 1;
        grid.MouseLeave += (_, _) => actions.Opacity = 0;
        return grid;
    }

    // =================== Quick Setup (shown until complete) ===================

    private UIElement BuildQuickSetup(SpeechModelInfo model)
    {
        var setup = new StackPanel();
        setup.Children.Add(SectionHeader("", "Quick Setup"));

        setup.Children.Add(SetupRow(
            "Voice Model Ready", "Speech recognition model is loaded and ready",
            model.IsDownloaded,
            model.IsDownloaded ? null : ("Download", () => Navigate("AI Settings"))));

        bool micOk = AudioRecorder.ListInputDevices().Count > 0;
        setup.Children.Add(SetupRow(
            "Microphone Available", "FluidVoice can see an input device",
            micOk,
            micOk ? null : ("Open Settings", () => TryOpen("ms-settings:privacy-microphone"))));

        setup.Children.Add(SetupRow(
            "Global Input Hooks Active", "Hotkeys and typing into apps are enabled",
            true, null));

        bool aiOk = !string.IsNullOrEmpty(Settings.Current.SelectedProviderID) &&
                    Ai.ProviderCatalog.IsConfigured(Settings.Current.SelectedProviderID);
        setup.Children.Add(SetupRow(
            "AI Enhancement Configured", aiOk ? "AI-powered text enhancement is ready to use" : "Optional — cloud provider or local AI",
            aiOk,
            aiOk ? null : ("Configure", () => Navigate("AI Settings"))));

        setup.Children.Add(SetupRow(
            "Setup Tested Successfully", "You've successfully tested voice transcription",
            Settings.Current.SetupTested,
            Settings.Current.SetupTested ? null : ("Test now", () => _ = RunTryoutAsync())));
        _tryoutStatus = new TextBlock { Foreground = new SolidColorBrush(Theme.SubtleText), Margin = new Thickness(4, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        setup.Children.Add(_tryoutStatus);
        return setup;
    }

    private UIElement BuildHowToUse()
    {
        var how = new StackPanel();
        how.Children.Add(SectionHeader("", "How to Use"));
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "Right Alt";
        how.Children.Add(NumberRow(1, "Start Recording", $"Press your hotkey (default: {hotkey}) in any app"));
        how.Children.Add(NumberRow(2, "Speak Clearly", "Speak naturally — works best in quiet environments"));
        how.Children.Add(NumberRow(3, "Auto-Type Result", "Transcription is automatically typed into your focused app"));
        return how;
    }

    private TextBlock? _tryoutStatus;

    private async Task RunTryoutAsync()
    {
        if (_coordinator is null || _tryoutStatus is null) return;
        var model = SpeechModels.Selected();
        if (!model.IsDownloaded)
        {
            _tryoutStatus.Text = "Download a speech model first (AI Settings).";
            return;
        }
        try
        {
            _tryoutStatus.Text = "Preparing model…";
            var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
            _tryoutStatus.Text = "🎙 Listening for 5 seconds — say something!";
            var recorder = new AudioRecorder();
            recorder.Start(Settings.Current.PreferredInputDeviceId);
            await Task.Delay(5000);
            var pcm = recorder.Stop();
            recorder.Dispose();
            if (pcm.Length < AudioRecorder.TargetSampleRate)
            {
                var padded = new float[AudioRecorder.TargetSampleRate];
                Array.Copy(pcm, padded, pcm.Length);
                pcm = padded;
            }
            _tryoutStatus.Text = "Transcribing…";
            var text = await engine.TranscribeAsync(pcm, CancellationToken.None);
            var formatted = Text.TranscriptFormatter.Process(text);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                _tryoutStatus.Text = "Heard nothing — check your microphone and try again.";
                return;
            }
            _tryoutStatus.Text = $"✓ You said: “{formatted}”";
            Settings.Current.SetupTested = true;
            Settings.Current.Save();
            Navigate("Home");
        }
        catch (Exception ex)
        {
            _tryoutStatus.Text = $"Test failed: {ex.Message}";
        }
    }

    private static void TryOpen(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
    }

    // =================== Command / Write / Files / Feedback pages ===================

    private UIElement BuildCommandModePage()
    {
        var page = new StackPanel();
        var card = new StackPanel();
        card.Children.Add(new TextBlock
        {
            Text = "Control your PC by voice: launch apps, manage files, run PowerShell — the AI checks prerequisites, executes, and verifies each step.",
            Foreground = new SolidColorBrush(Theme.SubtleText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        card.Children.Add(Theme.Toggle("Enable Command Mode hotkey", Settings.Current.CommandModeShortcutEnabled, v =>
        {
            Settings.Current.CommandModeShortcutEnabled = v;
            Settings.Current.CommandModeShortcut ??= Input.HotkeyShortcut.RightCtrl();
            Settings.Current.Save("hotkey");
        }));
        card.Children.Add(Theme.Toggle("Ask before running destructive commands", Settings.Current.CommandModeConfirmBeforeExecute, v =>
        {
            Settings.Current.CommandModeConfirmBeforeExecute = v;
            Settings.Current.Save();
        }));
        var open = PrimaryButton("Open Command Chat");
        open.Click += (_, _) => OpenCommandWindow?.Invoke();
        card.Children.Add(open);
        if (_commandService is not null && _commandService.RecentChats.Count > 0)
        {
            card.Children.Add(new TextBlock { Text = "Recent chats", Foreground = new SolidColorBrush(Theme.Text), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var chat in _commandService.RecentChats.Take(6))
                card.Children.Add(new TextBlock { Text = $"•  {chat.Title}", Foreground = new SolidColorBrush(Theme.SubtleText), Margin = new Thickness(0, 2, 0, 2) });
        }
        page.Children.Add(BigCard(card));
        return page;
    }

    private UIElement BuildWriteModePage()
    {
        var page = new StackPanel();
        var card = new StackPanel();
        card.Children.Add(new TextBlock
        {
            Text = "Write or rewrite text in any text box in any app. Select text and press the hotkey to rewrite it with a voice instruction, or use it with nothing selected to dictate brand-new content.",
            Foreground = new SolidColorBrush(Theme.SubtleText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        card.Children.Add(Theme.Toggle("Enable Write Mode hotkey (Alt+R)", Settings.Current.RewriteModeShortcutEnabled, v =>
        {
            Settings.Current.RewriteModeShortcutEnabled = v;
            Settings.Current.Save("hotkey");
        }));
        var open = PrimaryButton("Open Edit Window");
        open.Click += (_, _) => OpenRewriteWindow?.Invoke();
        card.Children.Add(open);
        page.Children.Add(BigCard(card));
        return page;
    }

    private UIElement BuildFileTranscriptionPage()
    {
        var page = new StackPanel();
        var card = new StackPanel();
        card.Children.Add(new TextBlock
        {
            Text = "Transcribe an audio file on-device (wav, mp3, m4a, flac, ogg…). Nothing is uploaded.",
            Foreground = new SolidColorBrush(Theme.SubtleText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var status = new TextBlock { Foreground = new SolidColorBrush(Theme.SubtleText), Margin = new Thickness(0, 8, 0, 8) };
        var result = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 180, Padding = new Thickness(10), Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var copyBtn = new Button { Content = "Copy", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        copyBtn.Click += (_, _) => ClipboardService.SetText(result.Text);
        var saveBtn = new Button { Content = "Save as .txt", Padding = new Thickness(12, 5, 12, 5) };
        saveBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text file|*.txt", FileName = "transcript.txt" };
            if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, result.Text);
        };
        buttons.Children.Add(copyBtn);
        buttons.Children.Add(saveBtn);

        var pick = PrimaryButton("Choose audio file…");
        pick.Click += async (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.wma;*.aac|All files|*.*",
            };
            if (dlg.ShowDialog() != true || _coordinator is null) return;
            try
            {
                pick.IsEnabled = false;
                var model = SpeechModels.Selected();
                if (!model.IsDownloaded) { status.Text = "Download a speech model first (AI Settings)."; return; }
                status.Text = "Loading model…";
                var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
                status.Text = "Reading audio…";
                var pcm = await Task.Run(() => AudioFileLoader.Load16kMono(dlg.FileName));
                status.Text = $"Transcribing {pcm.Length / 16000.0 / 60:0.0} min of audio… (stays on this PC)";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = await engine.TranscribeAsync(pcm, CancellationToken.None);
                status.Text = $"✓ Done in {sw.Elapsed.TotalSeconds:0.0}s";
                result.Text = Text.TranscriptFormatter.Process(text);
                result.Visibility = Visibility.Visible;
                buttons.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                status.Text = $"Failed: {ex.Message}";
            }
            finally
            {
                pick.IsEnabled = true;
            }
        };

        card.Children.Add(pick);
        card.Children.Add(status);
        card.Children.Add(result);
        card.Children.Add(buttons);
        page.Children.Add(BigCard(card));
        return page;
    }

    private UIElement BuildFeedbackPage()
    {
        var page = new StackPanel();
        var card = new StackPanel();
        card.Children.Add(new TextBlock
        {
            Text = "Found a bug or have an idea? FluidVoice is open source (GPLv3).",
            Foreground = new SolidColorBrush(Theme.SubtleText), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        var b1 = PrimaryButton("Open GitHub Issues");
        b1.Click += (_, _) => TryOpen("https://github.com/altic-dev/FluidVoice/issues");
        var b2 = new Button { Content = "Join the Discord", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b2.Click += (_, _) => TryOpen("https://discord.gg/VUPHaKSvYV");
        var b3 = new Button { Content = "Open log folder", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b3.Click += (_, _) => TryOpen(AppPaths.LogDir);
        card.Children.Add(b1);
        card.Children.Add(b2);
        card.Children.Add(b3);
        card.Children.Add(new TextBlock
        {
            Text = $"\nFluidVoice for Windows {App.Updater.ThisVersion} — port of altic-dev/FluidVoice (GPLv3).",
            Foreground = new SolidColorBrush(Theme.SubtleText), FontSize = 11, TextWrapping = TextWrapping.Wrap,
        });
        page.Children.Add(BigCard(card));
        return page;
    }

    // =================== shared building blocks ===================

    private static UIElement SectionHeader(string glyph, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10) };
        row.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Foreground = Theme.GreenBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = Theme.GreenBrush, VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static Border BigCard(UIElement child) => new()
    {
        Background = new SolidColorBrush(Theme.Card),
        BorderBrush = new SolidColorBrush(Theme.CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 0, 16),
        Child = child,
    };

    private static Border SetupRow(string title, string subtitle, bool done, (string Label, Action Click)? action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = done ? Theme.GreenBrush : new SolidColorBrush(Color.FromArgb(50, 122, 120, 114)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = done ? "" : "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = new SolidColorBrush(Theme.Text) });
        textCol.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.SubtleText), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(textCol, 1);
        grid.Children.Add(textCol);

        UIElement right;
        if (done)
        {
            right = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(34, 31, 122, 106)),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "✓ Done", FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.GreenBrush,
                },
            };
        }
        else if (action is { } a)
        {
            var btn = new Button
            {
                Content = a.Label, Padding = new Thickness(12, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            btn.Click += (_, _) => a.Click();
            right = btn;
        }
        else
        {
            right = new TextBlock();
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid,
        };
    }

    private static UIElement NumberRow(int n, string title, string subtitle)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(40, 31, 122, 106)),
            Margin = new Thickness(0, 2, 12, 0),
            Child = new TextBlock
            {
                Text = n.ToString(), FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = Theme.GreenBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = new SolidColorBrush(Theme.Text) });
        col.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.SubtleText), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(col);
        return row;
    }

    /// <summary>Dark charcoal pill button (Wispr's "Add new" style).</summary>
    private static Button PrimaryButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(16, 8, 16, 8),
        Margin = new Thickness(0, 6, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = Theme.InkBrush,
        Foreground = new SolidColorBrush(Theme.InkText),
        BorderThickness = new Thickness(0),
    };

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // hide to tray instead of quitting (menu-bar app behavior); Quit via tray menu
        e.Cancel = true;
        Hide();
    }
}

/// <summary>Loads any NAudio-supported audio file as 16k mono float (for file transcription).</summary>
public static class AudioFileLoader
{
    public static float[] Load16kMono(string path)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path);
        int channels = reader.WaveFormat.Channels;
        int srcRate = reader.WaveFormat.SampleRate;
        var all = new List<float>();
        var buf = new float[srcRate * channels];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels && i + c < read; c++) sum += buf[i + c];
                all.Add(sum / channels);
            }
        if (srcRate == 16000) return all.ToArray();
        double ratio = srcRate / 16000.0;
        var output = new float[(int)(all.Count / ratio)];
        for (int i = 0; i < output.Length; i++)
        {
            double pos = i * ratio;
            int i0 = (int)pos;
            float frac = (float)(pos - i0);
            output[i] = all[Math.Min(i0, all.Count - 1)] * (1 - frac) + all[Math.Min(i0 + 1, all.Count - 1)] * frac;
        }
        return output;
    }
}
