using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// Snippets: say a trigger word and the saved text is inserted (applied in
/// TranscriptFormatter.ApplySnippets). Page layout mirrors the reference design:
/// title + "Add new", All/Personal tabs, dark hero with example chips, then the list.
/// </summary>
public sealed class SnippetsTab : StackPanel
{
    public SnippetsTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(PageChrome.HeaderRow("Snippets", "Add new", AddBlankSnippet));
        Children.Add(PageChrome.TabsRow(new[] { "All", "Personal" }, 0));
        Children.Add(BuildHero());
        Children.Add(BuildList());
    }

    private UIElement BuildHero()
    {
        var content = new StackPanel { Margin = new Thickness(40, 28, 40, 28), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
        title.Inlines.Add(new Run("The stuff "));
        title.Inlines.Add(new Run("you") { FontStyle = FontStyles.Italic });
        title.Inlines.Add(new Run(" shouldn’t have to re-type."));
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = "Save text you type often — an email, intro, or prompt — then say a word to drop it in instantly.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        });

        // illustrative example rows (chip -> chip)
        content.Children.Add(ExampleRow("“my LinkedIn”", "https://www.linkedin.com/in/john-doe/"));
        content.Children.Add(ExampleRow("“rewrite prompt”", "Rewrite this to be more concise…"));
        content.Children.Add(ExampleRow("“intro email”", "Hey, would love to find some time to chat later…"));

        var add = PageChrome.HeroPill("Add new snippet");
        add.Margin = new Thickness(0, 14, 0, 0);
        add.MouseLeftButtonUp += (_, _) => AddBlankSnippet();
        content.Children.Add(add);

        var hero = PageChrome.DarkHero(content);
        ((Border)hero).MinHeight = 190;
        ((Border)hero).Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private static UIElement ExampleRow(string trigger, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(PageChrome.HeroChip(trigger, italic: true));
        row.Children.Add(new TextBlock
        {
            Text = "→",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        });
        row.Children.Add(PageChrome.HeroChip(text));
        return row;
    }

    private void AddBlankSnippet()
    {
        Settings.Current.Snippets.Insert(0, new Snippet { Trigger = "", Text = "" });
        Settings.Current.Save("snippets");
        Build();
    }

    /// <summary>Inline-editable rows in one bordered card — the same add/edit pattern
    /// (and exact row geometry) as the Dictionary page.</summary>
    private UIElement BuildList()
    {
        var list = new StackPanel();
        var snippets = Settings.Current.Snippets.ToList();
        if (snippets.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No snippets yet. Use Add new — say the trigger word while dictating and the saved text drops in.",
                FontSize = 14,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(20, 20, 20, 20),
            });
        }
        else
        {
            for (int i = 0; i < snippets.Count; i++)
            {
                list.Children.Add(EntryRow(snippets[i]));
                if (i < snippets.Count - 1) list.Children.Add(Theme.Divider());
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

    private UIElement EntryRow(Snippet s)
    {
        var grid = new Grid { Margin = new Thickness(20, 14, 14, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var trigger = new TextBox
        {
            Text = s.Trigger,
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "Say this word or phrase while dictating",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        trigger.LostFocus += (_, _) => { s.Trigger = trigger.Text.Trim(); Settings.Current.Save("snippets"); };
        Grid.SetColumn(trigger, 0);
        grid.Children.Add(trigger);

        var arrow = new TextBlock { Text = "\u2192", Foreground = Theme.SubtleBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        var body = new TextBox
        {
            Text = s.Text,
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "\u2026and LiquidFlow types this instead",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        body.LostFocus += (_, _) => { s.Text = body.Text; Settings.Current.Save("snippets"); };
        Grid.SetColumn(body, 2);
        grid.Children.Add(body);

        var delete = new Button
        {
            Content = new TextBlock { Text = ((char)0xE74D).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Delete snippet",
        };
        delete.Click += (_, _) =>
        {
            Settings.Current.Snippets.RemoveAll(x => x.Id == s.Id);
            Settings.Current.Save("snippets");
            Build();
        };
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);
        return grid;
    }
}
