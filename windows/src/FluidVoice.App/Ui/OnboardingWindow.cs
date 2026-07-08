using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FluidVoice.Core;
using FluidVoice.Input;
using FluidVoice.Stt;

namespace FluidVoice.Ui;

/// <summary>
/// First-run setup wizard (OpenWhispr-style stepped onboarding):
/// welcome/name → hotkey → speech model → AI cleanup → finish.
/// Every step persists as soon as it changes, so closing the wizard early
/// loses nothing; only Finish marks onboarding complete (it re-offers next launch).
/// </summary>
public sealed class OnboardingWindow : Window
{
    private readonly ContentControl _stepHost = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _backBtn;
    private readonly Button _nextBtn;
    private TextBox? _nameBox;
    private int _step;
    private const int StepCount = 5;
    private CancellationTokenSource? _downloadCts;

    public OnboardingWindow()
    {
        Title = "Set up LiquidFlow";
        Width = 680;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        FontFamily = Theme.UiFont;

        var shell = new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
        };
        WindowFx.RoundClip(shell, 18);
        shell.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var grid = new Grid { Margin = new Thickness(44, 40, 44, 28) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _stepHost,
        };
        grid.Children.Add(scroller);

        var footer = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _backBtn = Theme.SecondaryButton("Back");
        _backBtn.Click += (_, _) => GoTo(_step - 1);
        footer.Children.Add(_backBtn);

        Grid.SetColumn(_dots, 1);
        footer.Children.Add(_dots);

