using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Text;

namespace FluidVoice.Ui;

/// <summary>
/// Style: per-context writing styles (personal / work / email / other) applied to AI cleanup
/// via StyleRouter. Includes the first-run personalization wizard. Mirrors the reference:
/// tab strip with a Beta "Auto cleanup" tab, dark hero with "Start now", and 3 style cards.
/// </summary>
public sealed class StyleTab : StackPanel
{
    private int _tab;

    private static readonly (string Key, string Title)[] Tabs =
    {
        ("personal", "Personal messages"),
        ("work", "Work messages"),
        ("email", "Email"),
        ("other", "Other"),
        ("cleanup", "Auto cleanup"),
    };

    public StyleTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(PageChrome.TabsRowWithBeta(Tabs.Select(t => t.Title).ToArray(), 4, _tab, i => { _tab = i; Build(); }));

        if (_tab == 4)
        {
            Children.Add(BuildAutoCleanup());
            return;
        }

        var context = Tabs[_tab].Key;
        if (!Settings.Current.StyleWizardCompleted)
        {
            Children.Add(BuildHero());
            return;
        }

        Children.Add(BuildContextBanner(context));
        Children.Add(StyleCards.BuildRow(context, large: false, onPicked: Build));
    }

    private UIElement BuildHero()
    {
        var content = new StackPanel { Margin = new Thickness(44, 40, 44, 40), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
        title.Inlines.Add(new Run("Make LiquidFlow sound like "));
        title.Inlines.Add(new Run("you") { FontStyle = FontStyles.Italic });
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = "Set up different writing styles for different apps.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 20),
        });
        var start = PageChrome.HeroPill("Start now");
        start.MouseLeftButtonUp += (_, _) =>
        {
            var wiz = new StyleWizardDialog { Owner = Window.GetWindow(this) };
            wiz.ShowDialog();
            Build();
        };
        content.Children.Add(start);
        var hero = PageChrome.DarkHero(content);
        ((Border)hero).MinHeight = 230;
        return hero;
    }

    private static UIElement BuildContextBanner(string context)
    {
        var copy = new StackPanel { Margin = new Thickness(36, 26, 36, 26), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 24, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
        title.Inlines.Add(new Run(context switch
        {
            "personal" => "This style applies in personal messengers",
            "work" => "This style applies in work chat apps",
            "email" => "This style applies in email",
            _ => "This style applies everywhere else",
        }));
        copy.Children.Add(title);
        copy.Children.Add(new TextBlock
        {
            Text = "Style formatting only applies in English. More languages coming soon.",
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
        });
        var hero = PageChrome.DarkHero(copy);
        ((Border)hero).MinHeight = 150;
        ((Border)hero).Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private static UIElement BuildAutoCleanup()
    {
        var s = Settings.Current;
        var panel = new StackPanel();
        panel.Children.Add(Theme.Heading("Auto cleanup"));
        panel.Children.Add(Theme.Caption("Applied to every dictation before styling — all on-device."));
        panel.Children.Add(Theme.Toggle("Remove filler words (um, uh…)", s.RemoveFillerWordsEnabled, v => { s.RemoveFillerWordsEnabled = v; s.Save("fmt"); }));
        panel.Children.Add(Theme.Toggle("Convert spoken punctuation (\"comma\", \"new line\")", s.AutoConvertPunctuationEnabled, v => { s.AutoConvertPunctuationEnabled = v; s.Save("fmt"); }));
        panel.Children.Add(Theme.Toggle("Learn recurring corrections into the dictionary", s.AutoLearnCorrections, v => { s.AutoLearnCorrections = v; s.Save("fmt"); }));
        return Theme.Card2(panel);
    }
}

/// <summary>The three style option cards per context (shared by the Style page and the wizard).</summary>
public static class StyleCards
{
    public sealed record Option(string Key, string Title, string Subtitle, string Sample);

    public static Option[] OptionsFor(string context) => context switch
    {
        "personal" => new[]
        {
            new Option("formal", "Formal.", "Caps + Punctuation", "Hey, are you free for lunch tomorrow? Let’s do 12 if that works for you."),
            new Option("casual", "Casual", "Caps + Less punctuation", "Hey are you free for lunch tomorrow? Let’s do 12 if that works for you"),
            new Option("very-casual", "very casual", "No Caps + Less punctuation", "hey are you free for lunch tomorrow? let’s do 12 if that works for you"),
        },
        "work" => new[]
        {
            new Option("formal", "Formal.", "Caps + Punctuation", "Hey, if you’re free, let’s chat about the great results."),
            new Option("casual", "Casual", "Caps + Less punctuation", "Hey if you’re free let’s chat about the great results"),
            new Option("excited", "Excited!", "More exclamations", "Hey, if you’re free, let’s chat about the great results!"),
        },
        "email" => new[]
        {
            new Option("formal", "Formal.", "Caps + Punctuation", "Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat.\n\nBest,\nMary"),
            new Option("casual", "Casual", "Caps + Less punctuation", "Hi Alex, it was great talking with you today. Looking forward to our next chat.\n\nBest,\nMary"),
            new Option("excited", "Excited!", "More exclamations", "Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat!\n\nBest,\nMary"),
        },
        _ => new[]
        {
            new Option("formal", "Formal.", "Caps + Punctuation", "So far, I am enjoying the new workout routine.\n\nI am excited for tomorrow’s workout, especially after a full night of rest."),
            new Option("casual", "Casual", "Caps + Less punctuation", "So far I am enjoying the new workout routine.\n\nI am excited for tomorrow’s workout especially after a full night of rest."),
            new Option("excited", "Excited!", "More exclamations", "So far, I am enjoying the new workout routine.\n\nI am excited for tomorrow’s workout, especially after a full night of rest!"),
        },
    };

    public static string GetChoice(string context) => context switch
    {
        "personal" => Settings.Current.StylePersonal,
        "work" => Settings.Current.StyleWork,
        "email" => Settings.Current.StyleEmail,
        _ => Settings.Current.StyleOther,
    };

    public static void SetChoice(string context, string key)
    {
        var s = Settings.Current;
        switch (context)
        {
            case "personal": s.StylePersonal = key; break;
            case "work": s.StyleWork = key; break;
            case "email": s.StyleEmail = key; break;
            default: s.StyleOther = key; break;
        }
        s.Save("style");
    }

    /// <summary>Three side-by-side option cards; the current choice gets an accent border.</summary>
    public static UIElement BuildRow(string context, bool large, Action? onPicked)
    {
        var grid = new Grid();
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < 2) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        }
        var options = OptionsFor(context);
        var selected = GetChoice(context);
        for (int i = 0; i < options.Length; i++)
        {
            var card = BuildCard(context, options[i], options[i].Key == selected, large, onPicked);
            Grid.SetColumn(card, i * 2);
            grid.Children.Add(card);
        }
        return grid;
    }

    private static UIElement BuildCard(string context, Option o, bool selected, bool large, Action? onPicked)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = o.Title, FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
        panel.Children.Add(new TextBlock { Text = o.Subtitle, FontSize = 13, Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 4, 0, 16) });

        // sample "message bubble"
        var bubbleHost = new Grid();
        bubbleHost.Children.Add(new Border
        {
            Background = Theme.GreenSoftBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 26, 0),
            Child = new TextBlock
            {
                Text = o.Sample,
                FontSize = 12.5,
                Foreground = Theme.TextBrush,
                TextWrapping = TextWrapping.Wrap,
            },
        });
        bubbleHost.Children.Add(new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
            Background = Theme.GreenBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -10),
            Child = new TextBlock
            {
                Text = "J", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        panel.Children.Add(bubbleHost);

        var card = new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = selected ? Theme.AccentBrush : Theme.HairlineBrush,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16, 18, 24),
            MinHeight = large ? 340 : 280,
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => { SetChoice(context, o.Key); onPicked?.Invoke(); };
        return card;
    }
}

