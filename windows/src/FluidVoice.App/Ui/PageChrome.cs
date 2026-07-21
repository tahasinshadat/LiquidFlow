using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluidVoice.Ui;

/// <summary>
/// Shared page scaffolding for the reference layout: page header row with a right-aligned
/// "Add new" pill, All/Personal tab strips with utility icons, dark hero banners, and hero
/// chips/pills. Keeps the new tabs (Dictionary, Snippets, Style, Scratchpad) consistent.
/// </summary>
public static class PageChrome
{
    /// <summary>Title (+ optional Beta chip) with an optional black action pill on the right.</summary>
    public static UIElement HeaderRow(string title, string? action, Action? onAction, bool beta = false)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };
        if (action is not null)
        {
            var pill = new Border
            {
                Background = Theme.InkBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 9, 18, 9),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = action,
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Theme.InkText),
                },
            };
            pill.MouseLeftButtonUp += (_, _) => onAction?.Invoke();
            DockPanel.SetDock(pill, Dock.Right);
            row.Children.Add(pill);
        }

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
        });
        if (beta)
        {
            var chip = Theme.Pill("Beta", Theme.InkBrush, new SolidColorBrush(Theme.InkText), 11);
            chip.Margin = new Thickness(12, 4, 0, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(chip);
        }
        row.Children.Add(left);
        return row;
    }

    /// <summary>Underlined tab strip with subtle search / sort / refresh icons on the right.</summary>
    public static UIElement TabsRow(string[] tabs, int active, Action<int>? onSelect = null)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var glyph in new[] { "", "", "" })
            icons.Children.Add(IconButton(glyph, null, null));
        DockPanel.SetDock(icons, Dock.Right);
        dock.Children.Add(icons);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            bool on = i == active;
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 26, 0), Cursor = onSelect is null ? Cursors.Arrow : Cursors.Hand };
            wrap.Children.Add(new TextBlock
            {
                Text = tabs[i],
                FontSize = 15,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = on ? Theme.TextBrush : Theme.SubtleBrush,
            });
            wrap.Children.Add(new Border
            {
                Height = 2,
                Background = on ? Theme.TextBrush : Brushes.Transparent,
                Margin = new Thickness(0, 8, 0, 0),
            });
            if (onSelect is not null) wrap.MouseLeftButtonUp += (_, _) => onSelect(idx);
            row.Children.Add(wrap);
        }
        dock.Children.Add(row);

        var host = new StackPanel();
        host.Children.Add(dock);
        host.Children.Add(Theme.Divider(-14, 0));
        return host;
    }

    /// <summary>A tab strip entry that carries a small "Beta" chip (Style → Auto cleanup).</summary>
    public static UIElement TabsRowWithBeta(string[] tabs, int betaIndex, int active, Action<int> onSelect)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            bool on = i == active;
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 24, 0), Cursor = Cursors.Hand };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock
            {
                Text = tabs[i],
                FontSize = 15,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = on ? Theme.TextBrush : Theme.SubtleBrush,
            });
            if (i == betaIndex)
            {
                var chip = Theme.Pill("Beta", Theme.GreenSoftBrush, Theme.GreenBrush, 10.5);
                chip.Margin = new Thickness(8, -2, 0, 0);
                line.Children.Add(chip);
            }
            wrap.Children.Add(line);
            wrap.Children.Add(new Border
            {
                Height = 2,
                Background = on ? Theme.TextBrush : Brushes.Transparent,
                Margin = new Thickness(0, 8, 0, 0),
            });
            wrap.MouseLeftButtonUp += (_, _) => onSelect(idx);
            row.Children.Add(wrap);
        }
        dock.Children.Add(row);
        var host = new StackPanel();
        host.Children.Add(dock);
        host.Children.Add(Theme.Divider(-14, 0));
        return host;
    }

    /// <summary>Near-black rounded hero with a soft teal glow (our stand-in for their photo heroes).</summary>
    public static UIElement DarkHero(UIElement content, double corner = 16)
    {
        var grid = new Grid { ClipToBounds = true };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(corner),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(15, 16, 18), 0),
                    new(Color.FromRgb(24, 27, 30), 0.6),
                    new(Color.FromRgb(18, 46, 46), 1),
                },
                new Point(0, 0.2), new Point(1, 1)),
        });
        grid.Children.Add(new Border
        {
            Width = 360,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(corner),
            Opacity = 0.45,
            Background = new RadialGradientBrush(Color.FromArgb(130, 74, 214, 196), Color.FromArgb(0, 74, 214, 196))
            {
                Center = new Point(0.7, 0.4), GradientOrigin = new Point(0.7, 0.4), RadiusX = 0.8, RadiusY = 1.0,
            },
        });
        grid.Children.Add(content);
        return new Border { CornerRadius = new CornerRadius(corner), Child = grid };
    }

    /// <summary>Translucent chip used inside dark heroes.</summary>
    public static Border HeroChip(string text, bool italic = false) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(12, 6, 12, 6),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = Brushes.White,
            MaxWidth = 420,
            TextTrimming = TextTrimming.CharacterEllipsis,
        },
    };

    /// <summary>Light pill button used inside dark heroes ("Start now", "Add new snippet").</summary>
    public static Border HeroPill(string label) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(235, 246, 244, 239)),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 9, 16, 9),
        HorizontalAlignment = HorizontalAlignment.Left,
        Cursor = Cursors.Hand,
        Child = new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 23)),
        },
    };

    /// <summary>Small hoverable MDL2 icon button.</summary>
    public static Border IconButton(string glyph, string? tooltip, Action? onClick)
    {
        var b = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand,
            ToolTip = tooltip,
            Margin = new Thickness(4, 0, 0, 0),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = Theme.SubtleBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Theme.SidebarSelected);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        if (onClick is not null) b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }
}