        _nextBtn = Theme.PrimaryButton("Next");
        _nextBtn.HorizontalAlignment = HorizontalAlignment.Right;
        _nextBtn.MinWidth = 110;
        _nextBtn.Click += (_, _) => OnNext();
        Grid.SetColumn(_nextBtn, 2);
        footer.Children.Add(_nextBtn);

        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);

        shell.Child = grid;
        Content = shell;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _downloadCts?.Cancel();
        GoTo(0);
    }

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, StepCount - 1);
        _stepHost.Content = _step switch
        {
            0 => BuildWelcome(),
            1 => BuildHotkey(),
            2 => BuildModel(),
            3 => BuildAi(),
            _ => BuildFinish(),
        };
        _backBtn.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _nextBtn.Content = _step == StepCount - 1 ? "Finish" : "Next";
        RefreshDots();
    }

    private void OnNext()
    {
        if (_step == 0 && _nameBox is not null)
        {
            Settings.Current.DisplayName = _nameBox.Text.Trim();
            Settings.Current.Save("profile");
        }
        if (_step == StepCount - 1)
        {
            Settings.Current.OnboardingCompleted = true;
            Settings.Current.Save();
            Close();
            return;
        }
        GoTo(_step + 1);
    }

    private void RefreshDots()
    {
        _dots.Children.Clear();
        for (int i = 0; i < StepCount; i++)
        {
            _dots.Children.Add(new Ellipse
            {
                Width = i == _step ? 9 : 7,
                Height = i == _step ? 9 : 7,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = i == _step ? Theme.AccentBrush : new SolidColorBrush(Theme.SidebarSelected),
            });
        }
    }

    // ---------- step contents ----------

    private static TextBlock StepTitle(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontSize = 23,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock StepSub(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = Theme.SubtleBrush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19,
        Margin = new Thickness(0, 0, 0, 22),
    };

    private UIElement BuildWelcome()
    {
        var panel = new StackPanel();
        var mark = new Image
        {
            Width = 56,
            Height = 56,
            Source = WindowFx.AppIcon,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 20),
        };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
        panel.Children.Add(mark);
        panel.Children.Add(StepTitle("Welcome to LiquidFlow"));
        panel.Children.Add(StepSub("Fast, private dictation for every Windows app. Speech never leaves this PC unless you choose a cloud AI provider. A few quick steps and you're dictating."));

        panel.Children.Add(Theme.Label("What should LiquidFlow call you?"));
        _nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(Settings.Current.DisplayName) ? FallbackFirstName() : Settings.Current.DisplayName,
            Width = 320,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(_nameBox);
        panel.Children.Add(Theme.Caption("Used for the Home page greeting — nothing else."));
        return panel;
    }

    private UIElement BuildHotkey()
    {
        var s = Settings.Current;
        var panel = new StackPanel();
        panel.Children.Add(StepTitle("Pick your dictation key"));
        panel.Children.Add(StepSub("Press it in any app to start dictating; press again to stop and type the result. You can change this any time in Settings."));

        var rec = new ShortcutRecorder(s.PrimaryDictationShortcuts.FirstOrDefault() ?? HotkeyShortcut.RightAlt());
        rec.ShortcutChanged += sc => { s.PrimaryDictationShortcuts = new List<HotkeyShortcut> { sc }; s.Save("hotkey"); };
        panel.Children.Add(rec);

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        void AddPreset(string label, Func<HotkeyShortcut> make)
        {
            var chip = new Button { Content = label, Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            chip.Click += (_, _) =>
            {
                var sc = make();
                s.PrimaryDictationShortcuts = new List<HotkeyShortcut> { sc };
                s.Save("hotkey");
                rec.SetShortcut(sc);
            };
            presets.Children.Add(chip);
        }
        AddPreset("Copilot key", HotkeyShortcut.CopilotKey);
        AddPreset("Right Alt", HotkeyShortcut.RightAlt);
        AddPreset("Right Ctrl", HotkeyShortcut.RightCtrl);
        panel.Children.Add(presets);

        panel.Children.Add(Theme.Label("How it activates"));
        var modeCombo = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var m in Enum.GetValues<HotkeyActivationMode>()) modeCombo.Items.Add(m.ToString());
        modeCombo.SelectedItem = s.HotkeyMode.ToString();
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (Enum.TryParse<HotkeyActivationMode>((string)modeCombo.SelectedItem, out var m)) { s.HotkeyMode = m; s.Save("hotkey"); }
        };
        panel.Children.Add(modeCombo);
        panel.Children.Add(Theme.Caption("Toggle: tap to start/stop. Hold: records while pressed (push-to-talk). Automatic supports both."));
        return panel;
    }

    private UIElement BuildModel()
    {
        var panel = new StackPanel();
        panel.Children.Add(StepTitle("Choose a speech model"));
        panel.Children.Add(StepSub("Everything runs locally. Parakeet is the fastest for English; Whisper models cover 99 languages. You can install more later in Settings → Speech Models."));

        // the wizard offers the sensible primary choices; the full catalog lives in Settings
        foreach (var id in new[] { SpeechModels.ParakeetModelId, "whisper-base", "whisper-small" })
        {
            var model = SpeechModels.ById(id);
            if (model is not null) panel.Children.Add(ModelRow(model, panel));
        }
        return panel;
    }

    private Border ModelRow(SpeechModelInfo model, StackPanel host)
    {
        bool selected = Settings.Current.SelectedSpeechModel == model.Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var radio = new Ellipse
        {
            Width = 18,
            Height = 18,
            StrokeThickness = 2,
            Stroke = selected ? Theme.GreenBrush : new SolidColorBrush(Theme.CardBorder),
            Fill = selected ? Theme.GreenBrush : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        grid.Children.Add(radio);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = model.DisplayName,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (model.Badge is not null)
        {
            var badge = Theme.Pill(model.Badge, Theme.GreenSoftBrush, Theme.GreenBrush, 10.5);
            badge.Margin = new Thickness(8, 0, 0, 0);
            header.Children.Add(badge);
        }
        text.Children.Add(header);
        text.Children.Add(new TextBlock
        {
            Text = $"{model.Tagline}  ·  {model.SizeDisplay}  ·  {model.LanguageSupport}",
            FontSize = 12,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 3, 0, 0),
        });
        var bar = new ProgressBar { Height = 4, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        text.Children.Add(bar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var state = new TextBlock
        {
            Text = model.IsDownloaded ? "Installed" : $"Downloads on select",
            FontSize = 11.5,
            Foreground = model.IsDownloaded ? Theme.GreenBrush : Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 2, 0),
        };
        Grid.SetColumn(state, 2);
        grid.Children.Add(state);

        var row = new Border
        {
            Background = selected ? Theme.GreenSoftBrush : new SolidColorBrush(Theme.Card),
            BorderBrush = selected ? Theme.GreenBrush : new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            Settings.Current.SelectedSpeechModel = model.Id;
            Settings.Current.Save("model");
            if (!model.IsDownloaded)
            {
                bar.Visibility = Visibility.Visible;
                state.Text = "Downloading…";
                _downloadCts?.Cancel();
                _downloadCts = new CancellationTokenSource();
                var ct = _downloadCts.Token;
                var progress = new Progress<ModelPreparationProgress>(p => Dispatcher.BeginInvoke(() =>
                {
                    if (p.Phase == ModelPreparationPhase.Downloading)
                    {
                        bar.Value = p.Fraction * 100;
                        state.Text = $"{(int)(p.Fraction * 100)}%";
                    }
                }));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ModelDownloader.DownloadModelAsync(model, progress, ct);
                        await Dispatcher.BeginInvoke(() => { state.Text = "Installed"; state.Foreground = Theme.GreenBrush; bar.Visibility = Visibility.Collapsed; });
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("onboarding", $"Model download failed: {ex.Message}");
                        await Dispatcher.BeginInvoke(() => { state.Text = "Download failed — retries on first use"; bar.Visibility = Visibility.Collapsed; });
                    }
                });
            }
            // re-render the list so the radio selection moves
            var parent = (StackPanel)_stepHost.Content!;
            if (ReferenceEquals(parent, host)) GoTo(2);
        };
        return row;
    }

    private UIElement BuildAi()
    {
        var s = Settings.Current;
        var panel = new StackPanel();
        panel.Children.Add(StepTitle("AI cleanup (optional)"));
        panel.Children.Add(StepSub("A small local model fixes grammar and punctuation, applies self-corrections like “actually I meant…”, and formats spoken lists — without changing your words. It runs entirely on this PC. Skip it and dictation still works great."));

        var card = new StackPanel();
        card.Children.Add(Theme.Toggle("Enable local AI cleanup", s.SelectedProviderID == Ai.ProviderCatalog.FluidLocalId, v =>
        {
            s.SelectedProviderID = v ? Ai.ProviderCatalog.FluidLocalId : "";
            s.Save("ai");
        }));
        card.Children.Add(Theme.Caption("The model (~1 GB) downloads the first time it's used. Cloud providers (OpenAI, etc.) can be configured later in Settings → AI Enhancement."));
        panel.Children.Add(Theme.Card2(card));
        return panel;
    }

    private UIElement BuildFinish()
    {
        var panel = new StackPanel();
        panel.Children.Add(StepTitle("You're all set"));
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "your hotkey";
        panel.Children.Add(StepSub($"Press {hotkey} in any app, speak naturally, and LiquidFlow types the result where your cursor is."));

        var how = new StackPanel();
        void Step(int n, string title, string sub)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Theme.GreenSoftBrush,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = n.ToString(),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.GreenBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            var col = new StackPanel();
            col.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
            col.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(col);
            how.Children.Add(row);
        }
        Step(1, "Start recording", $"Press {hotkey} in any app");
        Step(2, "Speak naturally", "Whispering works too — quiet audio is boosted automatically");
        Step(3, "It types itself", "The transcript lands in your focused app; history keeps every take");
        panel.Children.Add(Theme.Card2(how));

        panel.Children.Add(Theme.Caption("Tip: the Home page has a “Test now” button to try a 5-second dictation safely."));
        return panel;
    }

    private static string FallbackFirstName()
    {
        var name = Environment.UserName;
        if (string.IsNullOrWhiteSpace(name)) return "";
        name = name.Split(' ', '.', '_', '-')[0];
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
