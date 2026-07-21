using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FluidVoice.Core;
using Path = System.Windows.Shapes.Path; // disambiguate from the global System.IO using

namespace FluidVoice.Ui;

/// <summary>Usage insights styled after Wispr Flow's analytics dashboard.</summary>
public sealed class HomeTab : StackPanel
{
    /// <summary>Capture-harness seam: newly built HomeTabs start on the "Your voice" tab.</summary>
    public static bool DefaultVoice;
    private bool _voice = DefaultVoice;

    public HomeTab()
    {
        Build();
        HistoryStore.HistoryChanged += () => Dispatcher.BeginInvoke(Build);
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(BuildTabs());
        if (_voice)
        {
            Children.Add(VoicePane.Build());
        }
        else
        {
            Children.Add(BuildMetricsRow());
            Children.Add(BuildLowerDashboard());
        }
    }

    private static UIElement BuildHistoryNotice()
    {
        var row = new DockPanel();
        row.Children.Add(new TextBlock
        {
            Text = "!",
            Width = 26,
            Height = 26,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 1, 12, 0),
        });

        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = Settings.Current.SaveTranscriptionHistory
                ? "History is enabled for more reliable insights"
                : "Enable history for more reliable insights",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Usage stats, app breakdowns, and voice profile estimates are calculated locally from saved dictations.",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        row.Children.Add(copy);

