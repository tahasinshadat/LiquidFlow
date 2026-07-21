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
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 30) };
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
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 24), MinHeight = 30 };
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var glyph in new[] { "", "", "" })
            icons.Children.Add(IconButton(glyph, null, null));
        DockPanel.SetDock(icons, Dock.Right);
        dock.Children.Add(icons);

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
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
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 24), MinHeight = 30 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            bool on = i == active;
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 26, 0), Cursor = Cursors.Hand };
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

    /// <summary>Near-black rounded hero with soft, blurred warm/cool color fields.</summary>
    public static UIElement DarkHero(UIElement content, double corner = 16)
    {
        var grid = new Grid { ClipToBounds = true };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(corner),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(15, 15, 16), 0),
                    new(Color.FromRgb(26, 22, 21), 0.55),
                    new(Color.FromRgb(47, 37, 28), 1),
                },
                new Point(0, 0.2), new Point(1, 1)),
        });
        grid.Children.Add(new Border
        {
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(corner),
            Opacity = 0.86,
            Background = new RadialGradientBrush(Color.FromArgb(210, 190, 123, 57), Color.FromArgb(0, 190, 123, 57))
            {
                Center = new Point(0.72, 0.44), GradientOrigin = new Point(0.72, 0.44), RadiusX = 0.65, RadiusY = 0.92,
            },
        });
        grid.Children.Add(new Border
        {
            Width = 330,
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(250, -70, 0, 0),
            Opacity = 0.7,
            Background = new RadialGradientBrush(Color.FromArgb(165, 42, 83, 116), Color.FromArgb(0, 42, 83, 116)),
        });
        grid.Children.Add(new Border
        {
            Width = 420,
            Height = 210,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -70, -85),
            Opacity = 0.72,
            Background = new RadialGradientBrush(Color.FromArgb(150, 103, 55, 37), Color.FromArgb(0, 103, 55, 37)),
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

    /// <summary>Overlapping generic colored circles + a "+" circle — the carve-out-safe
    /// stand-in for the reference's app-logo clusters (no third-party logos).</summary>
    public static UIElement IconCluster(double size = 44)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var colors = new[] { "#3AC8C6", "#7C37B1", "#5E81AC", "#D08770" };
        for (int i = 0; i < colors.Length; i++)
            row.Children.Add(new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[i])!,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(i == 0 ? 0 : -10, 0, 0, 0),
                Opacity = 0.92,
            });
        row.Children.Add(new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Theme.Hairline),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(-10, 0, 0, 0),
            Child = new TextBlock
            {
                Text = "+",
                FontSize = size * 0.42,
                Foreground = Theme.SubtleBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        });
        return row;
    }

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
