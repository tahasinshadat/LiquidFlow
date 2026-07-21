using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Input;

namespace FluidVoice.Ui;

/// <summary>Hotkey, activation mode, overlay size/position, theme (SettingsView general area).</summary>
public sealed class GeneralTab : StackPanel
{
    private ContentControl? _modeHost;
    private Action? _rebuildMode;
    private void RefreshModeControl() => _rebuildMode?.Invoke();

    public GeneralTab()
    {
        var s = Settings.Current;

        // --- Hotkeys ---
        var hk = new StackPanel();
        hk.Children.Add(Theme.Heading("Shortcuts"));

        hk.Children.Add(Theme.Label("Dictation"));
        var dictationRec = new ShortcutRecorder(s.PrimaryDictationShortcuts.FirstOrDefault() ?? HotkeyShortcut.RightAlt());
        dictationRec.ShortcutChanged += sc => { s.PrimaryDictationShortcuts = new List<HotkeyShortcut> { sc }; s.Save("hotkey"); };
        hk.Children.Add(dictationRec);
        hk.Children.Add(Theme.Caption("Use this shortcut anywhere in Windows to start or stop dictation."));

        // one-click presets: Copilot key first (Win+Shift+F23)
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        void AddPreset(string label, Func<HotkeyShortcut> make)
        {
            var chip = new Button { Content = label, Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            chip.Click += (_, _) =>
            {
                var sc = make();
                s.PrimaryDictationShortcuts = new List<HotkeyShortcut> { sc };
                s.Save("hotkey");
                dictationRec.SetShortcut(sc);
            };
            presets.Children.Add(chip);
        }
        AddPreset("Copilot key", HotkeyShortcut.CopilotKey);
        AddPreset("Right Alt", HotkeyShortcut.RightAlt);
        AddPreset("Right Ctrl", HotkeyShortcut.RightCtrl);
        hk.Children.Add(presets);
        hk.Children.Add(Theme.Caption("While LiquidFlow is running, the Copilot key starts dictation instead of opening Copilot."));

        hk.Children.Add(Theme.Label("Activation mode"));
        var modes = Enum.GetValues<HotkeyActivationMode>().ToList();
        UIElement BuildModeControl() => Theme.Segmented(
            modes.Select(m => m.ToString()).ToList(),
            modes.IndexOf(s.HotkeyMode),
            i => { s.HotkeyMode = modes[i]; s.Save("hotkey"); RefreshModeControl(); },
            maxWidth: 340);
        _modeHost = new ContentControl { Content = BuildModeControl(), HorizontalAlignment = HorizontalAlignment.Left };
        _rebuildMode = () => _modeHost.Content = BuildModeControl();
        hk.Children.Add(_modeHost);
        hk.Children.Add(Theme.Caption("Toggle taps on and off. Hold records while pressed. Automatic supports both."));

        hk.Children.Add(WithEnableToggle("Edit / Write mode", s.RewriteModeShortcut, s.RewriteModeShortcutEnabled,
            sc => { s.RewriteModeShortcut = sc; s.Save("hotkey"); },
            on => { s.RewriteModeShortcutEnabled = on; s.Save("hotkey"); }));

        hk.Children.Add(WithEnableToggle("Command mode", s.CommandModeShortcut ?? HotkeyShortcut.RightCtrl(), s.CommandModeShortcutEnabled,
            sc => { s.CommandModeShortcut = sc; s.Save("hotkey"); },
            on => { s.CommandModeShortcutEnabled = on; s.Save("hotkey"); }));

        Children.Add(Theme.Card2(hk));

        // --- Overlay ---
        var ov = new StackPanel();
        ov.Children.Add(Theme.Heading("Overlay"));
        ov.Children.Add(Theme.Label("Size"));
        var sizeCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var sz in Enum.GetValues<OverlaySize>()) sizeCombo.Items.Add(sz.ToString());
        sizeCombo.SelectedItem = s.OverlaySize.ToString();
        sizeCombo.SelectionChanged += (_, _) =>
        {
            if (Enum.TryParse<OverlaySize>((string)sizeCombo.SelectedItem, out var sz)) { s.OverlaySize = sz; s.Save("overlay"); }
        };
        ov.Children.Add(sizeCombo);
        ov.Children.Add(Theme.Caption("Pill (minimal), Small, Medium, Large. Live transcription appears in Small and larger."));

        ov.Children.Add(Theme.Label("Position"));
        var posCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var p in Enum.GetValues<OverlayPosition>()) posCombo.Items.Add(p.ToString());
        posCombo.SelectedItem = s.OverlayPosition.ToString();
        posCombo.SelectionChanged += (_, _) =>
        {
            if (Enum.TryParse<OverlayPosition>((string)posCombo.SelectedItem, out var p)) { s.OverlayPosition = p; s.Save("overlay"); }
        };
        ov.Children.Add(posCombo);

