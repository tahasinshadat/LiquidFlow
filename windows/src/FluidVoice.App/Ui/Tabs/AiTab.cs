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

        Children.Add(Theme.Heading("AI enhancement"));
        Children.Add(Theme.Caption("Optional. Cleans up dictation (formatting, capitalization, corrections) using a cloud provider or the local Fluid Local AI. Nothing leaves your PC unless you enable a cloud provider."));

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
            cfg.Children.Add(Theme.Caption("Stored securely in Windows Credential Manager. Never leaves your PC except to this provider."));
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
            statusText.Text = "Verifying…";
            try
            {
                var models = await LlmClient.ListModelsAsync(provider.Id, CancellationToken.None);
                s.AvailableModelsByProvider[provider.Id] = models;
                ProviderCatalog.MarkVerified(provider.Id);
                s.Save("ai");
                PopulateModels(models);
                statusText.Text = $"✓ Verified — {models.Count} models available";
                statusText.Foreground = Theme.AccentBrush;
            }
            catch (Exception ex)
            {
                statusText.Text = $"✗ {ex.Message}";
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
        panel.Children.Add(Theme.Heading("Fluid Local AI (open substitute)"));
        panel.Children.Add(Theme.Caption("Runs a small instruct model locally via llama.cpp (ARM64-native). This is the open replacement for the proprietary Fluid Intelligence runtime — same on-device enhancement, no data leaves your PC."));

        panel.Children.Add(Theme.Label("Model"));
        var modelCombo = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var m in LocalAiServer.Models) modelCombo.Items.Add(m.DisplayName);
        modelCombo.SelectedIndex = Math.Max(0, LocalAiServer.Models.ToList().FindIndex(m => m.Id == Settings.Current.LocalAiModelId));
        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedIndex >= 0)
            {
                Settings.Current.LocalAiModelId = LocalAiServer.Models[modelCombo.SelectedIndex].Id;
                Settings.Current.Save("localai");
            }
        };
        panel.Children.Add(modelCombo);

        var status = new TextBlock { Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
        status.Text = LocalAiServer.IsRuntimeInstalled() && LocalAiServer.IsModelInstalled()
            ? "✓ Installed and ready"
            : "Not set up yet — downloads the llama.cpp runtime + model (~1 GB).";
        var bar = new ProgressBar { Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 6) };

        var setupBtn = new Button { Content = "Download & set up", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        setupBtn.Click += async (_, _) =>
        {
            setupBtn.IsEnabled = false;
            bar.Visibility = Visibility.Visible;
            _localAiCts = new CancellationTokenSource();
            var progress = new Progress<ModelPreparationProgress>(p => Dispatcher.BeginInvoke(() =>
            {
                if (p.Phase == ModelPreparationPhase.Downloading) { bar.Value = p.Fraction * 100; status.Text = $"Downloading {(int)(p.Fraction * 100)}%…"; }
            }));
            try
            {
                await LocalAiServer.EnsureInstalledAsync(progress, _localAiCts.Token);
                ProviderCatalog.MarkVerified(ProviderCatalog.FluidLocalId);
                Settings.Current.Save("localai");
                status.Text = "✓ Installed and ready";
            }
            catch (Exception ex)
            {
                status.Text = $"✗ {ex.Message}";
                setupBtn.IsEnabled = true;
            }
            finally { bar.Visibility = Visibility.Collapsed; }
        };
        panel.Children.Add(setupBtn);
        panel.Children.Add(bar);
        panel.Children.Add(status);
        return Theme.Card2(panel);
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
