using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>Custom dictionary editor: trigger to replacement rows.</summary>
public sealed class DictionaryTab : StackPanel
{
    public DictionaryTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(BuildToolbar());
        Children.Add(BuildBanner());
        Children.Add(BuildList());
    }

    private UIElement BuildToolbar()
    {
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };
        var addBtn = Theme.PrimaryButton("Add new");
        addBtn.Click += (_, _) =>
        {
            Settings.Current.CustomDictionaryEntries.Insert(0, new CustomDictionaryEntry
            {
                Triggers = new List<string> { "" },
                Replacement = "",
            });
            Settings.Current.Save("dictionary");
            Build();
        };
        DockPanel.SetDock(addBtn, Dock.Right);
        toolbar.Children.Add(addBtn);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        tabs.Children.Add(Tab("All", true));
        tabs.Children.Add(Tab("Personal", false));
        tabs.Children.Add(Tab("Shared with team", false));
        toolbar.Children.Add(tabs);
        return toolbar;
    }

    private static UIElement Tab(string label, bool active)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = active ? Theme.TextBrush : Theme.SubtleBrush,
        });
        panel.Children.Add(new Border
        {
            Width = active ? 24 : 0,
            Height = 2,
            Background = Theme.TextBrush,
            Margin = new Thickness(0, 18, 0, -20),
        });
        return panel;
    }

    private UIElement BuildBanner()
    {
        var grid = new Grid { Height = 214, ClipToBounds = true };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(58, 36, 29), 0),
                    new(Color.FromRgb(126, 82, 44), 0.52),
                    new(Color.FromRgb(35, 22, 19), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
        });
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(76, 0, 0, 0)),
            CornerRadius = new CornerRadius(18),
        });

        var content = new StackPanel
        {
            Margin = new Thickness(36, 30, 36, 30),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock
        {
            FontFamily = Theme.DisplaySerif,
            FontSize = 34,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 16),
        };
        title.Inlines.Add(new Run("LiquidFlow spells the way "));
        title.Inlines.Add(new Run("you") { FontStyle = FontStyles.Italic });
        title.Inlines.Add(new Run(" do."));
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = "Teach LiquidFlow names, jargon, casing, URLs, and phrases that should always come out exactly right.",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22),
        });

        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        var addWord = Theme.SecondaryButton("Add new word");
        addWord.Click += (_, _) =>
        {
            Settings.Current.CustomDictionaryEntries.Insert(0, new CustomDictionaryEntry { Triggers = new List<string> { "" }, Replacement = "" });
            Settings.Current.Save("dictionary");
            Build();
        };
        chips.Children.Add(addWord);
        foreach (var sample in new[] { "LiquidFlow", "NVIDIA", "Claude.md", "Brookfield" })
        {
            var chip = Theme.Pill(sample, new SolidColorBrush(Color.FromArgb(210, 239, 234, 226)), Theme.TextBrush, 14);
            chip.Margin = new Thickness(10, 0, 0, 0);
            chips.Children.Add(chip);
        }
        content.Children.Add(chips);
        grid.Children.Add(content);

        return new Border
        {
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 0, 34),
            Child = grid,
        };
    }

    private UIElement BuildList()
    {
        var list = new StackPanel();
        var entries = Settings.Current.CustomDictionaryEntries.ToList();
        if (entries.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No dictionary terms yet.",
                FontSize = 15,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(24, 22, 24, 22),
            });
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                list.Children.Add(EntryRow(entries[i]));
                if (i < entries.Count - 1)
                    list.Children.Add(Theme.Divider());
            }
        }

        return new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = list,
        };
    }

    private UIElement EntryRow(CustomDictionaryEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(24, 16, 18, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        var trigger = new TextBox
        {
            Text = string.Join(", ", entry.Triggers),
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "Words or phrases to listen for",
        };
        trigger.LostFocus += (_, _) =>
        {
            entry.Triggers = trigger.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
            Settings.Current.Save("dictionary");
        };
        Grid.SetColumn(trigger, 0);
        grid.Children.Add(trigger);

        var replacement = new TextBox
        {
            Text = entry.Replacement,
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "Replacement text",
        };
        replacement.LostFocus += (_, _) =>
        {
            entry.Replacement = replacement.Text;
            Settings.Current.Save("dictionary");
        };
        Grid.SetColumn(replacement, 2);
        grid.Children.Add(replacement);

        var delete = new Button
        {
            Content = "X",
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Delete term",
        };
        delete.Click += (_, _) =>
        {
            Settings.Current.CustomDictionaryEntries.Remove(entry);
            Settings.Current.Save("dictionary");
            Build();
        };
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);
        return grid;
    }
}
