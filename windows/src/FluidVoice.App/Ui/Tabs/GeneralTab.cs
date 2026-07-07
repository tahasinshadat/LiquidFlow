using System.Windows;
using System.Windows.Controls;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Input;

namespace FluidVoice.Ui;

/// <summary>Hotkey, activation mode, overlay size/position, theme (SettingsView general area).</summary>
public sealed class GeneralTab : StackPanel
{
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
        hk.Children.Add(Theme.Caption("While FluidVoice is running, the Copilot key starts dictation instead of opening Copilot."));

        hk.Children.Add(Theme.Label("Activation mode"));
        var modeCombo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var m in Enum.GetValues<HotkeyActivationMode>()) modeCombo.Items.Add(m.ToString());
        modeCombo.SelectedItem = s.HotkeyMode.ToString();
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (Enum.TryParse<HotkeyActivationMode>((string)modeCombo.SelectedItem, out var m)) { s.HotkeyMode = m; s.Save("hotkey"); }
        };
        hk.Children.Add(modeCombo);
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
        ov.Children.Add(Theme.Toggle("Show live transcription preview", s.EnableStreamingPreview, v => { s.EnableStreamingPreview = v; s.Save("overlay"); }));
        Children.Add(Theme.Card2(ov));

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
        Children.Add(Theme.Card2(ap));

        // --- Behavior ---
        var bh = new StackPanel();
        bh.Children.Add(Theme.Heading("Behavior"));
        bh.Children.Add(Theme.Toggle("Play start/stop sounds", s.EnableTranscriptionSounds, v => { s.EnableTranscriptionSounds = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Pause media while dictating", s.PauseMediaDuringTranscription, v => { s.PauseMediaDuringTranscription = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Copy transcription to clipboard", s.CopyTranscriptionToClipboard, v => { s.CopyTranscriptionToClipboard = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Launch at startup", s.LaunchAtStartup, v => { s.LaunchAtStartup = v; s.Save(); StartupManager.Apply(v); }));
        bh.Children.Add(Theme.Toggle("Check for updates automatically", s.AutoUpdateCheckEnabled, v => { s.AutoUpdateCheckEnabled = v; s.Save(); }));
        bh.Children.Add(Theme.Toggle("Include beta releases", s.BetaReleasesEnabled, v => { s.BetaReleasesEnabled = v; s.Save(); }));
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
