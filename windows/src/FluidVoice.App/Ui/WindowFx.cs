using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FluidVoice.Ui;

/// <summary>Shared window chrome: app icon + immersive dark title bar.</summary>
public static class WindowFx
{
    private static BitmapFrame? _icon;

    public static BitmapFrame AppIcon =>
        _icon ??= BitmapFrame.Create(new Uri("pack://application:,,,/Assets/fluidvoice.ico"));

    /// <summary>Sets the window icon, modern font, and switches the title bar to dark mode.</summary>
    public static void Apply(Window window)
    {
        try { window.Icon = AppIcon; } catch { }
        window.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        System.Windows.Media.TextOptions.SetTextFormattingMode(window, System.Windows.Media.TextFormattingMode.Ideal);
        window.UseLayoutRounding = true;
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
