using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FluidVoice.Ui;

public enum ToastKind { Info, Success, Error }

/// <summary>
/// App-wide toast notifications: cards slide in at the top-right of the content sheet,
/// auto-dismiss (click to dismiss sooner), and stack newest-first. Use for transient
/// outcomes — "voice ready", "export done", server errors — instead of burying them
/// in inline status text.
/// </summary>
public static class Toasts
{
    private static StackPanel? _host;
    private const int MaxVisible = 4;

    /// <summary>MainWindow installs the overlay host once at startup.</summary>
    public static void Attach(StackPanel host) => _host = host;

    public static void Info(string message) => Show(message, ToastKind.Info);
    public static void Success(string message) => Show(message, ToastKind.Success);
    public static void Error(string message) => Show(message, ToastKind.Error);

    public static void Show(string message, ToastKind kind = ToastKind.Info, int? durationMs = null)
    {
        var host = _host;
        if (host is null || string.IsNullOrWhiteSpace(message)) return;
        host.Dispatcher.BeginInvoke(() =>
        {
            var card = BuildCard(message, kind);
            host.Children.Insert(0, card);
            while (host.Children.Count > MaxVisible)
                host.Children.RemoveAt(host.Children.Count - 1);

            // slide + fade in
            var slide = (TranslateTransform)card.RenderTransform;
            card.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            var life = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs ?? (kind == ToastKind.Error ? 7000 : 4200)),
            };
            life.Tick += (_, _) => { life.Stop(); Dismiss(card); };
            life.Start();
            card.MouseLeftButtonUp += (_, _) => { life.Stop(); Dismiss(card); };
        });
    }

    private static void Dismiss(Border card)
    {
        if (card.Tag is "dismissing") return;
        card.Tag = "dismissing";
        var slide = (TranslateTransform)card.RenderTransform;
        var fade = new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => (card.Parent as Panel)?.Children.Remove(card);
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        card.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static Border BuildCard(string message, ToastKind kind)
    {
        var (glyph, accent, accentSoft) = kind switch
        {
            ToastKind.Success => ("", Theme.GreenBrush, Theme.GreenSoftBrush),
            ToastKind.Error => ("", new SolidColorBrush(Theme.Danger), new SolidColorBrush(Color.FromArgb(28, 200, 60, 50))),
            _ => ("", Theme.SubtleBrush, (Brush)new SolidColorBrush(Theme.SidebarSelected)),
        };

        var row = new DockPanel { LastChildFill = true };
        var icon = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = accentSoft,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 10, 0),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // accent stripe down the left edge — reads the severity at a glance
        var shell = new DockPanel { LastChildFill = true };
        var stripe = new Border
        {
            Width = 4,
            Background = accent,
            CornerRadius = new CornerRadius(14, 0, 0, 14),
        };
        DockPanel.SetDock(stripe, Dock.Left);
        shell.Children.Add(stripe);
        shell.Children.Add(new Border { Padding = new Thickness(12, 11, 16, 12), Child = row });

        return new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 400,
            MinWidth = 250,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
            Opacity = 0,
            ClipToBounds = true,
            RenderTransform = new TranslateTransform(28, 0),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.18, Color = Colors.Black },
            Child = shell,
        };
    }
}
