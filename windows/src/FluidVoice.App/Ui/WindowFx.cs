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

    /// <summary>Sets the window icon and switches the title bar to dark mode.</summary>
    public static void Apply(Window window)
    {
        try { window.Icon = AppIcon; } catch { }
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                int dark = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (19 on older builds)
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch { }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
