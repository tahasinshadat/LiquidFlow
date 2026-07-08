using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Ai;
using FluidVoice.Core;
using FluidVoice.Stt;

namespace FluidVoice.Ui;

/// <summary>AI enhancement: enable/disable, choose provider, enter key, verify, pick model, set up local AI.</summary>
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

        // (the hosting page/modal supplies the section title — no duplicate heading here)
        Children.Add(new TextBlock
        {
            Text = "Choose how LiquidFlow cleans up capitalization, phrasing, and corrections after speech recognition.",
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
                // default to the private on-device provider so the toggle visibly does something
                s.SelectedProviderID = ProviderCatalog.FluidLocalId;
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

        enablePanel.Children.Add(Theme.Label("Provider"));
        var providerCombo = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        var providers = ProviderCatalog.All().ToList();
        foreach (var p in providers) providerCombo.Items.Add(p.Name);
        var current = providers.FindIndex(p => p.Id == s.SelectedProviderID);
        providerCombo.SelectedIndex = current;
        providerCombo.SelectionChanged += (_, _) =>
        {
            if (providerCombo.SelectedIndex >= 0)
            {
                s.SelectedProviderID = providers[providerCombo.SelectedIndex].Id;
                s.Save("ai");
                Dispatcher.BeginInvoke(Build);
            }
        };
        enablePanel.Children.Add(providerCombo);
        Children.Add(Theme.Panel(enablePanel, new Thickness(22), new Thickness(0, 0, 0, 16)));

        if (string.IsNullOrEmpty(s.SelectedProviderID)) return;

        var provider = ProviderCatalog.ById(s.SelectedProviderID);
        if (provider is null) return;

        if (provider.Id == ProviderCatalog.FluidLocalId)
        {
            Children.Add(LocalAiCard());
            return;
        }

        // cloud/custom provider config
        var cfg = new StackPanel();
        cfg.Children.Add(Theme.Heading($"{provider.Name} model"));

        if (!provider.IsLocal)
        {
            cfg.Children.Add(Theme.Label("API key"));
            var keyBox = new PasswordBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            var existing = CredentialStore.GetApiKey(provider.Id);
            if (!string.IsNullOrEmpty(existing)) keyBox.Password = existing;
            keyBox.PasswordChanged += (_, _) => CredentialStore.SetApiKey(provider.Id, keyBox.Password);
            cfg.Children.Add(keyBox);
            cfg.Children.Add(Theme.Caption("Stored in Windows Credential Manager and only sent to the selected provider."));
        }
        else
        {
            cfg.Children.Add(Theme.Caption($"Local endpoint: {provider.BaseUrl}. Make sure the server is running."));
        }

        var statusText = new TextBlock { Foreground = Theme.SubtleBrush, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        var verifyBtn = Theme.SecondaryButton("Refresh models");
        var modelCombo = new ComboBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left };

        void PopulateModels(List<string> models)
        {
            modelCombo.Items.Clear();
            foreach (var m in models) modelCombo.Items.Add(m);
            var sel = ProviderCatalog.SelectedModelFor(provider.Id);
            if (sel is not null && models.Contains(sel)) modelCombo.SelectedItem = sel;
            else if (models.Count > 0) modelCombo.SelectedIndex = 0;
        }
        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedItem is string m)
            {
                s.SelectedModelByProvider[provider.Id] = m;
                s.Save("ai");
            }
        };
        if (s.AvailableModelsByProvider.TryGetValue(provider.Id, out var cached)) PopulateModels(cached);

        verifyBtn.Click += async (_, _) =>
        {
            verifyBtn.IsEnabled = false;
            statusText.Text = "Verifying...";
            try
            {
                var models = await LlmClient.ListModelsAsync(provider.Id, CancellationToken.None);
                s.AvailableModelsByProvider[provider.Id] = models;
                ProviderCatalog.MarkVerified(provider.Id);
                s.Save("ai");
                PopulateModels(models);
                statusText.Text = $"Verified - {models.Count} models available";
                statusText.Foreground = Theme.AccentBrush;
            }
            catch (Exception ex)
            {
                statusText.Text = ex.Message;
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 90, 90));
            }
            finally { verifyBtn.IsEnabled = true; }
        };
        var selector = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selectorLeft = new StackPanel();
        selectorLeft.Children.Add(Theme.Label("Model"));
        selectorLeft.Children.Add(modelCombo);
        selectorLeft.Children.Add(statusText);
        Grid.SetColumn(selectorLeft, 0);
        selector.Children.Add(selectorLeft);
        verifyBtn.Margin = new Thickness(18, 24, 0, 0);
        verifyBtn.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(verifyBtn, 1);
        selector.Children.Add(verifyBtn);
        cfg.Children.Add(selector);
        Children.Add(Theme.Panel(cfg, new Thickness(22), new Thickness(0, 0, 0, 16)));

        Children.Add(StreamingCard());
    }

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
        var installed = LocalAiServer.IsModelInstalled(m);
        var selected = Settings.Current.LocalAiModelId == m.Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = m.DisplayName + (selected ? " - selected" : ""),
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
                    LocalAiServer.Stop(); // restart on demand with the new model
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
            CornerRadius = new CornerRadius(8),
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
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 16));
    }
}
