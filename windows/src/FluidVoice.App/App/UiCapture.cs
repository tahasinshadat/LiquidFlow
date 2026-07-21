using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluidVoice.Core;
using FluidVoice.Ui;

namespace FluidVoice.App;

/// <summary>
/// UI screenshot harness: `LiquidFlow.exe --capture-ui &lt;outDir&gt;` renders every page of the
/// main window offscreen to PNGs (dictation, insights usage+voice, dictionary, snippets,
/// style, transforms, scratchpad, meetings) and exits. Used by the visual-review loop that
/// compares each screen against windows/design/wispr-reference/.
/// </summary>
public static class UiCapture
{
    /// <summary>True while capturing — suppresses first-run dialogs.</summary>
    public static bool CaptureMode { get; private set; }

    public static int Run(string[] args)
    {
        CaptureMode = true;
        var idx = Array.IndexOf(args, "--capture-ui");
        var outDir = idx + 1 < args.Length && !args[idx + 1].StartsWith("-")
            ? args[idx + 1]
            : Path.Combine(AppPaths.DataDir, "ui-captures");
        Directory.CreateDirectory(outDir);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Styles.Apply(app);
        var win = new MainWindow(null, null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000, // offscreen: renders fully without appearing or stealing focus
            Top = 100,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        win.Show();

        var pages = new[]
        {
            "Dictation", "Insights", "Insights:voice", "Dictionary",
            "Snippets", "Style", "Transforms", "Scratchpad", "Meetings",
        };

        var exit = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                foreach (var page in pages)
                {
                    var voice = page.EndsWith(":voice");
                    var title = voice ? page.Split(':')[0] : page;
                    HomeTab.DefaultVoice = voice;
                    win.CaptureNavigate(title);
                    HomeTab.DefaultVoice = false;
                    await Task.Delay(350); // let layout, fonts, and async page content settle
                    win.UpdateLayout();
                    Snap(win, Path.Combine(outDir, page.Replace(":", "-").ToLowerInvariant() + ".png"));
                }
                Console.WriteLine($"captured {pages.Length} screens -> {outDir}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"capture failed: {ex}");
                exit = 1;
            }
            finally
            {
                app.Shutdown();
            }
        });
        app.Run();
        return exit;
    }

    private static void Snap(Window win, string path)
    {
        if (win.Content is not FrameworkElement root || root.ActualWidth < 1) return;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
