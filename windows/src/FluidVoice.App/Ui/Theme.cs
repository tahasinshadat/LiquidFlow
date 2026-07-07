using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    // Wispr Flow-style palette: warm canvas, white sheet, charcoal ink,
    // muted teal analytics, and one purple accent for voice/profile progress.
    public static Color Bg => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(244, 242, 237);
    public static Color Surface => IsDark ? Color.FromRgb(29, 30, 34) : Color.FromRgb(255, 255, 255);
    public static Color Sidebar => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(244, 242, 237);
    public static Color SidebarSelected => IsDark ? Color.FromRgb(58, 60, 66) : Color.FromRgb(235, 232, 224);
    public static Color Card => IsDark ? Color.FromRgb(35, 37, 41) : Color.FromRgb(248, 247, 244);
    public static Color CardInner => IsDark ? Color.FromRgb(42, 44, 49) : Color.FromRgb(255, 255, 255);
    public static Color CardBorder => IsDark ? Color.FromRgb(51, 54, 59) : Color.FromRgb(231, 227, 218);
    public static Color Text => IsDark ? Color.FromRgb(240, 240, 244) : Color.FromRgb(31, 31, 29);
    public static Color SubtleText => IsDark ? Color.FromRgb(155, 160, 166) : Color.FromRgb(113, 110, 104);
    public static Color Field => IsDark ? Color.FromRgb(44, 46, 51) : Color.FromRgb(255, 255, 255);
    public static Color Ink => IsDark ? Color.FromRgb(235, 235, 238) : Color.FromRgb(24, 24, 23);
    public static Color InkText => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(255, 255, 255);
    public static Color Hairline => IsDark ? Color.FromRgb(48, 50, 55) : Color.FromRgb(235, 231, 222);
    public static Color Green => Color.FromRgb(31, 112, 105);
    public static Color GreenSoft => IsDark ? Color.FromRgb(37, 64, 61) : Color.FromRgb(219, 239, 235);
    public static Color Teal2 => Color.FromRgb(67, 160, 149);
    public static Color Purple => Color.FromRgb(124, 55, 177);
    public static Color Warning => Color.FromRgb(159, 112, 43);
    public static Color Danger => Color.FromRgb(184, 67, 67);
    public static Color Accent => IsDark
        ? (Color)ColorConverter.ConvertFromString(Settings.Current.AccentColor)
        : Color.FromRgb(31, 122, 106);
    public static SolidColorBrush GreenBrush => new(Green);
    public static SolidColorBrush GreenSoftBrush => new(GreenSoft);
    public static SolidColorBrush SurfaceBrush => new(Surface);
    public static SolidColorBrush InkBrush => new(Ink);
    public static SolidColorBrush HairlineBrush => new(Hairline);

    /// <summary>Serif family for the big stat numbers (Wispr uses a serif there).</summary>
    public static FontFamily StatSerif { get; } = new("Georgia, 'Times New Roman', serif");
    public static FontFamily DisplaySerif { get; } = new("Georgia, 'Times New Roman', serif");

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
