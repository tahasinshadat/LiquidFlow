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
/// Main window — replica of the mac app shell (ContentView.swift): left sidebar
/// (Welcome / AI Settings / Command Mode / Write Mode / File Transcription / Stats /
/// History / Preferences / Feedback) + content pages.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly DictationCoordinator? _coordinator;
    private readonly CommandModeService? _commandService;
    public Action? OpenCommandWindow;
    public Action? OpenRewriteWindow;

    private readonly StackPanel _nav = new();
    private readonly ScrollViewer _content = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // constrain children to viewport width
    };
    private readonly Dictionary<string, Border> _navItems = new();
    private string _current = "";

    private sealed record NavEntry(string Glyph, string Title, Func<UIElement> Page);
    private readonly List<NavEntry> _entries;

    public MainWindow(CommandModeService? commandService = null, DictationCoordinator? coordinator = null)
    {
        _commandService = commandService;
        _coordinator = coordinator;
        Title = "FluidVoice";
        Width = 1080;
        Height = 740;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Theme.Bg);
        ShowInTaskbar = true;
        WindowFx.Apply(this);

        _entries = new List<NavEntry>
        {
            new("", "Welcome", () => BuildWelcomePage()),
            new("", "AI Settings", () => Stack(new SpeechModelsTab(), new AiTab())),
            new("", "Command Mode", () => BuildCommandModePage()),
            new("", "Write Mode", () => BuildWriteModePage()),
            new("", "File Transcription", () => BuildFileTranscriptionPage()),
            new("", "Stats", () => new HomeTab()),
            new("", "History", () => new HistoryTab()),
            new("", "Preferences", () => Stack(new GeneralTab(), new FormattingTab(), new DictionaryTab())),
            new("", "Feedback", () => BuildFeedbackPage()),
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ----- sidebar -----
        var side = new Border
        {
            Background = new SolidColorBrush(Theme.Sidebar),
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        _nav.Margin = new Thickness(10, 14, 10, 14);
        foreach (var e in _entries) _nav.Children.Add(NavItem(e));
        side.Child = _nav;
        Grid.SetColumn(side, 0);
        root.Children.Add(side);

        _content.Padding = new Thickness(0);
        Grid.SetColumn(_content, 1);
        root.Children.Add(_content);

        Content = root;
        Navigate("Welcome");
        Settings.Changed += _ => Dispatcher.BeginInvoke(() => Background = new SolidColorBrush(Theme.Bg));
    }

    public void SelectTab(string title) => Navigate(
        title switch { "Home" => "Welcome", "General" => "Preferences", "Dictionary" => "Preferences", _ => title });

    private Border NavItem(NavEntry entry)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = entry.Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Width = 26,
            Foreground = new SolidColorBrush(Theme.SubtleText),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var item = new Border
        {
            Child = row,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        item.MouseLeftButtonUp += (_, _) => Navigate(entry.Title);
        item.MouseEnter += (_, _) => { if (_current != entry.Title) item.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)); };
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

        var page = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(PageHeader(entry.Glyph, entry.Title == "Welcome" ? "Welcome to FluidVoice" : entry.Title));
        page.Children.Add(entry.Page());
        _content.Content = page;
        _content.ScrollToTop();
    }

    private static UIElement PageHeader(string glyph, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        row.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            Foreground = Theme.GreenBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    // =================== Welcome (Quick Setup + How to Use) ===================

    private UIElement BuildWelcomePage()
    {
        var page = new StackPanel();

        var setup = new StackPanel();
        setup.Children.Add(SectionHeader("", "Quick Setup"));

        var model = SpeechModels.Selected();
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
            "Global Input Hooks Active", "Hotkeys and typing into apps are enabled (no extra permission needed on Windows)",
            true, null));

        bool aiOk = !string.IsNullOrEmpty(Settings.Current.SelectedProviderID) &&
                    Ai.ProviderCatalog.IsConfigured(Settings.Current.SelectedProviderID);
        setup.Children.Add(SetupRow(
            "AI Enhancement Configured", aiOk ? "AI-powered text enhancement is ready to use" : "Optional — cloud provider or local AI",
            aiOk,
            aiOk ? null : ("Configure", () => Navigate("AI Settings"))));

        _tryoutRow = SetupRow(
            "Setup Tested Successfully", "You've successfully tested voice transcription",
            Settings.Current.SetupTested,
            Settings.Current.SetupTested ? null : ("Test now", () => _ = RunTryoutAsync()));
        setup.Children.Add(_tryoutRow);
        _tryoutStatus = new TextBlock { Foreground = new SolidColorBrush(Theme.SubtleText), Margin = new Thickness(4, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        setup.Children.Add(_tryoutStatus);

        page.Children.Add(BigCard(setup));

        var how = new StackPanel();
        how.Children.Add(SectionHeader("", "How to Use"));
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "Right Alt";
        how.Children.Add(NumberRow(1, "Start Recording", $"Press your hotkey (default: {hotkey}) in any app"));
        how.Children.Add(NumberRow(2, "Speak Clearly", "Speak naturally — works best in quiet environments"));
        how.Children.Add(NumberRow(3, "Auto-Type Result", "Transcription is automatically typed into your focused app"));
        page.Children.Add(BigCard(how));

        return page;
    }

    private Border? _tryoutRow;
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
            await _coordinator.Whisper.PrepareAsync(model, null, CancellationToken.None);
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
            var text = await _coordinator.Whisper.TranscribeAsync(pcm, CancellationToken.None);
            var formatted = Text.TranscriptFormatter.Process(text);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                _tryoutStatus.Text = "Heard nothing — check your microphone and try again.";
                return;
            }
            _tryoutStatus.Text = $"✓ You said: “{formatted}”";
            Settings.Current.SetupTested = true;
            Settings.Current.Save();
            Navigate("Welcome");
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
            Background = new SolidColorBrush(Theme.Field), Foreground = new SolidColorBrush(Theme.Text),
            BorderBrush = new SolidColorBrush(Theme.CardBorder), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
                await _coordinator.Whisper.PrepareAsync(model, null, CancellationToken.None);
                status.Text = "Reading audio…";
                var pcm = await Task.Run(() => AudioFileLoader.Load16kMono(dlg.FileName));
                status.Text = $"Transcribing {pcm.Length / 16000.0 / 60:0.0} min of audio… (stays on this PC)";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = await _coordinator.Whisper.TranscribeAsync(pcm, CancellationToken.None);
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

    // =================== shared building blocks (mac look) ===================

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

    /// <summary>One Quick-Setup row: check circle, title/sub, Done pill or action button.</summary>
    private static Border SetupRow(string title, string subtitle, bool done, (string Label, Action Click)? action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = done ? Theme.GreenBrush : new SolidColorBrush(Color.FromArgb(60, 155, 160, 166)),
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
                Background = new SolidColorBrush(Color.FromArgb(45, 48, 209, 88)),
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
            Background = new SolidColorBrush(Color.FromArgb(55, 48, 209, 88)),
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

    private static Button PrimaryButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 6, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = Theme.AccentBrush,
        Foreground = Brushes.White,
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
