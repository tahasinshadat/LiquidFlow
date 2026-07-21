using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// The in-app VoiceBox surface: auto-downloads/installs VoiceBox on first open, launches it,
/// and re-parents its window INSIDE LiquidFlow (SetParent embed) under a "← Back" bar — the
/// sidebar entry routes here so VoiceBox feels like part of the app. Singleton so the embedded
/// window survives tab switches; navigating away hides (not kills) VoiceBox.
/// </summary>
public sealed class VoiceBoxHostView : Grid
{
    public static VoiceBoxHostView Instance { get; } = new();

    private readonly TextBlock _status = new()
    {
        FontSize = 13.5,
        Foreground = Theme.SubtleBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 520,
        TextAlignment = TextAlignment.Center,
    };
    private readonly ProgressStripe _bar = new(380, 8) { Margin = new Thickness(0, 14, 0, 0) };
    private readonly StackPanel _progressPanel;
    private readonly Border _hostBorder;
    private EmbedHost? _embed;
    private Microsoft.Web.WebView2.Wpf.WebView2? _web;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private readonly System.Windows.Controls.Button _cancel;
    private readonly System.Windows.Controls.Button _retry;

    private VoiceBoxHostView()
    {
        // Full-bleed single-cell layout: the embedded VoiceBox window IS the page
        // (the sidebar handles navigation, so no back bar or chrome of our own).

        // ---- progress / status panel ----
        _progressPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _progressPanel.Children.Add(new TextBlock
        {
            Text = "VoiceBox",
            FontFamily = Theme.DisplaySerif,
            FontSize = 30,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        });
        _progressPanel.Children.Add(_status);
        _progressPanel.Children.Add(_bar);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        // No Cancel button: setup is fast and non-blocking, and a dead-feeling button is
        // worse than none. Failures surface the Try again button below.
        _cancel = Theme.SecondaryButton("Cancel");
        _cancel.Visibility = Visibility.Collapsed;
        _retry = Theme.SecondaryButton("Try again");
        _retry.Margin = new Thickness(8, 0, 0, 0);
        _retry.Visibility = Visibility.Collapsed;
        _retry.Click += (_, _) => _ = EnsureAsync();
        buttons.Children.Add(_retry);
        _progressPanel.Children.Add(buttons);
        Children.Add(_progressPanel);

        // ---- embed host (edge to edge) ----
        _hostBorder = new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_hostBorder);

