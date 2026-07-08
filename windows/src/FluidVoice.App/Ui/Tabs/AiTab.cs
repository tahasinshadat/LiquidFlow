using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Ai;
using FluidVoice.Core;
using FluidVoice.Stt;

namespace FluidVoice.Ui;

/// <summary>
/// AI enhancement: enable/disable, pick a provider (grouped by company with brand tiles),
/// enter key + verify, choose a model, edit the cleanup prompt, and set up local AI.
/// </summary>
public sealed class AiTab : StackPanel
{
    private CancellationTokenSource? _localAiCts;

    public AiTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        var s = Settings.Current;

        Children.Add(new TextBlock
        {
            Text = "Clean up dictation with AI — fixes grammar and punctuation, applies spoken self-corrections, and formats lists, without changing your words.",
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var enablePanel = new StackPanel();
        var enabled = !string.IsNullOrEmpty(s.SelectedProviderID);
        enablePanel.Children.Add(Theme.Toggle("Enable AI enhancement for dictation", enabled, v =>
        {
            if (v && string.IsNullOrEmpty(s.SelectedProviderID))
            {
                s.SelectedProviderID = ProviderCatalog.FluidLocalId; // default to private on-device
                s.Save("ai");
                Dispatcher.BeginInvoke(Build);
            }
            else if (!v && !string.IsNullOrEmpty(s.SelectedProviderID))
            {
                s.SelectedProviderID = "";
                s.Save("ai");
                Dispatcher.BeginInvoke(Build);
            }
        }));
        enablePanel.Children.Add(Theme.Caption("On-device cleanup keeps everything private. Cloud providers are faster and stronger but send text to their API."));
        Children.Add(Theme.Card2(enablePanel));

        if (string.IsNullOrEmpty(s.SelectedProviderID)) return;

        Children.Add(BuildProviderPicker(s.SelectedProviderID));

        var provider = ProviderCatalog.ById(s.SelectedProviderID);
        if (provider is null) return;

        if (provider.Id == ProviderCatalog.FluidLocalId)
            Children.Add(LocalAiCard());
        else if (!ProviderCatalog.NeedsApiKey(provider.Id))
            Children.Add(EndpointCard(provider));   // Ollama / LM Studio
        else
            Children.Add(CloudCard(provider));

        Children.Add(BuildPromptEditor());
        Children.Add(StreamingCard());
    }

    // ---------- provider picker (grouped brand tiles) ----------

    private UIElement BuildProviderPicker(string selectedId)
    {
        var host = new StackPanel();
        host.Children.Add(Theme.Heading("Provider"));

        foreach (var groupName in new[] { "On this PC", "Cloud providers" })
        {
            var inGroup = ProviderCatalog.All().Where(p => ProviderCatalog.Group(p.Id) == groupName).ToList();
            if (inGroup.Count == 0) continue;
            var eyebrow = Theme.Eyebrow(groupName);
            eyebrow.Margin = new Thickness(0, 4, 0, 8);
            host.Children.Add(eyebrow);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var p in inGroup) wrap.Children.Add(ProviderTile(p, p.Id == selectedId));
            host.Children.Add(wrap);
        }
        return Theme.Panel(host, new Thickness(22), new Thickness(0, 0, 0, 16));
    }

