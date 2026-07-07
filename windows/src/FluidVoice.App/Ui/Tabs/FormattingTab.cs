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
        fmt.Children.Add(Theme.Caption("Applied locally before FluidVoice types the result."));
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
        combo.Items.Add("Clipboard-free insert (fastest)");
        combo.Items.Add("Clipboard paste (most compatible)");
        combo.SelectedIndex = s.TextInsertionMode == TextInsertionMode.ReliablePaste ? 1 : 0;
        combo.SelectionChanged += (_, _) =>
        {
            s.TextInsertionMode = combo.SelectedIndex == 1 ? TextInsertionMode.ReliablePaste : TextInsertionMode.Standard;
            s.Save("typing");
        };
        typing.Children.Add(combo);
        typing.Children.Add(Theme.Caption("Use clipboard paste if a Windows app drops characters during direct insertion."));
        Children.Add(Theme.Card2(typing));
    }
}
