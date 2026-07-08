using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Stt;

namespace FluidVoice.Ui;

/// <summary>Speech model catalog: pick, download, delete, and choose language.</summary>
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
        Children.Add(BuildHeaderCard());
        foreach (var model in SpeechModels.All)
            Children.Add(ModelCard(model));
    }

    private UIElement BuildHeaderCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        // (the hosting page/modal supplies the section title — no duplicate heading here)
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = "Choose the local engine that turns speech into text. Parakeet is fastest for English; Whisper covers more languages.",
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 18, 0),
        });
        Grid.SetColumn(copy, 0);
        grid.Children.Add(copy);

        var language = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        language.Children.Add(Theme.Label("Whisper language"));
        var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var langs = new (string Code, string Name)[]
        {
            ("auto", "Auto-detect"), ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"),
            ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("ru", "Russian"), ("zh", "Chinese"),
            ("ja", "Japanese"), ("ko", "Korean"), ("hi", "Hindi"), ("ar", "Arabic"),
        };
        foreach (var (code, name) in langs)
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(langs, l => l.Code == Settings.Current.WhisperLanguage));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string code)
            {
                Settings.Current.WhisperLanguage = code;
                Settings.Current.Save("language");
            }
        };
        language.Children.Add(combo);
        Grid.SetColumn(language, 1);
        grid.Children.Add(language);

        return Theme.Panel(grid, new Thickness(24), new Thickness(0, 0, 0, 18));
    }

    private Border ModelCard(SpeechModelInfo model)
    {
        var selected = Settings.Current.SelectedSpeechModel == model.Id;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

        var left = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock
        {
            Text = model.DisplayName,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (model.Badge is not null)
        {
            var badge = Theme.Pill(model.Badge, selected ? Theme.GreenBrush : new SolidColorBrush(Theme.SidebarSelected), selected ? Brushes.White : Theme.TextBrush);
            badge.Margin = new Thickness(10, 0, 0, 0);
            header.Children.Add(badge);
        }
        if (selected)
        {
            var selectedBadge = Theme.Pill("Selected", Theme.GreenBrush, Brushes.White);
            selectedBadge.Margin = new Thickness(10, 0, 0, 0);
            header.Children.Add(selectedBadge);
        }
        left.Children.Add(header);
        left.Children.Add(new TextBlock
        {
            Text = model.Tagline,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        left.Children.Add(new TextBlock
        {
            Text = model.Description,
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        chips.Children.Add(ModelChip(model.Engine.ToString()));
        chips.Children.Add(ModelChip(model.LanguageSupport));
        chips.Children.Add(ModelChip(model.SizeDisplay));
        if (model.RamEstimate.Length > 0) chips.Children.Add(ModelChip(model.RamEstimate));
        if (model.SupportsLivePreview || model.Engine == SpeechEngineKind.Parakeet)
            chips.Children.Add(ModelChip("Live preview"));
        left.Children.Add(chips);

        var quality = new Grid();
        quality.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        quality.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        quality.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var speed = QualityBar("Speed", model.SpeedPercent);
        Grid.SetColumn(speed, 0);
        quality.Children.Add(speed);
        var accuracy = QualityBar("Accuracy", model.AccuracyPercent);
        Grid.SetColumn(accuracy, 2);
        quality.Children.Add(accuracy);
        left.Children.Add(quality);

        var bar = new ProgressBar { Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) };
        _bars[model.Id] = bar;
        left.Children.Add(bar);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var action = Theme.PrimaryButton("");
        action.Width = 160;
        action.HorizontalAlignment = HorizontalAlignment.Right;
        _buttons[model.Id] = action;
        UpdateButton(model, action, selected);
        action.Click += async (_, _) => await OnAction(model);
        buttons.Children.Add(action);

        if (model.IsDownloaded)
        {
            var del = Theme.SecondaryButton("Uninstall");
            del.Width = 160;
            del.Margin = new Thickness(0, 10, 0, 0);
            del.HorizontalAlignment = HorizontalAlignment.Right;
            del.Click += (_, _) => Uninstall(model, selected);
            buttons.Children.Add(del);
        }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var card = Theme.Panel(grid, new Thickness(22), new Thickness(0, 0, 0, 14));
        if (selected)
        {
            card.Background = new SolidColorBrush(Theme.GreenSoft);
            card.BorderBrush = Theme.GreenBrush;
        }
        return card;
    }

    private static Border ModelChip(string text)
    {
        var chip = Theme.Pill(text, new SolidColorBrush(Theme.SidebarSelected), Theme.TextBrush, 11.5);
        chip.Margin = new Thickness(0, 0, 8, 0);
        return chip;
    }

    private static UIElement QualityBar(string label, double value)
    {
        var panel = new StackPanel();
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        var percent = new TextBlock
        {
            Text = $"{value * 100:0}%",
            FontSize = 12,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(percent, Dock.Right);
        row.Children.Add(percent);
        panel.Children.Add(row);
        panel.Children.Add(new ProgressBar { Value = value * 100, Height = 5 });
        return panel;
    }

    private void UpdateButton(SpeechModelInfo model, Button btn, bool selected)
    {
        btn.Content = selected ? "Selected" : model.IsDownloaded ? "Use this model" : $"Download {model.SizeDisplay}";
        btn.IsEnabled = !selected;
    }

    private void Uninstall(SpeechModelInfo model, bool selected)
    {
        var note = selected
            ? $"Uninstall {model.DisplayName}?\n\nIt is your selected model. LiquidFlow will switch back to {SpeechModels.ById(SpeechModels.DefaultModelId)!.DisplayName}."
            : $"Uninstall {model.DisplayName} and free {model.SizeDisplay}?";
        if (MessageBox.Show(note, "LiquidFlow", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            if (Directory.Exists(model.LocalPath)) Directory.Delete(model.LocalPath, recursive: true);
            else if (File.Exists(model.LocalPath)) File.Delete(model.LocalPath);
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
        btn.Content = "Downloading";
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<ModelPreparationProgress>(p =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (p.Phase == ModelPreparationPhase.Downloading)
                {
                    bar.Value = p.Fraction * 100;
                    btn.Content = $"Downloading {(int)(p.Fraction * 100)}%";
                }
                else if (p.Phase == ModelPreparationPhase.Failed)
                {
                    btn.Content = "Retry download";
                }
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
            btn.Content = "Retry download";
            btn.IsEnabled = true;
            bar.Visibility = Visibility.Collapsed;
        }
    }
}