    private UIElement ProviderTile(ProviderInfo p, bool selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(ProviderIcon.For(p.Id, p.Name, 26));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        if (ProviderCatalog.IsConfigured(p.Id))
            text.Children.Add(new TextBlock { Text = "Ready", FontSize = 10.5, Foreground = Theme.GreenBrush });
        row.Children.Add(text);

        var tile = new Border
        {
            Child = row,
            Width = 188,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(11),
            Background = selected ? Theme.GreenSoftBrush : new SolidColorBrush(Theme.CardInner),
            BorderBrush = selected ? Theme.GreenBrush : new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(selected ? 1.5 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tile.MouseLeftButtonDown += (_, e) => e.Handled = true;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            Settings.Current.SelectedProviderID = p.Id;
            Settings.Current.Save("ai");
            Dispatcher.BeginInvoke(Build);
        };
        return tile;
    }

    // ---------- cloud provider config ----------

    private UIElement CloudCard(ProviderInfo provider)
    {
        var cfg = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(ProviderIcon.For(provider.Id, provider.Name, 30));
        head.Children.Add(new TextBlock { Text = provider.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        cfg.Children.Add(head);

        cfg.Children.Add(Theme.Label("API key"));
        var keyBox = new PasswordBox { Width = 440, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        var existing = CredentialStore.GetApiKey(provider.Id);
        if (!string.IsNullOrEmpty(existing)) keyBox.Password = existing;
        keyBox.PasswordChanged += (_, _) => CredentialStore.SetApiKey(provider.Id, keyBox.Password);
        cfg.Children.Add(keyBox);
        cfg.Children.Add(Theme.Caption("Stored in Windows Credential Manager and only sent to this provider."));

        BuildModelSelector(cfg, provider);
        return Theme.Panel(cfg, new Thickness(22), new Thickness(0, 0, 0, 16));
    }

    private UIElement EndpointCard(ProviderInfo provider)
    {
        var cfg = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(ProviderIcon.For(provider.Id, provider.Name, 30));
        head.Children.Add(new TextBlock { Text = provider.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        cfg.Children.Add(head);
        cfg.Children.Add(Theme.Caption($"Local endpoint: {provider.BaseUrl}. Start the server, then refresh to load its installed models."));
        BuildModelSelector(cfg, provider);
        return Theme.Panel(cfg, new Thickness(22), new Thickness(0, 0, 0, 16));
    }

    /// <summary>Model dropdown (curated defaults ∪ live-refreshed) + Verify/Refresh, shared by cloud + local-endpoint cards.</summary>
    private void BuildModelSelector(StackPanel cfg, ProviderInfo provider)
    {
        var s = Settings.Current;
        var statusText = new TextBlock { Foreground = Theme.SubtleBrush, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        var verifyBtn = Theme.SecondaryButton("Verify & refresh");
        var modelCombo = new ComboBox { Width = 440, HorizontalAlignment = HorizontalAlignment.Left };

        void Populate(IReadOnlyList<string> models)
        {
            var merged = ProviderCatalog.CuratedModels(provider.Id).Concat(models)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            modelCombo.Items.Clear();
            foreach (var m in merged) modelCombo.Items.Add(m);
            var sel = ProviderCatalog.SelectedModelFor(provider.Id);
            if (sel is not null && merged.Contains(sel, StringComparer.OrdinalIgnoreCase))
                modelCombo.SelectedItem = merged.First(m => m.Equals(sel, StringComparison.OrdinalIgnoreCase));
            else if (merged.Count > 0) modelCombo.SelectedIndex = 0;
        }
        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedItem is string m) { s.SelectedModelByProvider[provider.Id] = m; s.Save("ai"); }
        };
        Populate(s.AvailableModelsByProvider.TryGetValue(provider.Id, out var cached) ? cached : Array.Empty<string>());

        verifyBtn.Click += async (_, _) =>
        {
            verifyBtn.IsEnabled = false;
            statusText.Text = "Verifying…";
            statusText.Foreground = Theme.SubtleBrush;
            try
            {
                var models = await LlmClient.ListModelsAsync(provider.Id, CancellationToken.None);
                s.AvailableModelsByProvider[provider.Id] = models;
                ProviderCatalog.MarkVerified(provider.Id);
                s.Save("ai");
                Populate(models);
                statusText.Text = $"Verified — {models.Count} models available.";
                statusText.Foreground = Theme.AccentBrush;
            }
            catch (Exception ex)
            {
                statusText.Text = ex.Message;
                statusText.Foreground = new SolidColorBrush(Theme.Danger);
            }
            finally { verifyBtn.IsEnabled = true; }
        };

        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Theme.Label("Model"));
        left.Children.Add(modelCombo);
        left.Children.Add(statusText);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        verifyBtn.Margin = new Thickness(18, 24, 0, 0);
        verifyBtn.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(verifyBtn, 1);
        grid.Children.Add(verifyBtn);
        cfg.Children.Add(grid);
    }

    // ---------- prompt editor ----------

    private UIElement BuildPromptEditor()
    {
        var panel = new StackPanel();
        var headRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };
        var heading = Theme.Heading("Cleanup prompt");
        heading.Margin = new Thickness(0, 4, 0, 0);
        headRow.Children.Add(heading);
        if (PromptStore.HasOverride(PromptMode.Dictate))
        {
            var badge = Theme.Pill("Customized", Theme.PurpleBrush, Brushes.White, 10.5);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Margin = new Thickness(10, 4, 0, 0);
            headRow.Children.Add(badge);
        }
        panel.Children.Add(headRow);
        panel.Children.Add(Theme.Caption("Instructions the AI follows when cleaning your dictation. Edit to change its behavior; the transcript is appended automatically."));

        var editor = new TextBox
        {
            Text = PromptStore.EffectiveBody(PromptMode.Dictate),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 150,
            MaxHeight = 320,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
            FontSize = 12.5,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 10),
        };
        panel.Children.Add(editor);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var save = Theme.PrimaryButton("Save prompt");
        var status = new TextBlock { Foreground = Theme.SubtleBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        save.Click += (_, _) =>
        {
            PromptStore.SetOverride(PromptMode.Dictate, editor.Text);
            status.Text = PromptStore.HasOverride(PromptMode.Dictate) ? "Saved your custom prompt." : "Matches the default — using the built-in prompt.";
            status.Foreground = Theme.AccentBrush;
            Dispatcher.BeginInvoke(Build); // refresh the Customized badge
        };
        var reset = Theme.SecondaryButton("Reset to default");
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += (_, _) =>
        {
            PromptStore.SetOverride(PromptMode.Dictate, null);
            editor.Text = PromptStore.BuiltInBody(PromptMode.Dictate);
            status.Text = "Reset to the built-in prompt.";
            status.Foreground = Theme.SubtleBrush;
            Dispatcher.BeginInvoke(Build);
        };
        actions.Children.Add(save);
        actions.Children.Add(reset);
        actions.Children.Add(status);
        panel.Children.Add(actions);
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 16));
    }

    // ---------- local (llama.cpp) provider ----------

    private Border LocalAiCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Heading("LiquidFlow Local AI"));
        panel.Children.Add(Theme.Caption("Runs a small local model for private cleanup. No dictation text leaves your PC when this provider is selected."));

