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

        Children.Add(new TextBlock
        {
            Text = "AI enhancement",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 20, 0, 8),
        });
        Children.Add(new TextBlock
        {
            Text = "Choose how FluidVoice cleans up capitalization, phrasing, and corrections after speech recognition.",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        // enable + provider select
        var enablePanel = new StackPanel();
        var enabled = !string.IsNullOrEmpty(s.SelectedProviderID);
        enablePanel.Children.Add(Theme.Toggle("Enable AI enhancement for dictation", enabled, v =>
        {
            if (!v) { s.SelectedProviderID = ""; s.Save("ai"); Dispatcher.BeginInvoke(Build); }
        }));

        enablePanel.Children.Add(Theme.Label("Provider"));
        var providerCombo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
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
        Children.Add(Theme.Card2(enablePanel));

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
        cfg.Children.Add(Theme.Heading($"{provider.Name} configuration"));

        if (!provider.IsLocal)
        {
            cfg.Children.Add(Theme.Label("API key"));
            var keyBox = new PasswordBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
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

        var statusText = new TextBlock { Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 4, 0, 6) };
        var verifyBtn = new Button { Content = "Verify & load models", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        var modelCombo = new ComboBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };

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
        cfg.Children.Add(verifyBtn);
        cfg.Children.Add(statusText);
        cfg.Children.Add(Theme.Label("Model"));
        cfg.Children.Add(modelCombo);
        Children.Add(Theme.Card2(cfg));

        Children.Add(StreamingCard());
    }

    private Border LocalAiCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Heading("Fluid Local AI"));
        panel.Children.Add(Theme.Caption("Runs a small local model for private cleanup. No dictation text leaves your PC when this provider is selected."));

        foreach (var m in LocalAiServer.Models)
            panel.Children.Add(LocalModelRow(m));

        if (LocalAiServer.IsRuntimeInstalled())
            panel.Children.Add(Theme.Caption("llama.cpp runtime installed. The selected model starts on demand and stops when FluidVoice quits."));
        return Theme.Card2(panel);
    }

    private UIElement LocalModelRow(LocalAiModel m)
    {
        var installed = LocalAiServer.IsModelInstalled(m);
        var selected = Settings.Current.LocalAiModelId == m.Id;

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = m.DisplayName + (selected ? " - selected" : ""),
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Theme.TextBrush, FontSize = 13,
        });
        text.Children.Add(new TextBlock { Text = m.Description, Foreground = Theme.SubtleBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var bar = new ProgressBar { Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        text.Children.Add(bar);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!installed)
        {
            var dl = new Button { Content = "Download", Padding = new Thickness(11, 5, 11, 5) };
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
                Dispatcher.BeginInvoke(Build);
            };
            buttons.Children.Add(dl);
        }
        else
        {
            if (!selected)
            {
                var use = new Button { Content = "Use", Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 6, 0) };
                use.Click += (_, _) =>
                {
                    LocalAiServer.Stop(); // restart on demand with the new model
                    Settings.Current.LocalAiModelId = m.Id;
                    Settings.Current.Save("localai");
                    Dispatcher.BeginInvoke(Build);
                };
                buttons.Children.Add(use);
            }
            var del = new Button { Content = "Uninstall", Padding = new Thickness(11, 5, 11, 5) };
            del.Click += (_, _) =>
            {
                var gb = LocalAiServer.InstalledBytes(m) / 1024.0 / 1024 / 1024;
                if (MessageBox.Show($"Uninstall {m.DisplayName} and free {gb:0.0} GB?", "FluidVoice",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                LocalAiServer.DeleteModel(m);
                Dispatcher.BeginInvoke(Build);
            };
            buttons.Children.Add(del);
        }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private Border StreamingCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Heading("Options"));
        panel.Children.Add(Theme.Toggle("Stream AI responses", Settings.Current.EnableAIStreaming, v => { Settings.Current.EnableAIStreaming = v; Settings.Current.Save("ai"); }));
        panel.Children.Add(Theme.Toggle("Notify me if AI enhancement fails", Settings.Current.NotifyAIProcessingFailures, v => { Settings.Current.NotifyAIProcessingFailures = v; Settings.Current.Save("ai"); }));
        return Theme.Card2(panel);
    }
}
