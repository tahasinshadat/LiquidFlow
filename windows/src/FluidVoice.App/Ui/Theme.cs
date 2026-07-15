using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>Adaptive light/dark palette + shared control factory (AppTheme.swift).</summary>
public static class Theme
{
    public static bool IsDark => Settings.Current.Theme switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => SystemUsesDarkMode(),
    };

    // Light: warm cream canvas + white sheet (Wispr Flow).
    // Dark: layered near-black with clear elevation steps (canvas < sheet < card < field)
    // and a faint cool tint so surfaces read as "designed", not flat gray.
    public static Color Bg => IsDark ? Color.FromRgb(17, 18, 21) : Color.FromRgb(244, 242, 237);
    public static Color Surface => IsDark ? Color.FromRgb(26, 27, 32) : Color.FromRgb(255, 255, 255);
    public static Color Sidebar => IsDark ? Color.FromRgb(17, 18, 21) : Color.FromRgb(244, 242, 237);
    public static Color SidebarSelected => IsDark ? Color.FromRgb(45, 47, 54) : Color.FromRgb(235, 232, 224);
    public static Color Card => IsDark ? Color.FromRgb(32, 34, 39) : Color.FromRgb(248, 247, 244);
    public static Color CardInner => IsDark ? Color.FromRgb(38, 40, 46) : Color.FromRgb(255, 255, 255);
    public static Color CardBorder => IsDark ? Color.FromRgb(46, 48, 55) : Color.FromRgb(231, 227, 218);
    public static Color Text => IsDark ? Color.FromRgb(236, 237, 241) : Color.FromRgb(31, 31, 29);
    public static Color SubtleText => IsDark ? Color.FromRgb(146, 149, 158) : Color.FromRgb(113, 110, 104);
    public static Color Field => IsDark ? Color.FromRgb(38, 40, 46) : Color.FromRgb(255, 255, 255);
    public static Color Ink => IsDark ? Color.FromRgb(236, 237, 241) : Color.FromRgb(24, 24, 23);
    public static Color InkText => IsDark ? Color.FromRgb(20, 21, 24) : Color.FromRgb(255, 255, 255);
    public static Color Hairline => IsDark ? Color.FromRgb(42, 44, 51) : Color.FromRgb(235, 231, 222);
    public static Color Green => IsDark ? Color.FromRgb(58, 178, 162) : Color.FromRgb(31, 112, 105);
    public static Color GreenSoft => IsDark ? Color.FromRgb(30, 58, 55) : Color.FromRgb(219, 239, 235);
    public static Color Teal2 => IsDark ? Color.FromRgb(80, 184, 170) : Color.FromRgb(67, 160, 149);
    public static Color Purple => IsDark ? Color.FromRgb(167, 116, 214) : Color.FromRgb(124, 55, 177);
    public static Color Warning => IsDark ? Color.FromRgb(206, 158, 88) : Color.FromRgb(159, 112, 43);
    public static Color Danger => IsDark ? Color.FromRgb(224, 108, 108) : Color.FromRgb(184, 67, 67);
    public static Color Accent => IsDark ? Color.FromRgb(58, 178, 162) : Color.FromRgb(31, 122, 106);
    public static SolidColorBrush GreenBrush => new(Green);
    public static SolidColorBrush GreenSoftBrush => new(GreenSoft);
    public static SolidColorBrush SurfaceBrush => new(Surface);
    public static SolidColorBrush InkBrush => new(Ink);
    public static SolidColorBrush HairlineBrush => new(Hairline);

    /// <summary>Serif family for the big stat numbers / display headings.</summary>
    public static FontFamily StatSerif { get; } = new("Georgia, 'Times New Roman', serif");
    public static FontFamily DisplaySerif { get; } = new("Georgia, 'Times New Roman', serif");

    /// <summary>The user-selectable UI font (Preferences → Appearance). Applied window-wide.</summary>
    public static FontFamily UiFont => new(FontChoice.Resolve(Settings.Current.AppFont));

    /// <summary>User-selectable content zoom, clamped so a bad settings value can't wreck layout.</summary>
    public static double UiScale => Math.Clamp(Settings.Current.UiScale <= 0 ? 0.9 : Settings.Current.UiScale, 0.8, 1.25);

    /// <summary>LayoutTransform for page bodies honoring the text-size setting.</summary>
    public static Transform PageScale() =>
        Math.Abs(UiScale - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(UiScale, UiScale);

    public static SolidColorBrush BgBrush => new(Bg);
    public static SolidColorBrush CardBrush => new(Card);
    public static SolidColorBrush TextBrush => new(Text);
    public static SolidColorBrush SubtleBrush => new(SubtleText);
    public static SolidColorBrush AccentBrush => new(Accent);
    public static SolidColorBrush PurpleBrush => new(Purple);

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold,
        Foreground = TextBrush, Margin = new Thickness(0, 4, 0, 10),
    };

    public static TextBlock PageTitle(string text, bool serif = false) => new()
    {
        Text = text,
        FontSize = serif ? 29 : 24,
        FontWeight = serif ? FontWeights.Normal : FontWeights.SemiBold,
        FontFamily = serif ? DisplaySerif : new FontFamily("Segoe UI Variable Display, Segoe UI"),
        Foreground = TextBrush,
        Margin = new Thickness(0, 0, 0, 22),
    };

    public static TextBlock Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = SubtleBrush,
        Margin = new Thickness(0, 0, 0, 8),
    };

    public static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = TextBrush, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text, Foreground = SubtleBrush, FontSize = 11,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBlock Body(string text, double size = 13.5) => new()
    {
        Text = text,
        Foreground = SubtleBrush,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size + 6,
        Margin = new Thickness(0, 0, 0, 10),
    };

    public static Border Card2(UIElement child) => new()
    {
        Background = CardBrush,
        BorderBrush = new SolidColorBrush(CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(20),
        Margin = new Thickness(0, 0, 0, 12),
        Child = child,
    };

    public static Border Panel(UIElement child, Thickness? padding = null, Thickness? margin = null) => new()
    {
        Background = CardBrush,
        BorderBrush = new SolidColorBrush(CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(24),
        Margin = margin ?? new Thickness(0, 0, 0, 18),
        Child = child,
    };

    public static Border Divider(double top = 0, double bottom = 0) => new()
    {
        Height = 1,
        Background = HairlineBrush,
        Margin = new Thickness(0, top, 0, bottom),
    };

    public static Border Pill(string text, Brush? background = null, Brush? foreground = null, double fontSize = 12) => new()
    {
        Background = background ?? new SolidColorBrush(SidebarSelected),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(10, 5, 10, 5),
        Child = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground ?? TextBrush,
        },
    };

    public static Button PrimaryButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(16, 8, 16, 8),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = InkBrush,
        Foreground = new SolidColorBrush(InkText),
        BorderBrush = InkBrush,
        BorderThickness = new Thickness(0),
    };

    public static Button SecondaryButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(16, 8, 16, 8),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = new SolidColorBrush(SidebarSelected),
        Foreground = TextBrush,
        BorderBrush = new SolidColorBrush(SidebarSelected),
    };

    public static TextBlock Glyph(string glyph, double size = 15, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = brush ?? TextBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// iOS-style segmented control (a pill with a sliding selected segment). Cleaner than a
    /// dropdown for a small fixed set of choices like the activation mode.
    /// </summary>
    public static UIElement Segmented(IReadOnlyList<string> options, int selected, Action<int> onSelect, double maxWidth = 360)
    {
        var track = new Border
        {
            Background = new SolidColorBrush(SidebarSelected),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = maxWidth,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var grid = new Grid();
        var segs = new List<Border>();
        for (int i = 0; i < options.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int idx = i;
            bool on = i == selected;
            var seg = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = on ? SurfaceBrush : Brushes.Transparent,
                Padding = new Thickness(16, 7, 16, 7),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = options[i],
                    FontSize = 13,
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = on ? TextBrush : SubtleBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                },
            };
            if (on)
                seg.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.14, Color = Colors.Black };
            seg.MouseLeftButtonUp += (_, _) => onSelect(idx);
            Grid.SetColumn(seg, i);
            grid.Children.Add(seg);
            segs.Add(seg);
        }
        track.Child = grid;
        return track;
    }

    /// <summary>
    /// A labelled slider with a live value readout. <paramref name="onChange"/> is debounced
    /// (~180ms after the last move) so dragging doesn't hammer Settings.Save(); the readout
    /// still updates in real time. <paramref name="format"/> renders the value (e.g. "72 px").
    /// </summary>
    public static UIElement Slider(double min, double max, double value, Action<double> onChange,
        Func<double, string>? format = null, double width = 300)
    {
        format ??= v => ((int)Math.Round(v)).ToString();

        var row = new Grid { Margin = new Thickness(0, 2, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new System.Windows.Controls.Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = width,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = AccentBrush,
            IsMoveToPointEnabled = true,
            SmallChange = Math.Max(1, (max - min) / 100),
            LargeChange = Math.Max(1, (max - min) / 10),
        };
        var readout = new TextBlock
        {
            Foreground = SubtleBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            MinWidth = 52,
            Text = format(slider.Value),
        };

        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        double pending = slider.Value;
        debounce.Tick += (_, _) => { debounce.Stop(); onChange(pending); };
        slider.ValueChanged += (_, e) =>
        {
            pending = e.NewValue;
            readout.Text = format(e.NewValue);
            debounce.Stop();
            debounce.Start();
        };

        Grid.SetColumn(slider, 0);
        Grid.SetColumn(readout, 1);
        row.Children.Add(slider);
        row.Children.Add(readout);
        return row;
    }

    public static CheckBox Toggle(string label, bool value, Action<bool> onChange)
    {
        var cb = new CheckBox
        {
            Content = label, IsChecked = value, Foreground = TextBrush,
            Margin = new Thickness(0, 4, 0, 4), FontSize = 13,
        };
        cb.Checked += (_, _) => onChange(true);
        cb.Unchecked += (_, _) => onChange(false);
        return cb;
    }

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 0;
        }
        catch { return true; }
    }
}
