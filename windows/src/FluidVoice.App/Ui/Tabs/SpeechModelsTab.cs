using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Stt;

namespace FluidVoice.Ui;

/// <summary>Whisper model catalog: pick, download, delete, choose language (VoiceEngineSettingsView.swift).</summary>
public sealed class SpeechModelsTab : StackPanel
{
    private readonly Dictionary<string, ProgressBar> _bars = new();
    private readonly Dictionary<string, Button> _buttons = new();
    private CancellationTokenSource? _downloadCts;

    public SpeechModelsTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(Theme.Heading("Speech models"));
        Children.Add(Theme.Caption("All models run fully on-device, ARM64-native. Parakeet (NVIDIA, via sherpa-onnx) is English-only with true live streaming; Whisper (whisper.cpp) covers 99 languages. Larger = more accurate but slower."));

        // language selector
        var langPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        langPanel.Children.Add(new TextBlock { Text = "Language (Whisper): ", Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center });
        var langCombo = new ComboBox { Width = 160 };
        var langs = new (string Code, string Name)[]
        {
            ("auto", "Auto-detect"), ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"),
            ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("ru", "Russian"), ("zh", "Chinese"),
            ("ja", "Japanese"), ("ko", "Korean"), ("hi", "Hindi"), ("ar", "Arabic"),
        };
        foreach (var (code, name) in langs) langCombo.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        langCombo.SelectedIndex = Math.Max(0, Array.FindIndex(langs, l => l.Code == Settings.Current.WhisperLanguage));
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is ComboBoxItem it && it.Tag is string code)
            {
                Settings.Current.WhisperLanguage = code;
                Settings.Current.Save("language");
            }
        };
        langPanel.Children.Add(langCombo);
        Children.Add(langPanel);

        foreach (var model in SpeechModels.All)
            Children.Add(ModelCard(model));
    }

    private Border ModelCard(SpeechModelInfo model)
    {
        var selected = Settings.Current.SelectedSpeechModel == model.Id;
        var panel = new StackPanel();

        var header = new DockPanel();
        var title = new TextBlock
        {
            Text = $"{model.DisplayName} · {model.Tagline}",
            FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, FontSize = 14,
        };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        if (model.Badge is not null)
        {
            var badge = new Border
            {
                Background = Theme.AccentBrush, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock { Text = model.Badge, Foreground = Brushes.White, FontSize = 10 },
            };
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
        }
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Text = $"{model.Description}  ·  {model.SizeDisplay}  ·  speed {model.SpeedPercent * 100:0}%  accuracy {model.AccuracyPercent * 100:0}%",
            Foreground = Theme.SubtleBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6),
        });

        var bar = new ProgressBar { Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        _bars[model.Id] = bar;
        panel.Children.Add(bar);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var actionBtn = new Button { Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        _buttons[model.Id] = actionBtn;
        UpdateButton(model, actionBtn, selected);
        actionBtn.Click += async (_, _) => await OnAction(model);
        buttons.Children.Add(actionBtn);

        if (model.IsDownloaded)
        {
            var delBtn = new Button { Content = "Uninstall", Padding = new Thickness(12, 5, 12, 5) };
            delBtn.Click += (_, _) =>
            {
                var note = selected
                    ? $"Uninstall {model.DisplayName}?\n\nIt is your selected model — FluidVoice will switch back to {SpeechModels.ById(SpeechModels.DefaultModelId)!.DisplayName}."
                    : $"Uninstall {model.DisplayName} and free {model.SizeDisplay}?";
                if (MessageBox.Show(note, "FluidVoice", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                try
                {
                    if (Directory.Exists(model.LocalPath)) Directory.Delete(model.LocalPath, recursive: true);
                    else File.Delete(model.LocalPath);
                }
                catch (Exception ex)
                {
                    Log.Warn("models", $"Uninstall failed: {ex.Message}");
                }
                if (selected)
                {
                    Settings.Current.SelectedSpeechModel = SpeechModels.DefaultModelId;
                    Settings.Current.Save("model");
                }
                Build();
            };
            buttons.Children.Add(delBtn);
        }
        panel.Children.Add(buttons);

        var card = Theme.Card2(panel);
        if (selected) card.BorderBrush = Theme.AccentBrush;
        return card;
    }

    private void UpdateButton(SpeechModelInfo model, Button btn, bool selected)
    {
        btn.Content = selected ? "✓ Selected" : model.IsDownloaded ? "Use this model" : $"Download ({model.SizeDisplay})";
        btn.IsEnabled = !selected;
    }

    private async Task OnAction(SpeechModelInfo model)
    {
        if (Settings.Current.SelectedSpeechModel == model.Id) return;

        if (model.IsDownloaded)
        {
            Settings.Current.SelectedSpeechModel = model.Id;
            Settings.Current.Save("model");
            Build();
            return;
        }

        var bar = _bars[model.Id];
        var btn = _buttons[model.Id];
        bar.Visibility = Visibility.Visible;
        btn.IsEnabled = false;
        btn.Content = "Downloading…";
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<ModelPreparationProgress>(p =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (p.Phase == ModelPreparationPhase.Downloading) { bar.Value = p.Fraction * 100; btn.Content = $"Downloading {(int)(p.Fraction * 100)}%"; }
                else if (p.Phase == ModelPreparationPhase.Failed) btn.Content = "Failed — retry";
            });
        });
        try
        {
            await ModelDownloader.DownloadModelAsync(model, progress, _downloadCts.Token);
            Settings.Current.SelectedSpeechModel = model.Id;
            Settings.Current.Save("model");
            Build();
        }
        catch (Exception ex)
        {
            Log.Error("models", $"Download failed for {model.Id}", ex);
            btn.Content = "Failed — retry";
            btn.IsEnabled = true;
            bar.Visibility = Visibility.Collapsed;
        }
    }
}
