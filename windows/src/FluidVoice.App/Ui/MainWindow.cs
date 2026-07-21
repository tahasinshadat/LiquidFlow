using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
/// Main window: warm canvas, icon rail, inset content sheet, and dashboard pages
/// styled after the Wispr Flow desktop hub.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly DictationCoordinator? _coordinator;
    private readonly CommandModeService? _commandService;
    public Action? OpenCommandWindow;
    public Action? OpenRewriteWindow;

    private readonly ScrollViewer _content = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Dictionary<string, Border> _navItems = new();
    private readonly List<TextBlock> _navLabels = new();
    private string _current = "";
    private string _feedFilter = "";
    private bool _sidebarExpanded = true;  // expanded with labels by default (reference layout)
    private bool _didPromptForName;
    private ColumnDefinition? _railColumn;
    private Border? _brandMark;
    private double SidebarWidth => _sidebarExpanded ? 252 : 66;

    private sealed record NavEntry(string Glyph, string Title, Func<UIElement> Page);
    private SizeChangedEventHandler? _voiceBoxSizer;
    private readonly List<NavEntry> _entries;

    public MainWindow(CommandModeService? commandService = null, DictationCoordinator? coordinator = null)
    {
        _commandService = commandService;
        _coordinator = coordinator;
        Title = "LiquidFlow";
        Width = 1240;
        Height = 820;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Theme.Bg);
        ShowInTaskbar = true;
        WindowFx.Apply(this);

        _entries = new List<NavEntry>
        {
            new("\uE720", "Dictation", BuildHomePage),
            new("\uE9D2", "Insights", () => new HomeTab()),
            new("\uE82D", "Dictionary", () => new DictionaryTab()),
            new("\uE8C6", "Snippets", () => new SnippetsTab()),
            new("\uE8D2", "Style", () => new StyleTab()),
            new("\uE945", "Transforms", () => new TransformsTab(() => OpenCommandWindow?.Invoke(), () => OpenRewriteWindow?.Invoke(), _coordinator)),
            new("\uE70B", "Scratchpad", () => new ScratchpadTab()),
            new("\uE716", "Meetings", () => new MeetingsTab(_coordinator)),
            new("\uE767", "VoiceBox", () => App.VoiceBoxNative.IsArm64 && !Settings.Current.VoiceBoxUseEmulated ? new VoiceBoxStudioView() : (UIElement)new VoiceBoxTab()),
            // rail pins these to the bottom (reference layout); Settings opens the modal
            new("\uE713", "Settings", () => new TextBlock()),
            new("\uE897", "Help", BuildFeedbackPage),
        };

        var root = new Grid { Background = new SolidColorBrush(Theme.Bg) };
        _railColumn = new ColumnDefinition { Width = new GridLength(SidebarWidth) };
        root.ColumnDefinitions.Add(_railColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ----- icon rail (12px symmetric margin centers the 42px items in the 66px collapsed column) -----
        _rail = new DockPanel { Margin = new Thickness(12, 2, 12, 16), LastChildFill = false };
        BuildRailContent();
        Grid.SetColumn(_rail, 0);
        root.Children.Add(_rail);

        // ----- floating white sheet -----
        var sheet = new Border
        {
            Background = Theme.SurfaceBrush,
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 14, 14),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
        };
        _content.Padding = new Thickness(0);
        sheet.Child = _content;
        WindowFx.RoundClip(_content, 17); // scrollbar stays inside the rounded sheet
        Grid.SetColumn(sheet, 1);
        root.Children.Add(sheet);

        // ----- in-app titlebar above everything (part of the design, not an appended bar) -----
        var outer = new Grid { Background = new SolidColorBrush(Theme.Bg) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // titlebar
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // update banner
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        var titlebar = WindowFx.InstallChrome(this, "LiquidFlow", BuildTitlebarLeading(), showBrand: false);
        Grid.SetRow((UIElement)titlebar, 0);
        outer.Children.Add((UIElement)titlebar);
        _updateBanner = BuildUpdateBanner();
        Grid.SetRow(_updateBanner, 1);
        outer.Children.Add(_updateBanner);
        Grid.SetRow(root, 2);
        outer.Children.Add(root);
        // reflect any update already found before this window was wired up
        SetUpdateAvailable(App.UpdateCoordinator.Pending);

        Content = outer;
        SmoothScroll.Attach(_content);
        Navigate("Dictation");
        Loaded += (_, _) => PromptForNameIfNeeded();
        // dev seam: FLUIDVOICE_OPEN_SETTINGS=1 opens the settings modal on launch (screenshot tests)
        if (Environment.GetEnvironmentVariable("FLUIDVOICE_OPEN_SETTINGS") == "1")
            Loaded += (_, _) => Dispatcher.BeginInvoke(() => ShowSettingsDialog());
        HistoryStore.HistoryChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (_current == "Dictation") Navigate("Dictation");
        });
        Settings.Changed += hint => Dispatcher.BeginInvoke(() =>
        {
            Background = new SolidColorBrush(Theme.Bg);
            root.Background = new SolidColorBrush(Theme.Bg);
            outer.Background = new SolidColorBrush(Theme.Bg);
            sheet.Background = Theme.SurfaceBrush;
            sheet.BorderBrush = Theme.HairlineBrush;
            // theme/font swaps must re-render the whole surface (colors are snapshotted per control)
            if (hint is "theme" or "font")
            {
                RebuildRail();
                Navigate(_current);
            }
            else if (hint == "home" && _current == "Dictation")
            {
                Navigate("Dictation"); // reflect the setup-checklist toggle immediately
            }
        });
    }

    private Border? _updateBanner;
    private TextBlock? _updateBannerLabel;

    private Border BuildUpdateBanner()
    {
        _updateBannerLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var install = new Button
        {
            Content = "Install now",
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(14, 0, 6, 0),
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Theme.Green),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        install.Click += (_, _) => _ = App.UpdateCoordinator.InstallAsync();
        var later = new Button
        {
            Content = "Later",
            Padding = new Thickness(10, 5, 10, 5),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        later.Click += (_, _) => { if (_updateBanner is not null) _updateBanner.Visibility = Visibility.Collapsed; };

        var row = new DockPanel { Margin = new Thickness(20, 0, 14, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(install);
        actions.Children.Add(later);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        var glyphAndText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        glyphAndText.Children.Add(new TextBlock
        {
            Text = "", // Download glyph
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        glyphAndText.Children.Add(_updateBannerLabel);
        row.Children.Add(glyphAndText);

        return new Border
        {
            Background = new SolidColorBrush(Theme.Green),
            Padding = new Thickness(0, 9, 0, 9),
            Visibility = Visibility.Collapsed,
            Child = row,
        };
    }

    /// <summary>Show/hide the in-app "update available" banner (driven by UpdateCoordinator).</summary>
    public void SetUpdateAvailable(UpdateInfo? info)
    {
        if (_updateBanner is null || _updateBannerLabel is null) return;
        if (info is null)
        {
            _updateBanner.Visibility = Visibility.Collapsed;
            return;
        }
        _updateBannerLabel.Text = $"LiquidFlow {info.Version} is available.";
        _updateBanner.Visibility = Visibility.Visible;
    }

    /// <summary>Capture-harness seam: navigate directly to a page by nav title.</summary>
    public void CaptureNavigate(string title) => Navigate(title);

    public void SelectTab(string title)
    {
        switch (title)
        {
            case "General" or "Preferences" or "Settings": ShowSettingsDialog("General"); break;
            case "AI Settings" or "Models" or "Speech Models": ShowSettingsDialog("Speech Models"); break;
            case "Welcome" or "Home" or "History": Navigate("Dictation"); break;
            case "Stats": Navigate("Insights"); break;
            case "Feedback": Navigate("Help"); break;
            case "Command Mode" or "Write Mode" or "File Transcription": Navigate("Transforms"); break;
            default: Navigate(title); break;
        }
    }

    /// <summary>Top-left chrome cluster (reference layout): sidebar collapse + account.</summary>
    private UIElement BuildTitlebarLeading()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Border Btn(string glyph, string tip, Action onClick)
        {
            var b = PageChrome.IconButton(glyph, tip, onClick);
            b.Width = 34;
            b.Height = 34;
            ((TextBlock)b.Child).FontSize = 15;
            ((TextBlock)b.Child).Foreground = Theme.TextBrush;
            return b;
        }
        row.Children.Add(Btn("\uE700", "Toggle sidebar", () => SetSidebarExpanded(!_sidebarExpanded)));
        row.Children.Add(Btn("\uE77B", "Account & settings", () => ShowSettingsDialog("Account")));
        return row;
    }

    private UIElement BrandMark()
    {
        // brand row at the top of the sidebar (reference layout: "Flow"-style wordmark);
        // the collapse + account buttons live in the titlebar now.
        var mark = new Image
        {
            Width = 24,
            Height = 24,
            Source = WindowFx.AppIconLarge,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = "LiquidFlow",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        _navLabels.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 4, 0, 2) };
        row.Children.Add(mark);
        row.Children.Add(label);
        _brandMark = new Border { Background = Brushes.Transparent, Child = row };
        return _brandMark;
    }

    private Border NavItem(NavEntry entry)
    {
        var icon = new TextBlock
        {
            Text = entry.Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = new SolidColorBrush(Theme.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 42, // matches the collapsed item width so the glyph never shifts on expand
            TextAlignment = TextAlignment.Center, // Width alone left-aligns the glyph inside the box
        };
        var label = new TextBlock
        {
            Text = entry.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        _navLabels.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(label);
        var item = new Border
        {
            Child = row,
            Width = _sidebarExpanded ? 220 : 42,
            Height = 42,
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = entry.Title,
        };
        item.MouseLeftButtonUp += (_, _) =>
        {
            if (entry.Title == "Settings") ShowSettingsDialog();
            else Navigate(entry.Title);
        };
        AttachHoverFx(item, () => _current == entry.Title);
        _navItems[entry.Title] = item;
        return item;
    }

    /// <summary>Soft fade-in/out hover highlight (no instant color jumps).</summary>
    private static void AttachHoverFx(Border item, Func<bool> isSelected)
    {
        var hover = new SolidColorBrush(Theme.SidebarSelected) { Opacity = 0 };
        item.MouseEnter += (_, _) =>
        {
            if (isSelected()) return;
            item.Background = hover;
            hover.BeginAnimation(Brush.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.7, TimeSpan.FromMilliseconds(110)));
        };
        item.MouseLeave += (_, _) =>
        {
            if (isSelected()) return;
            hover.BeginAnimation(Brush.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        };
    }

    private DockPanel? _rail;

    private void BuildRailContent()
    {
        if (_rail is null) return;
        _rail.Children.Clear();
        _navItems.Clear();
        _navLabels.Clear();
        var topGroup = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var bottomGroup = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        topGroup.Children.Add(BrandMark());
        topGroup.Children.Add(new Border { Height = 18, Background = Brushes.Transparent });
        foreach (var e in _entries)
            (e.Title is "Settings" or "Help" ? bottomGroup : topGroup).Children.Add(NavItem(e));
        DockPanel.SetDock(topGroup, Dock.Top);
        DockPanel.SetDock(bottomGroup, Dock.Bottom);
        _rail.Children.Add(topGroup);
        _rail.Children.Add(bottomGroup);
        // restore the selected-item highlight
        if (_navItems.TryGetValue(_current, out var sel))
            sel.Background = new SolidColorBrush(Theme.SidebarSelected);
    }

    private void RebuildRail() => BuildRailContent();

    private void SetSidebarExpanded(bool expanded)
    {
        _sidebarExpanded = expanded;
        if (_railColumn is not null)
            _railColumn.Width = new GridLength(SidebarWidth);
        foreach (var label in _navLabels)
            label.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        foreach (var border in _navItems.Values)
            border.Width = _sidebarExpanded ? 220 : 42;
    }

    private void ShowSettingsDialog(string section = "General")
    {
        var oldOpacity = Opacity;
        try
        {
            Opacity = 0.62;
            var dialog = new SettingsModal(section) { Owner = this };
            dialog.ShowDialog();
        }
        finally
        {
            Opacity = oldOpacity;
            if (_current == "Dictation") Navigate("Dictation");
        }
    }

    private void PromptForNameIfNeeded()
    {
        // First run gets the full OpenWhispr-style wizard (name, hotkey, model, AI);
        // the lone name dialog only remains for upgraders who somehow lack a name.
        if (App.UiCapture.CaptureMode) return;
        if (_didPromptForName) return;
        _didPromptForName = true;
        if (!Settings.Current.OnboardingCompleted)
        {
            RunSetupWizard();
            return;
        }
        if (!string.IsNullOrWhiteSpace(Settings.Current.DisplayName)) return;
        var oldOpacity = Opacity;
        try
        {
            Opacity = 0.72;
            var dialog = new NamePromptDialog(FallbackFirstName()) { Owner = this };
            dialog.ShowDialog();
        }
        finally
        {
            Opacity = oldOpacity;
            if (_current == "Dictation") Navigate("Dictation");
        }
    }

    public void RunSetupWizard()
    {
        var oldOpacity = Opacity;
        try
        {
            Opacity = 0.62;
            var wizard = new OnboardingWindow { Owner = this };
            wizard.ShowDialog();
        }
        finally
        {
            Opacity = oldOpacity;
            if (_current == "Dictation") Navigate("Dictation");
        }
    }

    private void Navigate(string title)
    {
        if (title is "Preferences" or "Settings")
        {
            ShowSettingsDialog();
            return;
        }
        if (title is "Models" or "AI Settings" or "Speech Models")
        {
            ShowSettingsDialog("Speech Models");
            return;
        }

        var entry = _entries.FirstOrDefault(e => e.Title == title) ?? _entries[0];
        _current = entry.Title;
        foreach (var (name, border) in _navItems)
            border.Background = name == entry.Title ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent;

        if (_voiceBoxSizer is not null)
        {
            _content.SizeChanged -= _voiceBoxSizer;
            _voiceBoxSizer = null;
        }
        // The full-bleed embed host is only for the EMULATED x64 desktop app. On ARM64 the
        // VoiceBox tab is a normal native LiquidFlow page (VoiceBoxStudioView) below.
        if (entry.Title == "VoiceBox" && (!App.VoiceBoxNative.IsArm64 || Settings.Current.VoiceBoxUseEmulated))
        {
            var host = VoiceBoxHostView.Instance;
            _content.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            void SizeHost() =>
                host.Height = Math.Max(420, _content.ViewportHeight > 1 ? _content.ViewportHeight : _content.ActualHeight);
            _voiceBoxSizer = (_, _) => SizeHost();
            _content.SizeChanged += _voiceBoxSizer;
            SizeHost();
            _content.Content = host;
            _content.ScrollToTop();
            return;
        }
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Visible; // reserved lane on normal pages

        // A centered StackPanel with only MaxWidth is shrink-wrapped by WPF to its
        // current child's desired width. That made pages—and even Style sub-tabs—jump.
        var page = new StackPanel
        {
            Margin = PageChrome.PageMargin,
            MaxWidth = PageChrome.PageMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            LayoutTransform = Theme.PageScale(),
        };
        if (entry.Title is not ("Dictation" or "Snippets" or "Scratchpad" or "Dictionary" or "Meetings" or "Transforms" or "VoiceBox"))
            page.Children.Add(PageHeader(entry.Title));
        page.Children.Add(entry.Page());
        var pageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        pageHost.Children.Add(page);
        _content.Content = pageHost;
        _content.ScrollToTop();
    }

    // Same builder the self-headed pages use, so every page title has identical size/spacing.
    private static UIElement PageHeader(string title) => PageChrome.HeaderRow(title, null, null);

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
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Text),
            Margin = new Thickness(0, 0, 0, 24),
        });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });

        var main = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };

        var model = SpeechModels.Selected();
        bool setupDone = model.IsDownloaded && Settings.Current.SetupTested;
        main.Children.Add(BuildHomeHero(model, setupDone));
        // The setup checklist + how-to are opt-in (Settings → General); hidden by default so
        // Home stays clean once you're up and running.
        if (!setupDone && Settings.Current.ShowHomeSetup)
            main.Children.Add(BuildSetupStrip(model));
        main.Children.Add(BuildFeed());
        Grid.SetColumn(main, 0);
        columns.Children.Add(main);

        // stats live on the Insights page only (no repeated information across pages)
        var side = new StackPanel();
        side.Children.Add(BuildVoiceProfilePanel(model, setupDone));
        Grid.SetColumn(side, 1);
        columns.Children.Add(side);
        page.Children.Add(columns);
        return page;
    }

    private UIElement BuildHomeHero(SpeechModelInfo model, bool setupDone)
    {
        // Warm, blurred-photo-inspired hero treatment used throughout the reference UI.
        var hero = new Grid { Height = 190, ClipToBounds = true };
        hero.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(14, 14, 15), 0),
                    new(Color.FromRgb(27, 23, 21), 0.55),
                    new(Color.FromRgb(54, 39, 26), 1),
                },
                new Point(0, 0.3),
                new Point(1, 0.9)),
        });
        hero.Children.Add(new Border
        {
            Width = 320,
            Height = 190,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(18),
            Opacity = 0.82,
            Background = new RadialGradientBrush(
                Color.FromArgb(190, 196, 128, 58), Color.FromArgb(0, 196, 128, 58))
            {
                Center = new Point(0.75, 0.35),
                GradientOrigin = new Point(0.75, 0.35),
                RadiusX = 0.7,
                RadiusY = 0.9,
            },
        });

        var content = new StackPanel
        {
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(36, 30, 36, 30),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headline = new TextBlock
        {
            FontFamily = Theme.DisplaySerif,
            FontSize = 28,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        headline.Inlines.Add(new Run("Make LiquidFlow sound like "));
        headline.Inlines.Add(new Run("you") { FontStyle = FontStyles.Italic });
        content.Children.Add(headline);
        content.Children.Add(new TextBlock
        {
            Text = "Set up different writing styles for different apps.",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        });
        var cta = Theme.SecondaryButton("Start now");
        cta.Click += (_, _) => Navigate("Style");
        content.Children.Add(cta);
        hero.Children.Add(content);

        return new Border
        {
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 0, 26),
            Child = hero,
        };
    }

    private UIElement BuildSetupStrip(SpeechModelInfo model)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var quick = Theme.Panel(BuildQuickSetup(model), new Thickness(18), new Thickness(0, 0, 8, 16));
        Grid.SetColumn(quick, 0);
        grid.Children.Add(quick);

        var how = Theme.Panel(BuildHowToUse(), new Thickness(18), new Thickness(8, 0, 0, 16));
        Grid.SetColumn(how, 1);
        grid.Children.Add(how);
        return grid;
    }

    private static string FirstName()
    {
        var display = Settings.Current.DisplayName;
        if (!string.IsNullOrWhiteSpace(display))
        {
            var first = display.Trim().Split(' ', '.', '_', '-')[0];
            return char.ToUpperInvariant(first[0]) + first[1..];
        }
        return FallbackFirstName();
    }

    private static string FallbackFirstName()
    {
        var name = Environment.UserName;
        if (string.IsNullOrWhiteSpace(name)) return "there";
        name = name.Split(' ', '.', '_', '-')[0];
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string Greeting() => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    private static string WelcomeMessage()
    {
        var messages = new[]
        {
            "Ready when you are. Speak naturally and LiquidFlow will clean up the rest.",
            "Your voice workspace is ready. Start dictating in any app.",
            "Keep your hands on the work. LiquidFlow will handle the words.",
        };
        return messages[DateTime.Today.DayOfYear % messages.Length];
    }

    private UIElement BuildVoiceProfilePanel(SpeechModelInfo model, bool setupDone)
    {
        var panel = new StackPanel();
        panel.Children.Add(StatRow(FormatCompact(HistoryStore.TotalWords), "total words"));
        panel.Children.Add(StatRow(Settings.Current.UserTypingWPM.ToString(), "wpm"));
        panel.Children.Add(StatRow(HistoryStore.CurrentStreakDays.ToString(), "day streak"));
        panel.Children.Add(Theme.Divider(14, 20));
        panel.Children.Add(new TextBlock
        {
            Text = "Your Voice Profile",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Keep using LiquidFlow for new insights",
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var progress = Math.Clamp((HistoryStore.TotalWords % 1000) / 1000.0 + 0.05, 0.05, 1.0);
        var track = new Grid { Width = 112, Height = 7, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Border
        {
            Background = new SolidColorBrush(Theme.SidebarSelected),
            CornerRadius = new CornerRadius(3.5),
        });
        track.Children.Add(new Border
        {
            Width = 112 * progress,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.PurpleBrush,
            CornerRadius = new CornerRadius(3.5),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(track);
        row.Children.Add(new TextBlock
        {
            Text = $"Updates in {FormatCompact(Math.Max(1, 1000 - HistoryStore.TotalWords % 1000))} words",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(row);

        return Theme.Panel(panel, new Thickness(24), new Thickness(0, 0, 0, 18));
    }

    private static UIElement StatRow(string number, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(new TextBlock { Text = number, FontFamily = Theme.StatSerif, FontSize = 33, Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 14.5, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
        return row;
    }

    /// <summary>97.6K-style compact numbers for the stats rail.</summary>
    private static string FormatCompact(int n) => n >= 100_000 ? $"{n / 1000.0:0}K" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    private static string FeedDateHeader(DateTime t) =>
        t.Date == DateTime.Today ? "TODAY" : t.ToString("MMMM d, yyyy").ToUpperInvariant();

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
        _feedDateLabel = label;
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
            CornerRadius = new CornerRadius(8),
            Child = _feedRows,
        });
        RebuildFeedRows();
        return panel;
    }

    private StackPanel? _feedRows;
    private TextBlock? _feedDateLabel;

    /// <summary>Edit a past transcription (fix/delete words); optionally teach the change to the dictionary.</summary>
    private void EditEntry(TranscriptionHistoryEntry entry)
    {
        var oldText = entry.ProcessedText;
        var dlg = new EditTranscriptDialog(oldText) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var newText = dlg.ResultText;
        if (newText == oldText) return;

        HistoryStore.UpdateEntry(entry.Id, newText);
        if (dlg.AddToDictionary)
        {
            var learned = Text.CorrectionLearner.LearnFromManualEdit(oldText, newText);
            if (learned.Count > 0)
            {
                Text.TranscriptFormatter.InvalidateDictionaryCache();
                var summary = string.Join(", ", learned.Select(p => p.To.Length == 0 ? $"remove “{p.From}”" : $"“{p.From}”→“{p.To}”").Take(4));
                App.Notifications.Show("Dictionary updated", $"Now fixing: {summary}");
            }
        }
    }

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
                Text = "No transcripts yet. Press your hotkey and start talking.",
                Foreground = new SolidColorBrush(Theme.SubtleText),
                Margin = new Thickness(16, 18, 16, 18),
            });
            return;
        }
        if (_feedDateLabel is not null)
            _feedDateLabel.Text = FeedDateHeader(entries[0].Timestamp);
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0 && entries[i].Timestamp.Date != entries[i - 1].Timestamp.Date)
                _feedRows.Children.Add(new TextBlock
                {
                    Text = FeedDateHeader(entries[i].Timestamp),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Theme.SubtleText),
                    Margin = new Thickness(18, 20, 0, 10),
                });
            _feedRows.Children.Add(FeedRow(entries[i]));
            if (i < entries.Count - 1)
                _feedRows.Children.Add(new Border { Height = 1, Background = Theme.HairlineBrush });
        }
    }

    private UIElement FeedRow(TranscriptionHistoryEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(18, 16, 14, 16), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
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
        actions.Children.Add(ActionIcon(((char)0xE70F).ToString(), "Edit / fix words", () => EditEntry(entry)));
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
            model.IsDownloaded ? null : ("Download", () => Navigate("Models"))));

        bool micOk = AudioRecorder.ListInputDevices().Count > 0;
        setup.Children.Add(SetupRow(
            "Microphone Available", "LiquidFlow can see an input device",
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
            aiOk ? null : ("Configure", () => Navigate("Models"))));

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
            _tryoutStatus.Text = "Listening for 5 seconds — say something…";
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
            var text = await engine.TranscribeAsync(Dsp.Normalize(pcm), CancellationToken.None);
            var formatted = Text.TranscriptFormatter.Process(text);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                _tryoutStatus.Text = "Heard nothing — check your microphone and try again.";
                return;
            }
            _tryoutStatus.Text = $"✓ You said: “{formatted}”";
            Settings.Current.SetupTested = true;
            Settings.Current.Save();
            Navigate("Dictation");
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

    private UIElement BuildScratchpadPage()
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock
        {
            Text = "Dictate, rewrite, run commands, or transcribe a file from one compact workspace.",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -8, 0, 24),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(ScratchpadCard("Write Mode", "Rewrite selected text or dictate a fresh draft into the focused app.", BuildWriteModeControls()));
        left.Children.Add(ScratchpadCard("Command Mode", "Use voice instructions to operate your PC with confirmation controls.", BuildCommandModeControls()));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = ScratchpadCard("File Transcription", "Turn an audio file into text locally. Nothing is uploaded.", BuildFileTranscriptionControls());
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        page.Children.Add(grid);
        return page;
    }

    private UIElement ScratchpadCard(string title, string subtitle, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        panel.Children.Add(body);
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 18));
    }

    private UIElement BuildCommandModeControls()
    {
        var card = new StackPanel();
        card.Children.Add(Theme.Toggle("Enable Command Mode hotkey", Settings.Current.CommandModeShortcutEnabled, v =>
        {
            Settings.Current.CommandModeShortcutEnabled = v;
            Settings.Current.CommandModeShortcut ??= Input.HotkeyShortcut.RightCtrl();
            Settings.Current.Save("hotkey");
        }));
        card.Children.Add(Theme.Toggle("Ask before destructive commands", Settings.Current.CommandModeConfirmBeforeExecute, v =>
        {
            Settings.Current.CommandModeConfirmBeforeExecute = v;
            Settings.Current.Save();
        }));
        var open = Theme.PrimaryButton("Open command chat");
        open.Margin = new Thickness(0, 12, 0, 0);
        open.Click += (_, _) => OpenCommandWindow?.Invoke();
        card.Children.Add(open);
        return card;
    }

    private UIElement BuildWriteModeControls()
    {
        var card = new StackPanel();
        card.Children.Add(Theme.Toggle("Enable Write Mode hotkey", Settings.Current.RewriteModeShortcutEnabled, v =>
        {
            Settings.Current.RewriteModeShortcutEnabled = v;
            Settings.Current.Save("hotkey");
        }));
        var open = Theme.PrimaryButton("Open edit window");
        open.Margin = new Thickness(0, 12, 0, 0);
        open.Click += (_, _) => OpenRewriteWindow?.Invoke();
        card.Children.Add(open);
        return card;
    }

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

    private UIElement BuildFileTranscriptionControls()
    {
        var card = new StackPanel();
        var status = new TextBlock { Foreground = new SolidColorBrush(Theme.SubtleText), Margin = new Thickness(0, 8, 0, 8) };
        var result = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 220,
            Padding = new Thickness(10),
            Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var copyBtn = Theme.SecondaryButton("Copy");
        copyBtn.Margin = new Thickness(0, 0, 8, 0);
        copyBtn.Click += (_, _) => ClipboardService.SetText(result.Text);
        var saveBtn = Theme.SecondaryButton("Save as .txt");
        saveBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text file|*.txt", FileName = "transcript.txt" };
            if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, result.Text);
        };
        buttons.Children.Add(copyBtn);
        buttons.Children.Add(saveBtn);

        var pick = Theme.PrimaryButton("Choose audio file");
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
                if (!model.IsDownloaded) { status.Text = "Download a speech model first."; return; }
                status.Text = "Loading model...";
                var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
                status.Text = "Reading audio...";
                var pcm = await Task.Run(() => AudioFileLoader.Load16kMono(dlg.FileName));
                status.Text = $"Transcribing {pcm.Length / 16000.0 / 60:0.0} min of audio...";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = await engine.TranscribeAsync(Dsp.Normalize(pcm), CancellationToken.None);
                status.Text = $"Done in {sw.Elapsed.TotalSeconds:0.0}s";
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
        return card;
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
                var text = await engine.TranscribeAsync(Dsp.Normalize(pcm), CancellationToken.None);
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
            Text = "Found a bug or have an idea? LiquidFlow is open source (GPLv3).",
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
            Text = $"\nLiquidFlow for Windows {App.Updater.ThisVersion} — based on altic-dev/FluidVoice (GPLv3).",
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
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(20),
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

        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        textCol.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = new SolidColorBrush(Theme.Text), TextWrapping = TextWrapping.Wrap });
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
