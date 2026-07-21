using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Text;

namespace FluidVoice.Ui;

/// <summary>Custom dictionary editor + auto-learned corrections review.</summary>
public sealed class DictionaryTab : StackPanel
{
    private bool _bannerDismissed;

    public DictionaryTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(PageChrome.HeaderRow("Dictionary", "Add new", AddBlankEntry));
        Children.Add(PageChrome.TabsRow(new[] { "All", "Personal" }, 0));
        if (!_bannerDismissed) Children.Add(BuildBanner());
        var learned = BuildLearned();
        if (learned is not null) Children.Add(learned);
        Children.Add(BuildList());
    }

    private UIElement BuildBanner()
    {
        // Warm, blurred-photo-inspired banner matching the rest of the hub.
        var grid = new Grid { Height = 190, ClipToBounds = true };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(14, 14, 15), 0),
                    new(Color.FromRgb(27, 23, 21), 0.55),
                    new(Color.FromRgb(54, 39, 26), 1),
                },
                new Point(0, 0.3), new Point(1, 0.9)),
        });
        grid.Children.Add(new Border
        {
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(18),
            Opacity = 0.82,
            Background = new RadialGradientBrush(Color.FromArgb(190, 196, 128, 58), Color.FromArgb(0, 196, 128, 58))
            {
                Center = new Point(0.75, 0.35),
                GradientOrigin = new Point(0.75, 0.35),
                RadiusX = 0.7,
                RadiusY = 0.9,
            },
        });

        var content = new StackPanel { Margin = new Thickness(40, 28, 40, 28), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
        title.Inlines.Add(new Run("LiquidFlow spells the way "));
        title.Inlines.Add(new Run("you") { FontStyle = FontStyles.Italic });
        title.Inlines.Add(new Run(" do."));
        content.Children.Add(title);
        var body = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        };
        body.Inlines.Add(new Run("LiquidFlow learns your unique words and names — automatically or manually. "));
        body.Inlines.Add(new Run("Add personal terms, company jargon, client names, or fixed spellings") { FontWeight = FontWeights.SemiBold });
        body.Inlines.Add(new Run(" so they always come out exactly right."));
        content.Children.Add(body);
        var chips = new WrapPanel();
        var addChip = PageChrome.HeroPill("Add new word");
        addChip.Margin = new Thickness(0, 0, 10, 8);
        addChip.MouseLeftButtonUp += (_, _) => AddBlankEntry();
        chips.Children.Add(addChip);
        foreach (var word in Settings.Current.CustomDictionaryEntries
                     .Where(e => !e.Delete && e.Replacement.Length > 0)
                     .Select(e => e.Replacement).Distinct().Take(5))
        {
            var c = PageChrome.HeroChip(word);
            c.Margin = new Thickness(0, 0, 10, 8);
            chips.Children.Add(c);
        }
        content.Children.Add(chips);
        grid.Children.Add(content);

        var close = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.MouseLeftButtonUp += (_, _) => { _bannerDismissed = true; Build(); };
        grid.Children.Add(close);

        return new Border { CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 26), Child = grid };
    }

    // ---- auto-learned corrections ----

    private UIElement? BuildLearned()
    {
        var s = Settings.Current;
        var pending = s.LearnedCorrections.Where(c => !c.Promoted && !c.Dismissed).OrderByDescending(c => c.Count).ToList();
        var promoted = s.LearnedCorrections.Where(c => c.Promoted).OrderByDescending(c => c.Count).Take(6).ToList();
        if (pending.Count == 0 && promoted.Count == 0) return null;

        var panel = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var h = Theme.Heading("Learned automatically");
        h.Margin = new Thickness(0, 0, 0, 0);
        head.Children.Add(h);
        var count = pending.Count;
        if (count > 0)
        {
            var badge = Theme.Pill($"{count} to review", Theme.PurpleBrush, Brushes.White, 10.5);
            badge.Margin = new Thickness(10, 2, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(badge);
        }
        panel.Children.Add(head);
        panel.Children.Add(Theme.Caption("Corrections AI cleanup made to your transcripts. Add the ones you want kept forever; they become dictionary entries."));

        foreach (var c in pending) panel.Children.Add(LearnedRow(c, promoted: false));
        if (promoted.Count > 0)
        {
            var sub = Theme.Eyebrow("Added to dictionary");
            sub.Margin = new Thickness(2, 12, 0, 6);
            panel.Children.Add(sub);
            foreach (var c in promoted) panel.Children.Add(LearnedRow(c, promoted: true));
        }
        return Theme.Card2(panel);
    }

    private UIElement LearnedRow(LearnedCorrection c, bool promoted)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new Run(c.From) { Foreground = Theme.SubtleBrush, TextDecorations = TextDecorations.Strikethrough });
        text.Inlines.Add(new Run("  →  ") { Foreground = Theme.SubtleBrush });
        text.Inlines.Add(new Run(c.To) { Foreground = Theme.TextBrush, FontWeight = FontWeights.SemiBold });
        if (!promoted && c.Count > 1) text.Inlines.Add(new Run($"   ·  seen {c.Count}×") { Foreground = Theme.SubtleBrush, FontSize = 11 });
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (promoted)
        {
            actions.Children.Add(new TextBlock { Text = "Added", FontSize = 12, Foreground = Theme.GreenBrush, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var add = Theme.PrimaryButton("Add");
            add.Padding = new Thickness(12, 4, 12, 4);
            add.Margin = new Thickness(0, 0, 6, 0);
            add.Click += (_, _) => { CorrectionLearner.Promote(c); Settings.Current.Save("autolearn"); Build(); };
            var dismiss = Theme.SecondaryButton("Dismiss");
            dismiss.Padding = new Thickness(12, 4, 12, 4);
            dismiss.Click += (_, _) => { c.Dismissed = true; Settings.Current.Save("autolearn"); Build(); };
            actions.Children.Add(add);
            actions.Children.Add(dismiss);
        }
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    // ---- manual dictionary ----

    private UIElement BuildToolbar()
    {
        var toolbar = new DockPanel { Margin = new Thickness(0, 4, 0, 14) };
        var addBtn = Theme.PrimaryButton("Add new");
        addBtn.Click += (_, _) => AddBlankEntry();
        DockPanel.SetDock(addBtn, Dock.Right);
        toolbar.Children.Add(addBtn);
        toolbar.Children.Add(new TextBlock
        {
            Text = "Your dictionary",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        return toolbar;
    }

    private void AddBlankEntry()
    {
        Settings.Current.CustomDictionaryEntries.Insert(0, new CustomDictionaryEntry { Triggers = new List<string> { "" }, Replacement = "" });
        Settings.Current.Save("dictionary");
        Build();
    }

    private UIElement BuildList()
    {
        var list = new StackPanel();
        var entries = Settings.Current.CustomDictionaryEntries.ToList();
        if (entries.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No dictionary terms yet. Use Add new to teach names, jargon, or fixed spellings.",
                FontSize = 14,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(20, 20, 20, 20),
            });
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                list.Children.Add(EntryRow(entries[i]));
                if (i < entries.Count - 1) list.Children.Add(Theme.Divider());
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
        var grid = new Grid { Margin = new Thickness(20, 14, 14, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var trigger = new TextBox
        {
            Text = string.Join(", ", entry.Triggers),
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "Words or phrases to listen for (comma-separated)",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        trigger.LostFocus += (_, _) =>
        {
            entry.Triggers = trigger.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            Settings.Current.Save("dictionary");
        };
        Grid.SetColumn(trigger, 0);
        grid.Children.Add(trigger);

        var arrow = new TextBlock { Text = "→", Foreground = Theme.SubtleBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        var replacement = new TextBox
        {
            Text = entry.Replacement,
            Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "Replacement text",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        replacement.LostFocus += (_, _) => { entry.Replacement = replacement.Text; Settings.Current.Save("dictionary"); };
        Grid.SetColumn(replacement, 2);
        grid.Children.Add(replacement);

        var delete = new Button
        {
            Content = new TextBlock { Text = ((char)0xE74D).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Delete term",
        };
        delete.Click += (_, _) => { Settings.Current.CustomDictionaryEntries.Remove(entry); Settings.Current.Save("dictionary"); Build(); };
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);
        return grid;
    }
}