        Loaded += (_, _) =>
        {
            if (App.UiCapture.CaptureMode)
            {
                SetStatus("VoiceBox embeds right here — it downloads and installs itself on first open.", -1);
                return;
            }
            _ = EnsureAsync();
        };
        Unloaded += (_, _) => DetachEmbed(hideWindow: true);
    }

    /// <summary>Full auto flow: install if missing → launch → embed.</summary>
    private async Task EnsureAsync()
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        _retry.Visibility = Visibility.Collapsed;
        try
        {
            _progressPanel.Visibility = Visibility.Visible;
            _hostBorder.Visibility = Visibility.Collapsed;

            // ARM64 machines get the NATIVE port (VoiceBox's own backend + web UI on native
            // Python — boots in seconds). x64 machines, or the opt-in emulation toggle for
            // the Chatterbox/LuxTTS engines, get the official desktop app SetParent-embedded.
            if (VoiceBoxNative.IsArm64 && !Settings.Current.VoiceBoxUseEmulated)
            {
                await EnsureNativeAsync();
                return;
            }

            var exe = VoiceBoxManager.FindExecutable();
            if (exe is null)
            {
                SetStatus("VoiceBox isn't installed yet — downloading it now (~516 MB, one time).", -1);
                var progress = new Progress<(string Phase, double Pct)>(p => SetStatus(p.Phase, p.Pct));
                exe = await VoiceBoxManager.EnsureInstalledAsync(progress, _cts.Token);
                if (exe is null)
                {
                    SetStatus("Install didn't complete. Get it manually from github.com/jamiepine/voicebox/releases, then hit Try again.", -1);
                    ShowRetry();
                    return;
                }
            }

            // Seed the built-in voice library (Kokoro + Qwen presets, incl. the Jarvis persona)
            // before VoiceBox starts so the profiles are there on its very first paint.
            await VoiceBoxManager.SeedPresetVoicesAsync();

            // Boot the AI backend FIRST (headless). The VoiceBox shell reuses a running
            // server on its port, so this turns the slow cold boot into a quick attach.
            // Normally the LiquidFlow-startup pre-warm has already done this and the wait is ~0.
            if (!VoiceBoxManager.IsServerUp() && VoiceBoxManager.PrewarmServer(force: true))
            {
                SetStatus("Warming up VoiceBox's AI engine… (one-time per session; opens instantly while warm)", -1);
                for (int i = 0; i < 360 && !VoiceBoxManager.IsServerUp(); i++)
                    await Task.Delay(500, _cts.Token);
            }

            SetStatus("Starting VoiceBox…", -1);
            var hwnd = await LaunchAndFindWindowAsync(exe, _cts.Token);
            if (hwnd == IntPtr.Zero)
            {
                SetStatus("VoiceBox started but its window wasn't found — it may be open as a separate window.", -1);
                ShowRetry();
                return;
            }

            _embed?.Dispose();
            _embed = new EmbedHost(hwnd);
            _hostBorder.Child = _embed;
            _hostBorder.Visibility = Visibility.Visible;
            _progressPanel.Visibility = Visibility.Collapsed;
            Log.Info("voicebox", "VoiceBox embedded");

            // Very first run: voicebox.db only exists after VoiceBox boots — retry the seed
            // in the background so the library still lands without user action.
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 24; i++)
                {
                    await Task.Delay(5000);
                    if (await VoiceBoxManager.SeedPresetVoicesAsync() > 0) break;
                }
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled. Hit Try again whenever you're ready.", -1);
            ShowRetry();
        }
        catch (Exception ex)
        {
            Log.Error("voicebox", "VoiceBox auto-embed failed", ex);
            SetStatus($"Couldn't set up VoiceBox: {ex.Message}", -1);
            ShowRetry();
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowRetry() => Dispatcher.BeginInvoke(() =>
    {
        _retry.Visibility = Visibility.Visible;
        _bar.SetFraction(0);
    });

    /// <summary>Native ARM64 path: install runtime once, boot the server, show the real
    /// VoiceBox web UI in an embedded WebView2 — pixel-identical, zero emulation.</summary>
    private async Task EnsureNativeAsync()
    {
        if (!VoiceBoxNative.IsInstalled)
        {
            SetStatus("One-time setup: native ARM64 VoiceBox (about 350 MB)…", -1);
            var progress = new Progress<(string Phase, double Pct)>(p => SetStatus(p.Phase, p.Pct));
            await VoiceBoxNative.InstallAsync(progress, _cts!.Token);
        }

        SetStatus("Starting VoiceBox (native)…", 0.02);
        var started = await Task.Run(VoiceBoxNative.StartServer);
        if (!started)
        {
            SetStatus("Couldn't start the native VoiceBox server (the port may be blocked). Try again — details in VoiceBoxNative\\server.log.", -1);
            ShowRetry();
            return;
        }
        var boot = new Progress<double>(p => SetStatus("Starting VoiceBox (native)…", p));
        if (!await VoiceBoxNative.WaitForServerAsync(TimeSpan.FromSeconds(90), _cts!.Token, boot))
        {
            SetStatus("The native VoiceBox server didn't come up — details in VoiceBoxNative\\server.log. Try again, or enable the emulated app under Settings → General → VoiceBox.", -1);
            ShowRetry();
            return;
        }

        // profiles db now exists (server migrations) — make sure the built-in voices are there
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 12; i++)
            {
                if (await VoiceBoxManager.SeedPresetVoicesAsync() > 0) break;
                await Task.Delay(2500);
            }
        });

        if (_web is null)
        {
            _web = new Microsoft.Web.WebView2.Wpf.WebView2();
            // CRITICAL ORDER: the WPF WebView2 only finishes initialization once it is IN
            // the visual tree with a live HWND — awaiting EnsureCoreWebView2Async on an
            // unparented control deadlocks forever (the "stuck at full bar" bug).
            _hostBorder.Child = _web;
            _hostBorder.Visibility = Visibility.Visible;
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(FluidVoice.Core.AppPaths.DataDir, "WebView2-VoiceBoxNative"));
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // De-brand so this reads as part of LiquidFlow, not an embedded second app:
            // hide the VoiceBox logo marks (sidebar + loading splash). Everything else —
            // layout, colors, controls — stays exactly their UI. Runs before page scripts
            // on every navigation, so it survives frontend updates.
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (function () {
                    var style = document.createElement('style');
                    style.textContent =
                        'img[src*="voicebox-logo" i], img[alt="voicebox" i] { display: none !important; }';
                    (document.head || document.documentElement).appendChild(style);
                })();
                """);
            _web.CoreWebView2.NavigationCompleted += (_, e) => Dispatcher.BeginInvoke(() =>
            {
                if (e.IsSuccess)
                {
                    _progressPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _hostBorder.Visibility = Visibility.Collapsed;
                    SetStatus("VoiceBox's interface failed to load — details in VoiceBoxNative\\server.log.", -1);
                    ShowRetry();
                }
            });
        }
        else
        {
            _hostBorder.Child = _web;
            _hostBorder.Visibility = Visibility.Visible;
        }
        _web.Source = new Uri($"http://127.0.0.1:{VoiceBoxNative.Port}/");
        _progressPanel.Visibility = Visibility.Collapsed;
        Log.Info("voicebox", "Native VoiceBox UI embedded (WebView2)");
    }

    private void SetStatus(string text, double pct)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _status.Text = text;
            if (pct < 0) _bar.SetIndeterminate();
            else _bar.SetFraction(pct);
        });
    }

    private static async Task<IntPtr> LaunchAndFindWindowAsync(string exe, CancellationToken ct)
    {
        var processName = Path.GetFileNameWithoutExtension(exe);
        var existing = Process.GetProcessesByName(processName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (existing is null)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        for (int i = 0; i < 240; i++) // up to ~60s; with a warm backend the window shows in seconds
        {
            ct.ThrowIfCancellationRequested();
            var proc = Process.GetProcessesByName(processName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (proc is not null) return proc.MainWindowHandle;
            await Task.Delay(250, ct);
        }
        return IntPtr.Zero;
    }

    private void DetachEmbed(bool hideWindow)
    {
        if (_embed is null) return;
        _embed.Release(hideWindow);
        _hostBorder.Child = null;
        _embed.Dispose();
        _embed = null;
        _hostBorder.Visibility = Visibility.Collapsed;
        _progressPanel.Visibility = Visibility.Visible;
        SetStatus("VoiceBox keeps running in the background — reopen this tab to re-embed it.", -1);
    }

    /// <summary>Win32 SetParent embed: adopts VoiceBox's top-level window as a child of this host.</summary>
    private sealed class EmbedHost : HwndHost
    {
        private readonly IntPtr _target;
        private int _originalStyle;
        private bool _released;

        public EmbedHost(IntPtr target) => _target = target;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var host = CreateWindowEx(0, "STATIC", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                0, 0, 100, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _originalStyle = GetWindowLong(_target, GWL_STYLE);
            SetWindowLong(_target, GWL_STYLE, (_originalStyle & ~(WS_CAPTION | WS_THICKFRAME | WS_POPUP)) | WS_CHILD);
            SetParent(_target, host);
            ShowWindow(_target, SW_SHOW);
            Resize();
            SizeChanged += (_, _) => Resize();
            return new HandleRef(this, host);
        }

        private void Resize()
        {
            if (_released) return;
            // MoveWindow takes PHYSICAL pixels; ActualWidth/Height are DIPs — on a scaled
            // display (e.g. 150%) forgetting this leaves the child at ~2/3 size.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var w = Math.Max(100, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
            var h = Math.Max(100, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
            MoveWindow(_target, 0, 0, w, h, true);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Resize();
        }

        /// <summary>Give the window back to the desktop (keeps VoiceBox alive across tab switches).</summary>
        public void Release(bool hide)
        {
            if (_released) return;
            _released = true;
            try
            {
                SetParent(_target, IntPtr.Zero);
                SetWindowLong(_target, GWL_STYLE, _originalStyle);
                ShowWindow(_target, hide ? SW_HIDE : SW_SHOW);
            }
            catch { /* window may already be gone */ }
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Release(hide: true);
            DestroyWindow(hwnd.Handle);
        }

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    }
}
