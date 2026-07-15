using System.Windows;
using System.Windows.Controls;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>Local formatting pipeline toggles + filler-word list (SettingsView formatting area).</summary>
public sealed class FormattingTab : StackPanel
{
    public FormattingTab()
    {
        var s = Settings.Current;

        var fmt = new StackPanel();
        fmt.Children.Add(Theme.Heading("Formatting"));
        fmt.Children.Add(Theme.Caption("Applied locally before LiquidFlow types the result."));
        fmt.Children.Add(Theme.Toggle("Convert spoken punctuation", s.AutoConvertPunctuationEnabled, v => { s.AutoConvertPunctuationEnabled = v; s.Save("fmt"); }));
        fmt.Children.Add(Theme.Toggle("Remove filler words", s.RemoveFillerWordsEnabled, v => { s.RemoveFillerWordsEnabled = v; s.Save("fmt"); }));
        Children.Add(Theme.Card2(fmt));

        var fillers = new StackPanel();
        fillers.Children.Add(Theme.Heading("Filler words"));
        fillers.Children.Add(Theme.Caption("Comma-separated words to strip when filler removal is on."));
        var box = new TextBox
        {
            Text = string.Join(", ", s.FillerWords),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 60,
            Padding = new Thickness(8),
        };
        box.LostFocus += (_, _) =>
        {
            s.FillerWords = box.Text.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().ToList();
            s.Save("fmt");
        };
        fillers.Children.Add(box);
        Children.Add(Theme.Card2(fillers));

        var gaav = new StackPanel();
        gaav.Children.Add(Theme.Heading("Post-processing"));
        gaav.Children.Add(Theme.Toggle("Remove trailing period", s.GaavRemoveTrailingPeriodEnabled, v => { s.GaavRemoveTrailingPeriodEnabled = v; s.Save("fmt"); }));
        gaav.Children.Add(Theme.Toggle("Lowercase first letter", s.GaavLowercaseFirstLetterEnabled, v => { s.GaavLowercaseFirstLetterEnabled = v; s.Save("fmt"); }));
        gaav.Children.Add(Theme.Toggle("Context-aware capitalization (continuous dictation)", s.ContextAwareCapitalizationEnabled, v => { s.ContextAwareCapitalizationEnabled = v; s.Save("fmt"); }));
        gaav.Children.Add(Theme.Toggle("Auto-space between continuous dictations", s.ContinuousDictationSpacingEnabled, v => { s.ContinuousDictationSpacingEnabled = v; s.Save("fmt"); }));
        Children.Add(Theme.Card2(gaav));

        var typing = new StackPanel();
        typing.Children.Add(Theme.Heading("Text insertion"));
        typing.Children.Add(Theme.Label("Method"));
        var combo = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        combo.Items.Add("Clipboard-free insert (fastest)");   // Standard
        combo.Items.Add("Clipboard paste (most compatible)"); // ReliablePaste
        combo.Items.Add("Typing effect (types it out)");       // TypeOut
        combo.SelectedIndex = s.TextInsertionMode switch
        {
            TextInsertionMode.ReliablePaste => 1,
            TextInsertionMode.TypeOut => 2,
            _ => 0,
        };

        // Speed control for the typing effect — only relevant when "Typing effect" is chosen.
        var speedPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        speedPanel.Children.Add(Theme.Label("Typing speed"));
        speedPanel.Children.Add(Theme.Slider(0, 100, s.TypingSpeed,
            v => { s.TypingSpeed = (int)Math.Round(v); s.Save("typing"); },
            v => (int)Math.Round(v) >= 100 ? "Instant" : $"{(int)Math.Round(v)}"));
        speedPanel.Children.Add(Theme.Caption("100 = instant. Lower it to watch the text type out character by character."));
        speedPanel.Visibility = s.TextInsertionMode == TextInsertionMode.TypeOut ? Visibility.Visible : Visibility.Collapsed;

        combo.SelectionChanged += (_, _) =>
        {
            s.TextInsertionMode = combo.SelectedIndex switch
            {
                1 => TextInsertionMode.ReliablePaste,
                2 => TextInsertionMode.TypeOut,
                _ => TextInsertionMode.Standard,
            };
            s.Save("typing");
            speedPanel.Visibility = combo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        };
        typing.Children.Add(combo);
        typing.Children.Add(Theme.Caption("Use clipboard paste if a Windows app drops characters during direct insertion."));
        typing.Children.Add(speedPanel);
        Children.Add(Theme.Card2(typing));
    }
}
