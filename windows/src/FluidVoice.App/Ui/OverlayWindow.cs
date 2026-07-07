using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FluidVoice.Core;
using FluidVoice.Input;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace FluidVoice.Ui;

public enum OverlayState { Hidden, Recording, Processing, Error }

/// <summary>
/// Live transcription overlay — Windows port of the mac bottom overlay
/// (BottomOverlayView.swift). Sizes/bars/colors/timings from the spec:
/// pill 100x46 r23 8 bars • small 300x124 r14 7 bars • medium 380x156 r18 9 bars •
/// large 600x288 r24 11 bars; black background, mode-tinted waveform (dictation
/// white@0.85, write blue, command red), fade-in 50ms / fade-out 20ms, bottom-center
/// of the screen containing the pointer, offset 50px (configurable).
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly Canvas _barsCanvas = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _previewText = new();
    private readonly Border _root;
    private readonly List<Rectangle> _bars = new();
    private readonly DispatcherTimer _animTimer;
    private readonly Random _rng = new();

    private OverlayState _state = OverlayState.Hidden;
    private RecordingMode _mode = RecordingMode.Dictation;
    private float _level;
    private double[] _barHeights = Array.Empty<double>();
    private double _shimmerPhase;

    private record Layout(double W, double H, double Corner, int Bars, double BarW, double Gap,
        double WaveH, double FontSize, bool ShowPreview, bool ShowStatus);

    private Layout _layout = LayoutFor(Core.OverlaySize.Medium);

    private static Layout LayoutFor(OverlaySize size) => size switch
    {
        Core.OverlaySize.Pill => new(100, 46, 23, 8, 3.0, 2.5, 30, 10, false, false),
        Core.OverlaySize.Small => new(300, 124, 14, 7, 3.0, 3.5, 20, 11, true, true),
        Core.OverlaySize.Medium => new(380, 156, 18, 9, 3.5, 4.5, 32, 13, true, true),
        Core.OverlaySize.Large => new(600, 288, 24, 11, 5.0, 6.0, 48, 15, true, true),
        _ => new(380, 156, 18, 9, 3.5, 4.5, 32, 13, true, true),
    };

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Opacity = 0;

        _statusText.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        _statusText.FontWeight = FontWeights.Medium;
        _statusText.HorizontalAlignment = HorizontalAlignment.Center;

        _previewText.Foreground = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)); // white@0.75
        _previewText.TextWrapping = TextWrapping.Wrap;
        _previewText.TextTrimming = TextTrimming.None;
        _previewText.FontWeight = FontWeights.Medium;
        _previewText.VerticalAlignment = VerticalAlignment.Bottom;

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_statusText, 0);
        Grid.SetRow(_barsCanvas, 1);
        Grid.SetRow(_previewText, 2);
        _barsCanvas.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(_statusText);
        stack.Children.Add(_barsCanvas);
        stack.Children.Add(_previewText);

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(38, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        _root.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = 0.32, Color = Colors.Black,
        };
        Content = _root;

        _animTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) }; // 30fps
        _animTimer.Tick += (_, _) => AnimateTick();

        SourceInitialized += (_, _) => MakeNonActivating();
        Settings.Changed += _ => Dispatcher.BeginInvoke(ApplyLayout);
        ApplyLayout();
    }

    private void MakeNonActivating()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void ApplyLayout()
    {
        _layout = LayoutFor(Settings.Current.OverlaySize);
        Width = _layout.W;
        Height = _layout.H;
        _root.CornerRadius = new CornerRadius(_layout.Corner);
        _root.Padding = Settings.Current.OverlaySize == Core.OverlaySize.Pill
            ? new Thickness(12, 8, 12, 8)
            : new Thickness(18, 12, 18, 12);
        _statusText.FontSize = _layout.FontSize + 1;
        _statusText.Visibility = _layout.ShowStatus ? Visibility.Visible : Visibility.Collapsed;
        _previewText.FontSize = _layout.FontSize;
        _previewText.Visibility = _layout.ShowPreview ? Visibility.Visible : Visibility.Collapsed;

        _barsCanvas.Children.Clear();
        _bars.Clear();
        double totalW = _layout.Bars * _layout.BarW + (_layout.Bars - 1) * _layout.Gap;
        _barsCanvas.Width = totalW;
        _barsCanvas.Height = _layout.WaveH;
        _barHeights = new double[_layout.Bars];
        for (int i = 0; i < _layout.Bars; i++)
        {
            var bar = new Rectangle
            {
                Width = _layout.BarW,
                Height = 3,
                RadiusX = _layout.BarW / 2,
                RadiusY = _layout.BarW / 2,
                Fill = ModeBrush(),
            };
            Canvas.SetLeft(bar, i * (_layout.BarW + _layout.Gap));
            Canvas.SetTop(bar, (_layout.WaveH - 3) / 2);
            _barsCanvas.Children.Add(bar);
            _bars.Add(bar);
        }
    }

    private SolidColorBrush ModeBrush() => _state switch
    {
        OverlayState.Processing => new SolidColorBrush(WithOpacity(ModeColor(), 0.16)),
        _ => new SolidColorBrush(ModeColor()),
    };

    private Color ModeColor() => _mode switch
    {
        // NotchContentViews.swift:253-269
        RecordingMode.Dictation => Color.FromArgb(217, 255, 255, 255),          // white @ 0.85
        RecordingMode.PromptMode => Color.FromArgb(255, 102, 153, 255),         // 0.4,0.6,1.0
        RecordingMode.Rewrite => Color.FromArgb(255, 115, 140, 255),            // 0.45,0.55,1.0
        RecordingMode.Command => Color.FromArgb(255, 255, 89, 89),              // 1.0,0.35,0.35
        _ => Color.FromArgb(217, 255, 255, 255),
    };

    private static Color WithOpacity(Color c, double opacity) =>
        Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);

    // ---- public control (must be called on UI thread) ----

    public void ShowRecording(RecordingMode mode)
    {
        _mode = mode;
        _state = OverlayState.Recording;
        _previewText.Text = "";
        UpdateStatusLabel();
        RefreshBarBrushes();
        PositionOnActiveScreen();
        if (!IsVisible) Show();
        _animTimer.Start();
        FadeTo(1.0, TimeSpan.FromMilliseconds(50)); // presentation 50ms
    }

    public void ShowProcessing(string status)
    {
        _state = OverlayState.Processing;
        _statusText.Text = status;
        RefreshBarBrushes();
        if (!IsVisible)
        {
            PositionOnActiveScreen();
            Show();
            _animTimer.Start();
            FadeTo(1.0, TimeSpan.FromMilliseconds(50));
        }
    }

    public void SetMode(RecordingMode mode)
    {
        _mode = mode;
        UpdateStatusLabel();
        RefreshBarBrushes();
    }

    public void SetLevel(float level) => _level = level;

    public void SetPreviewText(string text)
    {
        var cap = Math.Clamp(Settings.Current.TranscriptionPreviewCharLimit, 50, 800);
        _previewText.Text = text.Length > cap ? text[^cap..] : text; // keep the tail (newest)
    }

    public void HideOverlay()
    {
        _state = OverlayState.Hidden;
        _animTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(20)); // dismissal 20ms
        anim.Completed += (_, _) => { if (_state == OverlayState.Hidden) Hide(); };
        BeginAnimation(OpacityProperty, anim);
    }

    private void UpdateStatusLabel()
    {
        _statusText.Text = _state == OverlayState.Processing
            ? _statusText.Text
            : _mode switch
            {
                RecordingMode.Command => "Command",
                RecordingMode.Rewrite => "Edit",
                RecordingMode.PromptMode => "Prompt",
                _ => "Dictation",
            };
    }

    private void RefreshBarBrushes()
    {
        var brush = ModeBrush();
        foreach (var bar in _bars) bar.Fill = brush;
    }

    private void FadeTo(double target, TimeSpan duration)
        => BeginAnimation(OpacityProperty, new DoubleAnimation(target, duration));

    private void AnimateTick()
    {
        const double minH = 3, maxH = 15;
        if (_state == OverlayState.Recording)
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                // per-bar random walk scaled by mic level (mac bars: 3..15px, 0.1s ease)
                double target = minH + (maxH - minH) * _level * (0.45 + 0.55 * _rng.NextDouble());
                _barHeights[i] += (target - _barHeights[i]) * 0.45;
                var h = Math.Max(minH, Math.Min(maxH, _barHeights[i]));
                _bars[i].Height = h;
                Canvas.SetTop(_bars[i], (_layout.WaveH - h) / 2);
            }
        }
        else if (_state == OverlayState.Processing)
        {
            // flatten + shimmer sweep (1.05s loop)
            _shimmerPhase += 0.033 / 1.05;
            if (_shimmerPhase > 1.3) _shimmerPhase = -0.3;
            for (int i = 0; i < _bars.Count; i++)
            {
                _bars[i].Height = minH;
                Canvas.SetTop(_bars[i], (_layout.WaveH - minH) / 2);
                double pos = _bars.Count <= 1 ? 0 : (double)i / (_bars.Count - 1);
                double d = Math.Abs(pos - _shimmerPhase);
                double glow = Math.Max(0, 1 - d * 3.5);
                var baseColor = ModeColor();
                _bars[i].Fill = new SolidColorBrush(WithOpacity(baseColor, 0.16 + 0.74 * glow));
            }
        }
    }

    private void PositionOnActiveScreen()
    {
        // screen containing the mouse pointer (OverlayScreenResolver.swift)
        var pos = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(pos);
        var wa = screen.WorkingArea;

        var source = PresentationSource.FromVisual(this);
        double dpiX = 1.0, dpiY = 1.0;
        if (source?.CompositionTarget is not null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
        }
        else
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            dpiX = dpi.DpiScaleX;
            dpiY = dpi.DpiScaleY;
        }

        double offset = Math.Clamp(Settings.Current.OverlayBottomOffset, 10, 1000);
        double left = (wa.Left + (wa.Width - Width * dpiX) / 2) / dpiX;
        double top = Settings.Current.OverlayPosition == OverlayPosition.Top
            ? (wa.Top + offset * dpiY) / dpiY
            : (wa.Bottom - Height * dpiY - offset * dpiY) / dpiY;
        Left = left;
        Top = top;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
