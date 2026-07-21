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
            "Snippets", "Style", "Transforms", "Scratchpad", "Meetings", "VoiceBox",
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
                // Style tab post-wizard content (in-memory flag only — never saved)
                var prevWiz = Settings.Current.StyleWizardCompleted;
                Settings.Current.StyleWizardCompleted = true;
                win.CaptureNavigate("Style");
                await Task.Delay(350);
                win.UpdateLayout();
                Snap(win, Path.Combine(outDir, "style-content.png"));
                Settings.Current.StyleWizardCompleted = prevWiz;

                // Style wizard steps + completion
                var wiz = new StyleWizardDialog
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = 120,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                wiz.Show();
                for (int s = 0; s <= 4; s++)
                {
                    wiz.SetStepForCapture(s);
                    await Task.Delay(250);
                    wiz.UpdateLayout();
                    Snap(wiz, Path.Combine(outDir, s == 4 ? "style-wizard-allset.png" : $"style-wizard-{s + 1}.png"));
                }
                wiz.Close();

                // Floating scratchpad note popup
                NoteWindow.OpenNote(null);
                await Task.Delay(300);
                if (Application.Current.Windows.OfType<NoteWindow>().FirstOrDefault() is { } nw)
                {
                    nw.UpdateLayout();
                    Snap(nw, Path.Combine(outDir, "scratchpad-note.png"));
                    nw.Close();
                }

                var captureNote = new Note
                {
                    Id = "ui-capture-scratchpad-note",
                    Title = "yeooo",
                    CustomTitle = true,
                    Body = "1. Go to the gym\n2. Complete UI review\n3. Test the scratchpad editor\n4. Ship the update",
                };
                NoteWindow.OpenNote(captureNote);
                await Task.Delay(300);
                if (Application.Current.Windows.OfType<NoteWindow>().FirstOrDefault() is { } contentNote)
                {
                    contentNote.UpdateLayout();
                    Snap(contentNote, Path.Combine(outDir, "scratchpad-note-content.png"));
                    contentNote.SetSidebarCollapsedForCapture(true);
                    contentNote.UpdateLayout();
                    Snap(contentNote, Path.Combine(outDir, "scratchpad-note-collapsed.png"));
                    contentNote.SetSidebarCollapsedForCapture(false);
                    contentNote.SetToolForCapture("formatting");
                    contentNote.UpdateLayout();
                    Snap(contentNote, Path.Combine(outDir, "scratchpad-note-formatting.png"));
                    contentNote.SetToolForCapture("transforms");
                    contentNote.UpdateLayout();
                    Snap(contentNote, Path.Combine(outDir, "scratchpad-note-transforms.png"));
                    contentNote.Close();
                }
                win.CaptureNavigate("Scratchpad");
                await Task.Delay(250);
                win.UpdateLayout();
                Snap(win, Path.Combine(outDir, "scratchpad-content.png"));
                NotesStore.Delete(captureNote.Id);

                Console.WriteLine($"captured {pages.Length + 12} screens -> {outDir}");
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
