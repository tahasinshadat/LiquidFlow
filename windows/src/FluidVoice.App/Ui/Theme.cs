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

    public static Color Bg => IsDark ? Color.FromRgb(24, 24, 27) : Color.FromRgb(245, 245, 247);
    public static Color Card => IsDark ? Color.FromRgb(32, 32, 36) : Color.FromRgb(255, 255, 255);
    public static Color CardBorder => IsDark ? Color.FromRgb(52, 52, 58) : Color.FromRgb(222, 222, 226);
    public static Color Text => IsDark ? Color.FromRgb(240, 240, 244) : Color.FromRgb(24, 24, 27);
    public static Color SubtleText => IsDark ? Color.FromRgb(160, 160, 168) : Color.FromRgb(110, 110, 118);
    public static Color Field => IsDark ? Color.FromRgb(39, 39, 42) : Color.FromRgb(250, 250, 252);
    public static Color Accent => (Color)ColorConverter.ConvertFromString(Settings.Current.AccentColor);

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