        ov.Children.Add(Theme.Label("Distance from screen edge"));
        ov.Children.Add(Theme.Slider(8, 320, s.OverlayBottomOffset,
            v => { s.OverlayBottomOffset = Math.Round(v); s.Save("overlay"); },
            v => $"{(int)Math.Round(v)} px"));
        ov.Children.Add(Theme.Caption("How far the bar sits from the screen edge. Lower = closer to the edge (a bit lower on screen, for the default Bottom position)."));

        ov.Children.Add(Theme.Toggle("Show live transcription preview", s.EnableStreamingPreview, v => { s.EnableStreamingPreview = v; s.Save("overlay"); }));
        Children.Add(Theme.Card2(ov));

        // --- VoiceBox ---
        var vbx = new StackPanel();
        vbx.Children.Add(Theme.Heading("VoiceBox"));
        vbx.Children.Add(Theme.Toggle("Keep VoiceBox's AI engine warm in the background", s.VoiceBoxPrewarmEnabled, v =>
        {
            s.VoiceBoxPrewarmEnabled = v;
            s.Save("voicebox");
            if (v) App.VoiceBoxManager.PrewarmServer();
        }));
        vbx.Children.Add(Theme.Caption("Starts VoiceBox's engine quietly when LiquidFlow launches, so the VoiceBox tab opens in seconds instead of a cold boot. Costs some idle memory — turn off to free it."));
        Children.Add(Theme.Card2(vbx));

