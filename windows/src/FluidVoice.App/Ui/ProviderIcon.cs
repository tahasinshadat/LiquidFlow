using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace FluidVoice.Ui;

/// <summary>
/// Small brand tiles for AI providers — a rounded square in the company's accent colour with
/// a monogram, plus a few simple vector marks for flavour. These are clean-room glyphs
/// (colour + letterform / basic geometry), not reproductions of the companies' logos.
/// </summary>
public static class ProviderIcon
{
    private sealed record Brand(Color Bg, string Mono, Color Fg, string Kind = "mono");

    private static readonly Dictionary<string, Brand> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new(Rgb(0x10, 0xA3, 0x7F), "AI", Colors.White),
        ["anthropic"] = new(Rgb(0xD9, 0x77, 0x57), "A", Colors.White),
        ["claude"] = new(Rgb(0xD9, 0x77, 0x57), "A", Colors.White),
        ["xai"] = new(Rgb(0x1A, 0x1A, 0x1A), "X", Colors.White),
        ["groq"] = new(Rgb(0xF5, 0x4F, 0x35), "G", Colors.White),
        ["cerebras"] = new(Rgb(0xF2, 0x6A, 0x2E), "C", Colors.White),
        ["google"] = new(Rgb(0x1A, 0x73, 0xE8), "", Colors.White, "gemini"),
        ["gemini"] = new(Rgb(0x1A, 0x73, 0xE8), "", Colors.White, "gemini"),
        ["openrouter"] = new(Rgb(0x64, 0x67, 0xF2), "OR", Colors.White),
        ["mistral"] = new(Rgb(0xEE, 0x79, 0x2F), "", Colors.White, "mistral"),
        ["ollama"] = new(Rgb(0x2B, 0x2B, 0x2B), "O", Colors.White),
        ["lmstudio"] = new(Rgb(0x7C, 0x4D, 0xFF), "LM", Colors.White),
        ["nvidia"] = new(Rgb(0x76, 0xB9, 0x00), "N", Colors.White),
        ["meta"] = new(Rgb(0x08, 0x66, 0xFF), "M", Colors.White),
        ["llama"] = new(Rgb(0x08, 0x66, 0xFF), "M", Colors.White),
        ["fluid-local"] = new(Rgb(0x22, 0x94, 0x8A), "", Colors.White, "wave"),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>A brand tile for the provider id. Falls back to a neutral monogram from the name.</summary>
    public static FrameworkElement For(string providerId, string displayName, double size = 30)
    {
        var brand = Brands.TryGetValue(providerId, out var b)
            ? b
            : new Brand(Rgb(0x5A, 0x5E, 0x66), Monogram(displayName), Colors.White);

        var tile = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = new SolidColorBrush(brand.Bg),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        tile.Child = brand.Kind switch
        {
            "gemini" => GeminiSpark(size),
            "mistral" => MistralGrid(size),
            "wave" => Waveform(size, brand.Fg),
            _ => new TextBlock
            {
                Text = brand.Mono,
                FontSize = size * (brand.Mono.Length > 1 ? 0.34 : 0.46),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(brand.Fg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return tile;
    }

    private static string Monogram(string name)
    {
        var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return char.ToUpperInvariant(parts[0][0]).ToString();
        return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0])).Trim();
    }

    // four-point sparkle (Gemini-ish), single colour so it reads on the blue tile
    private static UIElement GeminiSpark(double size)
    {
        double c = size / 2, r = size * 0.34;
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = true };
        double waist = r * 0.32;
        fig.Segments.Add(new LineSegment(new Point(c + waist, c - waist), true));
        fig.Segments.Add(new LineSegment(new Point(c + r, c), true));
        fig.Segments.Add(new LineSegment(new Point(c + waist, c + waist), true));
        fig.Segments.Add(new LineSegment(new Point(c, c + r), true));
        fig.Segments.Add(new LineSegment(new Point(c - waist, c + waist), true));
        fig.Segments.Add(new LineSegment(new Point(c - r, c), true));
        fig.Segments.Add(new LineSegment(new Point(c - waist, c - waist), true));
        return new Path { Data = new PathGeometry(new[] { fig }), Fill = Brushes.White };
    }

    // 2x2 warm colour-block grid (Mistral-ish palette)
    private static UIElement MistralGrid(double size)
    {
        double cell = size * 0.26, gap = size * 0.05;
        var colors = new[] { Rgb(0xFF, 0xCE, 0x3C), Rgb(0xF2, 0xA7, 0x3B), Rgb(0xEE, 0x79, 0x2F), Rgb(0xEA, 0x33, 0x26) };
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 2; i++) { g.RowDefinitions.Add(new RowDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); }
        for (int i = 0; i < 4; i++)
        {
            var sq = new Border { Width = cell, Height = cell, Background = new SolidColorBrush(colors[i]), Margin = new Thickness(gap / 2), CornerRadius = new CornerRadius(1.5) };
            Grid.SetRow(sq, i / 2); Grid.SetColumn(sq, i % 2);
            g.Children.Add(sq);
        }
        return g;
    }

    // three-bar waveform for the on-device provider (matches the app mark)
    private static UIElement Waveform(double size, Color fg)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        double[] h = { 0.30, 0.5, 0.30 };
        foreach (var frac in h)
            row.Children.Add(new Border
            {
                Width = size * 0.10,
                Height = size * frac,
                Background = new SolidColorBrush(fg),
                CornerRadius = new CornerRadius(size * 0.05),
                Margin = new Thickness(size * 0.035, 0, size * 0.035, 0),
            });
        return row;
    }
}
