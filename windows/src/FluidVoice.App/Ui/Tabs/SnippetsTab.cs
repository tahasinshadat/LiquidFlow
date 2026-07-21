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
        Children.Add(PageChrome.HeaderRow("Snippets", "Add new", () => EditSnippet(null)));
        Children.Add(PageChrome.TabsRow(new[] { "All", "Personal" }, 0));
        Children.Add(BuildHero());
        Children.Add(BuildList());
    }

    private UIElement BuildHero()
    {
        var content = new StackPanel { Margin = new Thickness(40, 30, 40, 30) };
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
        add.MouseLeftButtonUp += (_, _) => EditSnippet(null);
        content.Children.Add(add);

        return PageChrome.DarkHero(content);
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

    private UIElement BuildList()
    {
        var host = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        var snippets = Settings.Current.Snippets;
        if (snippets.Count == 0) return host; // hero carries the empty state
        foreach (var s in snippets.ToList())
            host.Children.Add(SnippetRow(s));
        return host;
    }

    private UIElement SnippetRow(Snippet s)
    {
        var grid = new Grid { Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var trig = new TextBlock
        {
            Text = $"“{s.Trigger}”",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(trig, 0);
        grid.Children.Add(trig);

        var body = new TextBlock
        {
            Text = s.Text.Replace('\n', ' '),
            FontSize = 13.5,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 8, 0),
        };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0 };
        actions.Children.Add(PageChrome.IconButton("", "Edit", () => EditSnippet(s)));
        actions.Children.Add(PageChrome.IconButton("", "Delete", () =>
        {
            Settings.Current.Snippets.RemoveAll(x => x.Id == s.Id);
            Settings.Current.Save("snippets");
            Build();
        }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        grid.MouseEnter += (_, _) => actions.Opacity = 1;
        grid.MouseLeave += (_, _) => actions.Opacity = 0;

        return new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 12, 14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid,
        };
    }

    private void EditSnippet(Snippet? existing)
    {
        var dlg = new SnippetDialog(existing) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        if (existing is null) Settings.Current.Snippets.Add(dlg.Result);
        Settings.Current.Save("snippets");
        Build();
    }
}

/// <summary>Add/edit one snippet (trigger word + inserted text).</summary>
public sealed class SnippetDialog : Window
{
    private readonly TextBox _trigger;
    private readonly TextBox _text;
    private readonly Snippet _snippet;

    public Snippet Result => _snippet;

    public SnippetDialog(Snippet? existing)
    {
        _snippet = existing ?? new Snippet();
        Title = existing is null ? "New snippet" : "Edit snippet";
        Width = 520;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Theme.Bg);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Theme.Label("Say this word or phrase…"));
        _trigger = new TextBox { Text = _snippet.Trigger, Padding = new Thickness(8, 6, 8, 6), FontSize = 14, Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(_trigger);
        root.Children.Add(Theme.Label("…and LiquidFlow types this instead"));
        _text = new TextBox
        {
            Text = _snippet.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 120, MaxHeight = 150, Padding = new Thickness(8), FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        root.Children.Add(_text);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Theme.SecondaryButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = Theme.PrimaryButton("Save");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_trigger.Text) || string.IsNullOrWhiteSpace(_text.Text)) return;
            _snippet.Trigger = _trigger.Text.Trim();
            _snippet.Text = _text.Text;
            DialogResult = true;
            Close();
        };
        btns.Children.Add(cancel);
        btns.Children.Add(save);
        root.Children.Add(btns);
        Content = root;
        Loaded += (_, _) => _trigger.Focus();
    }
}