        return new Border
        {
            Width = 720,
            Background = Theme.CardBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 34),
            Child = row,
        };
    }

    private UIElement BuildTabs()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 34) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(TabLabel("Your usage", !_voice, () => { _voice = false; Build(); }));
        row.Children.Add(TabLabel("Your voice", _voice, () => { _voice = true; Build(); }));
        panel.Children.Add(row);
        panel.Children.Add(Theme.Divider(12, 0));
        return panel;
    }

    private static UIElement TabLabel(string text, bool active, Action onClick)
    {
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 28, 0), Cursor = System.Windows.Input.Cursors.Hand };
        wrap.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = active ? Theme.TextBrush : Theme.SubtleBrush,
        });
        wrap.Children.Add(new Border
        {
            Height = 2,
            Background = active ? Theme.TextBrush : Brushes.Transparent,
            Margin = new Thickness(0, 20, 0, -13),
        });
        wrap.MouseLeftButtonUp += (_, _) => onClick();
        return wrap;
    }

    private static UIElement BuildMetricsRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var gauge = BuildWpmCard();
        Grid.SetColumn(gauge, 0);
        grid.Children.Add(gauge);

        var fixes = BuildFixesCard();
        Grid.SetColumn(fixes, 2);
        grid.Children.Add(fixes);

        var totals = BuildTotalWordsCard();
        Grid.SetColumn(totals, 4);
        grid.Children.Add(totals);
        return grid;
    }

    private static UIElement BuildWpmCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(StatNumber(Settings.Current.UserTypingWPM.ToString()));
        panel.Children.Add(CapsWithInfo("Words per minute"));

        var canvas = new Canvas { Width = 210, Height = 122, Margin = new Thickness(0, 14, 0, 0) };
        canvas.Children.Add(new Path
        {
            Data = ArcGeometry(1),
            Stroke = new SolidColorBrush(Theme.SidebarSelected),
            StrokeThickness = 18,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        canvas.Children.Add(new Path
        {
            Data = ArcGeometry(Math.Clamp(Settings.Current.UserTypingWPM / 140.0, 0.25, 1.0)),
            Stroke = Theme.GreenBrush,
            StrokeThickness = 18,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        canvas.Children.Add(new TextBlock
        {
            Text = "Top",
            FontSize = 17,
            Foreground = Theme.SubtleBrush,
            Width = 210,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0),
        });
        canvas.Children.Add(new TextBlock
        {
            Text = $"{Math.Max(1, 100 - Settings.Current.UserTypingWPM)}%",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Width = 210,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 84, 0, 0),
        });
        panel.Children.Add(canvas);
        return DashboardCard(panel);
    }

    private static UIElement BuildFixesCard()
    {
        var aiFixes = HistoryStore.Entries.Count(e => e.WasAIProcessed);
        var dictionaryFixes = Settings.Current.CustomDictionaryEntries.Count;
        var panel = new StackPanel();
        panel.Children.Add(StatNumber((aiFixes + dictionaryFixes).ToString("N0")));
        panel.Children.Add(LabelCaps("Fixes made by LiquidFlow"));
        panel.Children.Add(Theme.Divider(22, 18));
        panel.Children.Add(MiniStatBold(aiFixes.ToString("N0"), "AI-enhanced dictations"));
        panel.Children.Add(MiniStatBold(dictionaryFixes.ToString("N0"), "dictionary entries"));
        return DashboardCard(panel);
    }

    private static UIElement BuildTotalWordsCard()
    {
        var panel = new StackPanel();
        var header = new DockPanel { LastChildFill = false };
        var left = new StackPanel();
        left.Children.Add(StatNumber(HistoryStore.TotalWords.ToString("N0")));
        left.Children.Add(LabelCaps("Total words dictated"));
        header.Children.Add(left);
        var monthWords = HistoryStore.DailyWordCounts(30).Sum(d => d.Words);
        // Soft on-brand badge (was a plain white pill that floated oddly in the corner).
        var badge = Theme.Pill($"{monthWords:N0} this month", Theme.GreenSoftBrush, Theme.GreenBrush, 11.5);
        badge.VerticalAlignment = VerticalAlignment.Top;
        badge.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        panel.Children.Add(header);
        panel.Children.Add(Theme.Divider(24, 20));

        var row = new DockPanel { LastChildFill = false };
        var source = new StackPanel();
        source.Children.Add(new TextBlock
        {
            Text = "Desktop",
            FontSize = 16,
            Foreground = Theme.TextBrush,
        });
        source.Children.Add(new TextBlock
        {
            Text = $"{HistoryStore.TotalWords:N0} words",
            FontSize = 15,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 6, 0, 0),
        });
        row.Children.Add(source);
        panel.Children.Add(row);
        return DashboardCard(panel);
    }

    private static UIElement BuildLowerDashboard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var desktop = BuildDesktopUsageCard();
        Grid.SetColumn(desktop, 0);
        grid.Children.Add(desktop);

        var streak = BuildStreakCard();
        Grid.SetColumn(streak, 2);
        grid.Children.Add(streak);
        return grid;
    }

    private static UIElement BuildDesktopUsageCard()
    {
        var panel = new StackPanel();
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 24) };
        header.Children.Add(new TextBlock
        {
            Text = "Desktop usage",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        var appCount = HistoryStore.TopApps(1000).Count;
        var total = new TextBlock
        {
            Text = $"TOTAL APPS USED | {appCount}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(total, Dock.Right);
        header.Children.Add(total);
        panel.Children.Add(header);

        var apps = HistoryStore.TopApps(6);
        if (apps.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Start dictating to populate app usage.",
                FontSize = 14,
                Foreground = Theme.SubtleBrush,
            });
        }
        else
        {
            var max = Math.Max(1, apps.Max(a => a.Count));
            foreach (var (app, count) in apps)
                panel.Children.Add(UsageRow(app, count, count / (double)max));
        }

        return DashboardCard(panel);
    }

    private static UIElement BuildStreakCard()
    {
        var panel = new StackPanel();
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 24) };
        header.Children.Add(new TextBlock
        {
            Text = $"{HistoryStore.CurrentStreakDays} day streak",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        var longest = Math.Max(HistoryStore.CurrentStreakDays, LongestStreak());
        var longestText = new TextBlock
        {
            Text = $"LONGEST STREAK | {longest} DAYS",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(longestText, Dock.Right);
        header.Children.Add(longestText);
        panel.Children.Add(header);
        panel.Children.Add(BuildMonthsRow());
        panel.Children.Add(BuildHeatmap());
        panel.Children.Add(BuildLegend());
        return DashboardCard(panel);
    }

    private static UIElement BuildHeatmap()
    {
        var days = HistoryStore.DailyWordCounts(70);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (int i = 0; i < 10; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        for (int i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        var labels = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        for (int row = 0; row < labels.Length; row++)
        {
            var label = new TextBlock
            {
                Text = labels[row],
                FontSize = 11,
                Foreground = Theme.SubtleBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
        }

        for (int i = 0; i < days.Count; i++)
        {
            var (date, words) = days[i];
            var cell = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(UsageColor(words)),
                ToolTip = $"{date:MMM d}: {words:N0} words",
            };
            Grid.SetColumn(cell, i / 7 + 1);
            Grid.SetRow(cell, (int)date.DayOfWeek);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static UIElement UsageRow(string app, int count, double ratio)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var glyph = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.SidebarSelected),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = app.Length > 0 ? app[..1].ToUpperInvariant() : "•",
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(glyph, 0);
        row.Children.Add(glyph);

        // the teal bar's own length encodes the share — no gray track behind it
        var barArea = new Grid { Height = 30, Margin = new Thickness(0, 0, 16, 0) };
        var clamped = Math.Clamp(ratio, 0.08, 1.0);
        barArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clamped, GridUnitType.Star) });
        barArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - clamped), GridUnitType.Star) });
        var fill = new Border
        {
            Background = Theme.GreenBrush,
            CornerRadius = new CornerRadius(6),
            MinWidth = 44,
            Child = new TextBlock
            {
                Text = $"{Math.Round(ratio * 100):0}%",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(fill, 0);
        barArea.Children.Add(fill);
        Grid.SetColumn(barArea, 1);
        row.Children.Add(barArea);

        var label = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.Inlines.Add(new System.Windows.Documents.Run($"{count:N0} "));
        label.Inlines.Add(new System.Windows.Documents.Run(app.ToUpperInvariant()));
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        return row;
    }

    private static Border DashboardCard(UIElement child) => Theme.Panel(child, new Thickness(22), new Thickness(0));

    private static TextBlock StatNumber(string text) => new()
    {
        Text = text,
        FontSize = 30,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.TextBrush,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static TextBlock LabelCaps(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.SubtleBrush,
    };

    private static TextBlock MiniStat(string text) => new()
    {
        Text = text,
        FontSize = 15,
        Foreground = Theme.TextBrush,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private static Geometry ArcGeometry(double progress)
    {
        const double cx = 105;
        const double cy = 112;
        const double radius = 88;
        var start = new Point(cx - radius, cy);
        var angle = Math.PI + Math.PI * Math.Clamp(progress, 0, 1);
        var end = new Point(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
        });
        return new PathGeometry(new[] { figure });
    }

    private static UIElement BuildMonthsRow()
    {
        var days = HistoryStore.DailyWordCounts(70);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (int i = 0; i < 10; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string prev = "";
        for (int col = 0; col < 10 && col * 7 < days.Count; col++)
        {
            var monthName = days[col * 7].Date.ToString("MMM");
            if (monthName == prev) continue;
            prev = monthName;
            var label = new TextBlock { Text = monthName, FontSize = 11.5, Foreground = Theme.SubtleBrush };
            Grid.SetColumn(label, col + 1);
            grid.Children.Add(label);
        }

        var pagers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var g in new[] { "\uE76B", "\uE76C" }) // chevrons
            pagers.Children.Add(new TextBlock
            {
                Text = g,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(8, 2, 0, 0),
            });
        Grid.SetColumn(pagers, 11);
        grid.Children.Add(pagers);
        return grid;
    }

    private static UIElement BuildLegend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(34, 14, 0, 0) };
        row.Children.Add(new TextBlock { Text = "More", FontSize = 11.5, Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        foreach (var c in new[] { Theme.Green, Theme.Teal2, Color.FromRgb(111, 196, 187), Theme.SidebarSelected })
            row.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 5, 0) });
        row.Children.Add(new TextBlock { Text = "Less", FontSize = 11.5, Foreground = Theme.SubtleBrush, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static UIElement CapsWithInfo(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LabelCaps(text));
        row.Children.Add(new TextBlock
        {
            Text = "\uE946",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(6, 1, 0, 0),
        });
        return row;
    }

    private static UIElement MiniStatBold(string number, string rest)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var text = new TextBlock { FontSize = 15, Foreground = Theme.TextBrush };
        text.Inlines.Add(new System.Windows.Documents.Run(number) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.Run(" " + rest));
        row.Children.Add(text);
        row.Children.Add(new TextBlock
        {
            Text = "\uE946",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10.5,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(6, 3, 0, 0),
        });
        return row;
    }

    private static Color UsageColor(int words) => words switch
    {
        <= 0 => Theme.SidebarSelected,
        < 80 => Color.FromRgb(199, 236, 232),
        < 250 => Color.FromRgb(111, 196, 187),
        < 700 => Theme.Teal2,
        _ => Theme.Green,
    };

    private static int LongestStreak()
    {
        var days = HistoryStore.Entries.Select(e => e.Timestamp.Date).Distinct().OrderBy(d => d).ToList();
        var best = 0;
        var current = 0;
        DateTime? previous = null;
        foreach (var day in days)
        {
            current = previous is not null && day == previous.Value.AddDays(1) ? current + 1 : 1;
            best = Math.Max(best, current);
            previous = day;
        }
        return best;
    }
}
