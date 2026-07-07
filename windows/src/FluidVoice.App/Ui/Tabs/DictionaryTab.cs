using System.Windows;
using System.Windows.Controls;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>Custom dictionary editor: trigger → replacement rows (CustomDictionaryView.swift).</summary>
public sealed class DictionaryTab : StackPanel
{
    public DictionaryTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(Theme.Heading("Custom dictionary"));
        Children.Add(Theme.Caption("Replace what you say with something specific — names, jargon, casing. Matched whole-word, case-insensitively, on every transcript."));

        var addBtn = new Button { Content = "+ Add entry", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        addBtn.Click += (_, _) =>
        {
            Settings.Current.CustomDictionaryEntries.Add(new CustomDictionaryEntry { Triggers = new List<string> { "" }, Replacement = "" });
            Settings.Current.Save("dictionary");
            Build();
        };
        Children.Add(addBtn);

        foreach (var entry in Settings.Current.CustomDictionaryEntries.ToList())
            Children.Add(EntryRow(entry));
    }

    private Border EntryRow(CustomDictionaryEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var triggerBox = new TextBox { Text = string.Join(", ", entry.Triggers), Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0) };
        triggerBox.LostFocus += (_, _) =>
        {
            entry.Triggers = triggerBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            Settings.Current.Save("dictionary");
        };
        Grid.SetColumn(triggerBox, 0);
        grid.Children.Add(triggerBox);

        var replaceBox = new TextBox { Text = entry.Replacement, Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0) };
        replaceBox.LostFocus += (_, _) => { entry.Replacement = replaceBox.Text; Settings.Current.Save("dictionary"); };
        Grid.SetColumn(replaceBox, 1);
        grid.Children.Add(replaceBox);

        var delBtn = new Button { Content = "✕", Padding = new Thickness(8, 4, 8, 4) };
        delBtn.Click += (_, _) =>
        {
            Settings.Current.CustomDictionaryEntries.Remove(entry);
            Settings.Current.Save("dictionary");
            Build();
        };
        Grid.SetColumn(delBtn, 2);
        grid.Children.Add(delBtn);

        var panel = new StackPanel();
        var labels = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l1 = Theme.Caption("Say (comma-separated)"); Grid.SetColumn(l1, 0); labels.Children.Add(l1);
        var l2 = Theme.Caption("Replace with"); Grid.SetColumn(l2, 1); labels.Children.Add(l2);
        panel.Children.Add(labels);
        panel.Children.Add(grid);
        return Theme.Card2(panel);
    }
}