        // --- Appearance ---
        var ap = new StackPanel();
        ap.Children.Add(Theme.Heading("Appearance"));
        ap.Children.Add(Theme.Label("Theme"));
        var themeCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var t in Enum.GetValues<ThemePreference>()) themeCombo.Items.Add(t.ToString());
        themeCombo.SelectedItem = s.Theme.ToString();
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (Enum.TryParse<ThemePreference>((string)themeCombo.SelectedItem, out var t)) { s.Theme = t; s.Save("theme"); }
        };
        ap.Children.Add(themeCombo);

        ap.Children.Add(Theme.Label("Font"));
        var fontCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (name, stack) in FontChoice.Options)
            fontCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name, FontFamily = new FontFamily(stack) });
        var fontIdx = Array.FindIndex(FontChoice.Options, o => o.Name.Equals(s.AppFont, StringComparison.OrdinalIgnoreCase));
        fontCombo.SelectedIndex = fontIdx >= 0 ? fontIdx : 0;
        fontCombo.SelectionChanged += (_, _) =>
        {
            if (fontCombo.SelectedItem is ComboBoxItem it && it.Tag is string name)
            {
                s.AppFont = name;
                s.Save("font");
            }
        };
        ap.Children.Add(fontCombo);
        ap.Children.Add(Theme.Caption("Changes the font across the whole app instantly."));

        ap.Children.Add(Theme.Label("Text size"));
        var sizeOptions = new (string Label, double Scale)[]
        {
            ("Compact (85%)", 0.85), ("Default (90%)", 0.9), ("Medium (100%)", 1.0),
            ("Large (110%)", 1.1), ("Extra large (120%)", 1.2),
        };
        var scaleCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (label, _) in sizeOptions) scaleCombo.Items.Add(label);
        var scaleIdx = Array.FindIndex(sizeOptions, o => Math.Abs(o.Scale - s.UiScale) < 0.01);
        scaleCombo.SelectedIndex = scaleIdx >= 0 ? scaleIdx : 1;
        scaleCombo.SelectionChanged += (_, _) =>
        {
            if (scaleCombo.SelectedIndex >= 0)
            {
                s.UiScale = sizeOptions[scaleCombo.SelectedIndex].Scale;
                s.Save("font"); // font hint re-renders every page/modal at the new scale
            }
        };
        ap.Children.Add(scaleCombo);
        ap.Children.Add(Theme.Caption("Scales page content up or down."));
        Children.Add(Theme.Card2(ap));

        // --- Behavior ---
        var bh = new StackPanel();
        bh.Children.Add(Theme.Heading("Behavior"));

        // Silero VAD auto-stop (OpenWhispr port). The tiny model downloads on first enable.
        var vadStatus = Theme.Caption("Ends the recording after you stop talking. Doesn't apply in Hold mode.");
        bh.Children.Add(Theme.Toggle("Stop automatically after silence", s.VadAutoStopEnabled, v =>
        {
            s.VadAutoStopEnabled = v;
            s.Save();
            if (v && !FluidVoice.Audio.VadAutoStopMonitor.IsModelInstalled)
            {
                vadStatus.Text = "Downloading voice-detection model…";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FluidVoice.Audio.VadAutoStopMonitor.DownloadModelAsync(null, CancellationToken.None);
                        await Dispatcher.BeginInvoke(() => vadStatus.Text = "Voice-detection model ready.");
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.BeginInvoke(() =>
                            vadStatus.Text = $"Model download failed ({ex.Message}) — using the simpler level-based detector.");
                    }
                });
            }
        }));
        var vadRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        vadRow.Children.Add(new TextBlock
        {
            Text = "Silence duration",
            FontSize = 13,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        var vadCombo = new ComboBox { Width = 130 };
        var silenceChoices = new (string Label, double Secs)[]
        {
            ("1.5 seconds", 1.5), ("2 seconds", 2.0), ("2.5 seconds", 2.5), ("3 seconds", 3.0), ("5 seconds", 5.0),
        };
        foreach (var (label, _) in silenceChoices) vadCombo.Items.Add(label);
        var vadIdx = Array.FindIndex(silenceChoices, c => Math.Abs(c.Secs - s.VadAutoStopSilenceSeconds) < 0.01);
        vadCombo.SelectedIndex = vadIdx >= 0 ? vadIdx : 2;
        vadCombo.SelectionChanged += (_, _) =>
        {
            if (vadCombo.SelectedIndex >= 0) { s.VadAutoStopSilenceSeconds = silenceChoices[vadCombo.SelectedIndex].Secs; s.Save(); }
        };
        vadRow.Children.Add(vadCombo);
        bh.Children.Add(vadRow);
        bh.Children.Add(vadStatus);

        bh.Children.Add(Theme.Toggle("Play start/stop sounds", s.EnableTranscriptionSounds, v => { s.EnableTranscriptionSounds = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Pause media while dictating", s.PauseMediaDuringTranscription, v => { s.PauseMediaDuringTranscription = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Copy transcription to clipboard", s.CopyTranscriptionToClipboard, v => { s.CopyTranscriptionToClipboard = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Show setup checklist & how-to on Home", s.ShowHomeSetup, v => { s.ShowHomeSetup = v; s.Save("home"); }));
        bh.Children.Add(Theme.Toggle("Launch at startup", s.LaunchAtStartup, v => { s.LaunchAtStartup = v; s.Save(); StartupManager.Apply(v); }));
        bh.Children.Add(Theme.Toggle("Check for updates automatically", s.AutoUpdateCheckEnabled, v => { s.AutoUpdateCheckEnabled = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Include beta releases", s.BetaReleasesEnabled, v => { s.BetaReleasesEnabled = v; s.Save(); }));

        bh.Children.Add(Theme.Label("Update folder"));
        var updRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var pathBox = new TextBox
        {
            Width = 330,
            Text = s.UpdateFolderPath,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pathBox.LostFocus += (_, _) => { s.UpdateFolderPath = pathBox.Text.Trim(); s.Save("update"); };
        var browse = Theme.SecondaryButton("Browse…");
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.VerticalAlignment = VerticalAlignment.Center;
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder to watch for updates" };
            if (dlg.ShowDialog() == true) { pathBox.Text = dlg.FolderName; s.UpdateFolderPath = dlg.FolderName; s.Save("update"); }
        };
        var checkNow = Theme.SecondaryButton("Check now");
        checkNow.Margin = new Thickness(8, 0, 0, 0);
        checkNow.VerticalAlignment = VerticalAlignment.Center;
        checkNow.Click += (_, _) => _ = FluidVoice.App.UpdateCoordinator.RefreshAsync(interactive: true);
        updRow.Children.Add(pathBox);
        updRow.Children.Add(browse);
        updRow.Children.Add(checkNow);
        bh.Children.Add(updRow);
        bh.Children.Add(Theme.Caption("Drop new LiquidFlow-Setup-<version>-<arch>.exe builds in this folder. When a newer one appears, an Update button + notification show up — click to install."));

        Children.Add(Theme.Card2(bh));
    }

    private static UIElement WithEnableToggle(string name, HotkeyShortcut sc, bool enabled,
        Action<HotkeyShortcut> onChange, Action<bool> onToggle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(Theme.Toggle(name, enabled, onToggle));
        var rec = new ShortcutRecorder(sc);
        rec.ShortcutChanged += onChange;
        panel.Children.Add(rec);
        return panel;
    }
}
