using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace FluidVoice.Ui;

/// <summary>Shared window chrome: app icon + immersive dark title bar.</summary>
public static class WindowFx
{
    private static BitmapFrame? _icon;

    public static BitmapFrame AppIcon =>
        _icon ??= BitmapFrame.Create(new Uri("pack://application:,,,/Assets/fluidvoice.ico"));

    public const double TitlebarHeight = 44;

    /// <summary>
    /// Replaces the native title bar with an in-app one (icon + app name + caption buttons)
    /// so the window chrome is part of the design instead of a bar appended on top.
    /// Returns the titlebar element; the caller docks it at the top of its layout.
    /// The whole strip is draggable (WindowChrome caption) and supports snap/double-click.
    /// </summary>
    public static UIElement InstallChrome(Window window, string title)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = TitlebarHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0, 1, 0, 0), // keeps the DWM shadow + Win11 rounding
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });

        var bar = new Grid { Height = TitlebarHeight, Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        var mark = new Image
        {
            Width = 20,
            Height = 20,
            Source = AppIcon,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
        brand.Children.Add(mark);
        brand.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
        });
        bar.Children.Add(brand);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(CaptionButton(window, "", "Minimize",
            _ => SystemCommands.MinimizeWindow(window)));
        TextBlock? maxGlyph = null;
        var maxBtn = CaptionButton(window, "", "Maximize", _ =>
        {
            if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
            else SystemCommands.MaximizeWindow(window);
        });
        maxGlyph = (TextBlock)((Border)maxBtn).Child;
        buttons.Children.Add(maxBtn);
        buttons.Children.Add(CaptionButton(window, "", "Close",
            _ => window.Close(), danger: true));
        Grid.SetColumn(buttons, 1);
        bar.Children.Add(buttons);

        window.StateChanged += (_, _) =>
        {
            bool max = window.WindowState == WindowState.Maximized;
            if (maxGlyph is not null) maxGlyph.Text = max ? "" : "";
            // keep content off the monitor edges when maximized (WindowChrome quirk)
            if (window.Content is FrameworkElement fe)
                fe.Margin = max ? new Thickness(7) : new Thickness(0);
        };

        Core.Settings.Changed += _ => window.Dispatcher.BeginInvoke(() =>
        {
            if (window.IsLoaded && brand.Children[1] is TextBlock t) t.Foreground = Theme.TextBrush;
        });
        return bar;
    }

    private static UIElement CaptionButton(Window window, string glyph, string tip, Action<object?> onClick, bool danger = false)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var host = new Border
        {
            Width = 46,
            Height = TitlebarHeight - 6,
            Background = new SolidColorBrush(Colors.Transparent),
            Child = text,
            ToolTip = tip,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(host, true);

        var hoverColor = danger ? Color.FromRgb(196, 43, 28)
                       : Theme.IsDark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
        void Animate(Color to, Brush fg, int ms)
        {
            ((SolidColorBrush)host.Background).BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)));
            text.Foreground = fg;
        }
        host.MouseEnter += (_, _) => Animate(hoverColor, danger ? Brushes.White : Theme.TextBrush, 110);
        host.MouseLeave += (_, _) => Animate(Colors.Transparent, Theme.SubtleBrush, 160);
        host.MouseLeftButtonUp += (_, _) => onClick(null);
        Core.Settings.Changed += _ => window.Dispatcher.BeginInvoke(() =>
        {
            if (!window.IsLoaded) return;
            hoverColor = danger ? Color.FromRgb(196, 43, 28)
                       : Theme.IsDark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
            if (!host.IsMouseOver) text.Foreground = Theme.SubtleBrush;
        });
        return host;
    }

    /// <summary>Clips an element to a rounded rect that tracks its size (scrollbars stay inside cards).</summary>
    public static void RoundClip(FrameworkElement element, double radius)
    {
        void Update() => element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        element.SizeChanged += (_, _) => Update();
        if (element.IsLoaded) Update();
    }

    /// <summary>Sets the window icon, modern font, and switches the title bar to dark mode.</summary>
    public static void Apply(Window window)
    {
        try { window.Icon = AppIcon; } catch { }
        window.FontFamily = Theme.UiFont;
        System.Windows.Media.TextOptions.SetTextFormattingMode(window, System.Windows.Media.TextFormattingMode.Ideal);
        window.UseLayoutRounding = true;
        Core.Settings.Changed += _ => window.Dispatcher.BeginInvoke(() =>
        {
            if (window.IsLoaded) window.FontFamily = Theme.UiFont;
        });
        window.SourceInitialized += (_, _) => ApplyTitlebar(window);
        Core.Settings.Changed += _ => window.Dispatcher.BeginInvoke(() =>
        {
            if (window.IsLoaded) ApplyTitlebar(window);
        });
    }

    private static void ApplyTitlebar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = Theme.IsDark ? 1 : 0;
            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (19 on older builds)
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
