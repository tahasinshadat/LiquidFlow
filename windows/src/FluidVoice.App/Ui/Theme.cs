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

    // Wispr Flow-style palette: warm cream canvas, white surface, charcoal ink,
    // muted teal accent. Dark variant kept for users who prefer it.
    public static Color Bg => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(243, 241, 236);        // cream canvas
    public static Color Surface => IsDark ? Color.FromRgb(28, 29, 33) : Color.FromRgb(255, 255, 255);   // big inset sheet
    public static Color Sidebar => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(243, 241, 236);   // rail = canvas
    public static Color SidebarSelected => IsDark ? Color.FromRgb(58, 60, 66) : Color.FromRgb(230, 227, 220);
    public static Color Card => IsDark ? Color.FromRgb(35, 37, 41) : Color.FromRgb(250, 249, 246);
    public static Color CardInner => IsDark ? Color.FromRgb(42, 44, 49) : Color.FromRgb(255, 255, 255);
    public static Color CardBorder => IsDark ? Color.FromRgb(51, 54, 59) : Color.FromRgb(233, 230, 223);
    public static Color Text => IsDark ? Color.FromRgb(240, 240, 244) : Color.FromRgb(33, 33, 31);
    public static Color SubtleText => IsDark ? Color.FromRgb(155, 160, 166) : Color.FromRgb(122, 120, 114);
    public static Color Field => IsDark ? Color.FromRgb(44, 46, 51) : Color.FromRgb(255, 255, 255);
    public static Color Ink => IsDark ? Color.FromRgb(235, 235, 238) : Color.FromRgb(38, 38, 36);       // dark pill buttons
    public static Color InkText => IsDark ? Color.FromRgb(23, 24, 27) : Color.FromRgb(250, 249, 246);
    public static Color Hairline => IsDark ? Color.FromRgb(48, 50, 55) : Color.FromRgb(238, 236, 230);
    public static Color Green => Color.FromRgb(31, 122, 106); // wispr teal-green
    public static Color Accent => IsDark
        ? (Color)ColorConverter.ConvertFromString(Settings.Current.AccentColor)
        : Color.FromRgb(31, 122, 106);
    public static SolidColorBrush GreenBrush => new(Green);
    public static SolidColorBrush SurfaceBrush => new(Surface);
    public static SolidColorBrush InkBrush => new(Ink);
    public static SolidColorBrush HairlineBrush => new(Hairline);

    /// <summary>Serif family for the big stat numbers (Wispr uses a serif there).</summary>
    public static FontFamily StatSerif { get; } = new("Georgia, 'Times New Roman', serif");

    public static SolidColorBrush BgBrush => new(Bg);
    public static SolidColorBrush CardBrush => new(Card);
    public static SolidColorBrush TextBrush => new(Text);
    public static SolidColorBrush SubtleBrush => new(SubtleText);
    public static SolidColorBrush AccentBrush => new(Accent);

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold,
        Foreground = TextBrush, Margin = new Thickness(0, 4, 0, 8),
    };

    public static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = TextBrush, FontSize = 13, Margin = new Thickness(0, 0, 0, 2),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text, Foreground = SubtleBrush, FontSize = 11,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
    };

    public static Border Card2(UIElement child) => new()
    {
        Background = CardBrush,
        BorderBrush = new SolidColorBrush(CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 12),
        Child = child,
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
