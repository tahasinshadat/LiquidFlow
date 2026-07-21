using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluidVoice.Ui;

/// <summary>
/// Slim progress bar that actually LOOKS alive: known progress fills left-to-right,
/// unknown progress shows a sliding marquee pill. (WPF's stock ProgressBar renders
/// indeterminate as a solid fill under our theme — it read as "always 100%".)
/// </summary>
public sealed class ProgressStripe : Border
{
    private readonly Border _fill;
    private readonly TranslateTransform _slide = new();
    private readonly double _trackWidth;
    private bool _indeterminate;

    public ProgressStripe(double width = 380, double height = 8)
    {
        _trackWidth = width;
        Width = width;
        Height = height;
        CornerRadius = new CornerRadius(height / 2);
        Background = new SolidColorBrush(Theme.SidebarSelected);
        ClipToBounds = true;
        _fill = new Border
        {
            Background = Theme.GreenBrush,
            CornerRadius = new CornerRadius(height / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            RenderTransform = _slide,
        };
        Child = _fill;
    }

    /// <summary>Determinate: fill 0..1 of the track.</summary>
    public void SetFraction(double pct)
    {
        if (_indeterminate)
        {
            _indeterminate = false;
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = 0;
        }
        _fill.Width = Math.Clamp(pct, 0, 1) * _trackWidth;
    }

    /// <summary>Indeterminate: a pill sweeping across the track on repeat.</summary>
    public void SetIndeterminate()
    {
        if (_indeterminate) return;
        _indeterminate = true;
        var pill = _trackWidth * 0.28;
        _fill.Width = pill;
        var sweep = new DoubleAnimation(-pill, _trackWidth, TimeSpan.FromSeconds(1.1))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, sweep);
    }
}