        foreach (var m in LocalAiServer.Models)
            panel.Children.Add(LocalModelRow(m));

        if (LocalAiServer.IsRuntimeInstalled())
            panel.Children.Add(Theme.Caption("llama.cpp runtime installed. The selected model starts on demand and stops when LiquidFlow quits."));
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 16));
    }

    private UIElement LocalModelRow(LocalAiModel m)
    {
        var selected = Settings.Current.LocalAiModelId == m.Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = m.DisplayName + (selected ? " — selected" : ""),
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Theme.TextBrush, FontSize = 14,
        });
        text.Children.Add(new TextBlock
        {
            Text = m.Description,
            Foreground = Theme.SubtleBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var installed = LocalAiServer.IsModelInstalled(m);
        status.Children.Add(Theme.Pill(installed ? "Installed" : "Not installed", installed ? Theme.GreenBrush : new SolidColorBrush(Theme.SidebarSelected), installed ? Brushes.White : Theme.TextBrush, 11));
        if (selected)
        {
            var selectedPill = Theme.Pill("Selected", Theme.PurpleBrush, Brushes.White, 11);
            selectedPill.Margin = new Thickness(8, 0, 0, 0);
            status.Children.Add(selectedPill);
        }
        text.Children.Add(status);
        var bar = new ProgressBar { Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        text.Children.Add(bar);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        if (!installed)
        {
            var dl = Theme.PrimaryButton("Download");
            dl.Click += async (_, _) =>
            {
                dl.IsEnabled = false;
                bar.Visibility = Visibility.Visible;
                Settings.Current.LocalAiModelId = m.Id;
                Settings.Current.Save("localai");
                _localAiCts = new CancellationTokenSource();
                var progress = new Progress<ModelPreparationProgress>(p => Dispatcher.BeginInvoke(() =>
                {
                    if (p.Phase == ModelPreparationPhase.Downloading) { bar.Value = p.Fraction * 100; dl.Content = $"{(int)(p.Fraction * 100)}%"; }
                }));
                try
                {
                    await LocalAiServer.EnsureInstalledAsync(progress, _localAiCts.Token);
                    ProviderCatalog.MarkVerified(ProviderCatalog.FluidLocalId);
                    Settings.Current.Save("localai");
                }
                catch (Exception ex) { Log.Warn("localai", ex.Message); }
                _ = Dispatcher.BeginInvoke(Build);
            };
            buttons.Children.Add(dl);
        }
        else
        {
            if (!selected)
            {
                var use = Theme.PrimaryButton("Use");
                use.Margin = new Thickness(0, 0, 8, 0);
                use.Click += (_, _) =>
                {
                    LocalAiServer.Stop();
                    Settings.Current.LocalAiModelId = m.Id;
                    Settings.Current.Save("localai");
                    Dispatcher.BeginInvoke(Build);
                };
                buttons.Children.Add(use);
            }
            var del = Theme.SecondaryButton("Uninstall");
            del.Foreground = new SolidColorBrush(Theme.Danger);
            del.Click += (_, _) =>
            {
                var gb = LocalAiServer.InstalledBytes(m) / 1024.0 / 1024 / 1024;
                if (MessageBox.Show($"Uninstall {m.DisplayName} and free {gb:0.0} GB?", "LiquidFlow",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                LocalAiServer.DeleteModel(m);
                Dispatcher.BeginInvoke(Build);
            };
            buttons.Children.Add(del);
        }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 10, 0, 0),
            Child = grid,
        };
    }

    private Border StreamingCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Heading("Options"));
        panel.Children.Add(Theme.Toggle("Stream AI responses", Settings.Current.EnableAIStreaming, v => { Settings.Current.EnableAIStreaming = v; Settings.Current.Save("ai"); }));
        panel.Children.Add(Theme.Toggle("Notify me if AI enhancement fails", Settings.Current.NotifyAIProcessingFailures, v => { Settings.Current.NotifyAIProcessingFailures = v; Settings.Current.Save("ai"); }));
        panel.Children.Add(Theme.Toggle("Auto-learn corrections", Settings.Current.AutoLearnCorrections, v => { Settings.Current.AutoLearnCorrections = v; Settings.Current.Save("ai"); }));
        panel.Children.Add(Theme.Caption("Watches which names and terms AI cleanup fixes, and after a few repeats adds them to your dictionary so plain transcription gets them right too. Review them under Dictionary."));
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 16));
    }
}