/// <summary>4-step personalization wizard (personal → work → email → other → all set).</summary>
public sealed class StyleWizardDialog : Window
{
    private static readonly string[] Steps = { "personal", "work", "email", "other" };
    private int _step;
    private readonly Grid _host = new();

    public StyleWizardDialog()
    {
        Title = "Personalize LiquidFlow";
        Width = 1000;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var shell = new Border
        {
            Background = Theme.SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.3, Color = Colors.Black },
            Child = _host,
        };
        Content = shell;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not TextBox) try { DragMove(); } catch { } };
        Render();
    }

    private void Render()
    {
        _host.Children.Clear();
        _host.Children.Add(_step >= Steps.Length ? RenderAllSet() : RenderStep(Steps[_step]));
    }

    private UIElement RenderStep(string context)
    {
        var page = new DockPanel { Margin = new Thickness(56, 40, 56, 32), LastChildFill = true };

        // bottom nav
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (_step > 0)
        {
            var back = Theme.SecondaryButton("Back");
            back.Margin = new Thickness(0, 0, 10, 0);
            back.Click += (_, _) => { _step--; Render(); };
            nav.Children.Add(back);
        }
        var next = Theme.PrimaryButton("Next");
        next.Click += (_, _) => { _step++; Render(); };
        nav.Children.Add(next);
        DockPanel.SetDock(nav, Dock.Bottom);
        page.Children.Add(nav);

        var body = new StackPanel();
        // progress dashes
        var dashes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 26) };
        for (int i = 0; i < Steps.Length; i++)
            dashes.Children.Add(new Border
            {
                Width = 64, Height = 4, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(4, 0, 4, 0),
                Background = i <= _step ? Theme.InkBrush : new SolidColorBrush(Theme.SidebarSelected),
            });
        body.Children.Add(dashes);

        var title = new TextBlock
        {
            FontFamily = Theme.DisplaySerif,
            FontSize = 34,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 36),
        };
        title.Inlines.Add(new Run(context switch
        {
            "personal" => "How do you write your ",
            "work" => "How do you write your ",
            "email" => "How do you write your ",
            _ => "How do you write in ",
        }));
        title.Inlines.Add(new Run(context switch
        {
            "personal" => "personal messages?",
            "work" => "work messages?",
            "email" => "emails?",
            _ => "other apps?",
        })
        { Foreground = Theme.PurpleBrush, FontStyle = FontStyles.Italic });
        body.Children.Add(title);

        body.Children.Add(StyleCards.BuildRow(context, large: true, onPicked: Render));
        page.Children.Add(body);
        return page;
    }

    /// <summary>Capture-harness seam: jump straight to a wizard step (0-3) or 4 = all-set.</summary>
    public void SetStepForCapture(int step)
    {
        _step = step;
        Render();
    }

    private UIElement RenderAllSet()
    {
        if (!App.UiCapture.CaptureMode)
        {
            Settings.Current.StyleWizardCompleted = true;
            Settings.Current.Save("style");
        }

        var page = new StackPanel { Margin = new Thickness(56, 70, 56, 40), VerticalAlignment = VerticalAlignment.Center };
        page.Children.Add(new TextBlock
        {
            Text = "You’re all set!",
            FontFamily = Theme.DisplaySerif,
            FontSize = 42,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 34),
        });

        // generic app dots (no third-party logos)
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 34) };
        var palette = new[] { "#3AC8C6", "#7C37B1", "#1F707A", "#D08770", "#5E81AC", "#A3BE8C", "#B48EAD", "#E5C07B" };
        foreach (var hex in palette)
            dots.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(12),
                Margin = new Thickness(7, 0, 7, 0),
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,
                Opacity = 0.9,
            });
        page.Children.Add(dots);

        page.Children.Add(new Border
        {
            Background = Theme.CardBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24, 18, 24, 18),
            MaxWidth = 760,
            Child = new TextBlock
            {
                Text = "Try LiquidFlow in your apps to see the difference. You can update your styles anytime in the Style tab.\nStyle formatting only applies in English. More languages coming soon.",
                FontSize = 14,
                Foreground = Theme.TextBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
        });

        var done = Theme.PrimaryButton("Done");
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.Margin = new Thickness(0, 30, 0, 0);
        done.Click += (_, _) => Close();
        page.Children.Add(done);
        return page;
    }
}
