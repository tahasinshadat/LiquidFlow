using System.IO;
using System.Windows;
using System.Windows.Media;
using FluidVoice.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FluidVoice.Ui.Web;

/// <summary>
/// Hosts the React front-end (windows/web) in a WebView2 control as the app window.
/// Loads the vite dev server when LIQUIDFLOW_WEB_DEV=1 (hot reload), otherwise the bundled
/// static build via a virtual host mapping. All UI lives in the web layer; this window is
/// just the native shell + the <see cref="WebBridge"/> to the C# core.
/// </summary>
public sealed class WebShellWindow : Window
{
    private readonly WebView2 _web = new();
    private WebBridge? _bridge;

    public WebShellWindow()
    {
        Title = "LiquidFlow";
        Width = 1240;
        Height = 820;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(13, 16, 19)); // matches web --color-ink until first paint
        WindowFx.Apply(this);

        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 13, 16, 19);
        Content = _web;
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => DetachEvents();
    }

    private async Task InitAsync()
    {
        try
        {
            var userData = Path.Combine(AppPaths.DataDir, "WebView2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            _bridge = new WebBridge(core, Dispatcher);
            AttachEvents();

            // trim chrome we don't want in an app shell
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif

            if (Environment.GetEnvironmentVariable("LIQUIDFLOW_WEB_DEV") == "1")
            {
                core.Navigate("http://localhost:5199/");
                Log.Info("webshell", "loaded vite dev server");
            }
            else
            {
                var dist = ResolveDist();
                if (dist is null)
                {
                    core.NavigateToString("<body style='background:#0d1013;color:#eee;font-family:Segoe UI;padding:40px'>" +
                        "<h2>Web UI not built</h2><p>Run <code>npm run build</code> in windows/web, or set LIQUIDFLOW_WEB_DEV=1 with the vite dev server running.</p></body>");
                    Log.Warn("webshell", "no web/dist found");
                    return;
                }
                core.SetVirtualHostNameToFolderMapping("liquidflow.app", dist, CoreWebView2HostResourceAccessKind.Allow);
                core.Navigate("https://liquidflow.app/index.html");
                Log.Info("webshell", $"loaded bundled UI from {dist}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("webshell", "WebView2 init failed", ex);
            MessageBox.Show($"Failed to start the web UI: {ex.Message}", "LiquidFlow");
        }
    }

    /// <summary>Find the built front-end: next to the exe (shipping) or in the source tree (dev).</summary>
    private static string? ResolveDist()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "web") };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "windows", "web", "dist"));
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "index.html")));
    }

    // ---- push core events to the web UI ----

    private Action<string>? _settingsHandler;
    private Action? _historyHandler;

    private void AttachEvents()
    {
        _settingsHandler = hint => _bridge?.Emit("settingsChanged", new { hint });
        _historyHandler = () => _bridge?.Emit("historyChanged");
        Settings.Changed += _settingsHandler;
        HistoryStore.HistoryChanged += _historyHandler;
    }

    private void DetachEvents()
    {
        if (_settingsHandler is not null) Settings.Changed -= _settingsHandler;
        if (_historyHandler is not null) HistoryStore.HistoryChanged -= _historyHandler;
    }
}
