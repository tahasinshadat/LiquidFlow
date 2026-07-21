using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;
using Microsoft.Win32;

namespace FluidVoice.Ui;

/// <summary>
/// VoiceBox integration: routes to the open-source (MIT) AI voice studio by Jamie Pine
/// (github.com/jamiepine/voicebox — voice cloning, 7 TTS engines, stories editor, agent
/// voices). VoiceBox is its own Tauri app, so LiquidFlow detects the install and launches
/// it; if you run VoiceBox's web/server mode, it can also embed right inside LiquidFlow.
/// </summary>
public sealed class VoiceBoxTab : StackPanel
{
    private const string ReleasesUrl = "https://github.com/jamiepine/voicebox/releases/latest";
    private const string RepoUrl = "https://github.com/jamiepine/voicebox";

    public VoiceBoxTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        var exe = VoiceBoxLocator.FindExecutable();
        Children.Add(BuildHeader(exe is not null));
        Children.Add(BuildHero(exe));
        Children.Add(BuildFeatureCards());
        Children.Add(BuildEmbedCard());
        Children.Add(new TextBlock
        {
            Text = "VoiceBox is a separate MIT-licensed open-source project by Jamie Pine. LiquidFlow launches or embeds it; all credit to the VoiceBox authors.",
            FontSize = 11.5,
            FontStyle = FontStyles.Italic,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 18, 0, 0),
        });
    }

    private UIElement BuildHeader(bool installed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 30) };
        var status = Theme.Pill(installed ? "Installed" : "Not installed",
            installed ? Theme.GreenSoftBrush : new SolidColorBrush(Theme.SidebarSelected),
            installed ? Theme.GreenBrush : Theme.SubtleBrush, 12);
        status.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(status);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "VoiceBox",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
        });
        var chip = Theme.Pill("MIT · open source", Theme.InkBrush, new SolidColorBrush(Theme.InkText), 11);
        chip.Margin = new Thickness(12, 4, 0, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(chip);
        row.Children.Add(left);
        return row;
    }

    private UIElement BuildHero(string? exe)
    {
        var dock = new DockPanel();
        var cluster = (FrameworkElement)PageChrome.IconCluster(46);
        cluster.VerticalAlignment = VerticalAlignment.Center;
        cluster.Margin = new Thickness(0, 0, 34, 0);
        DockPanel.SetDock(cluster, Dock.Right);
        dock.Children.Add(cluster);

        var content = new StackPanel { Margin = new Thickness(40, 28, 20, 28), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
        title.Inlines.Add(new Run("The open-source AI "));
        title.Inlines.Add(new Run("voice studio") { FontStyle = FontStyles.Italic });
        title.Inlines.Add(new Run("."));
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = "Clone any voice, generate speech in 23 languages, build multi-track audio stories, and give your AI agents a voice — running right beside LiquidFlow.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (exe is not null)
        {
            var open = PageChrome.HeroPill("Open VoiceBox");
            open.MouseLeftButtonUp += (_, _) => Launch(exe);
            actions.Children.Add(open);
            var where = new TextBlock
            {
                Text = "Opens in its own window — LiquidFlow stays right here.",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
            };
            actions.Children.Add(where);
        }
        else
        {
            var get = PageChrome.HeroPill("Get VoiceBox (free)");
            get.MouseLeftButtonUp += (_, _) => OpenUrl(ReleasesUrl);
            actions.Children.Add(get);
            var refresh = new TextBlock
            {
                Text = "Re-check after installing",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline,
            };
            refresh.MouseLeftButtonUp += (_, _) => Build();
            actions.Children.Add(refresh);
        }
        content.Children.Add(actions);
        dock.Children.Add(content);

        var hero = PageChrome.DarkHero(dock);
        hero.Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private UIElement BuildFeatureCards()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < 2) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        }
        var features = new (string Title, string Body)[]
        {
            ("Clone & generate", "Seven TTS engines, zero-shot voice cloning from a sample, 50+ preset voices, paralinguistic tags like [laugh] and [sigh]."),
            ("Stories editor", "Multi-track timeline for conversations and podcasts — drag, trim, and play whole scenes in the voices you built."),
            ("Agent voices", "An MCP server + REST API so Claude Code, Cursor, and your own tools can speak in voices you own."),
        };
        for (int i = 0; i < features.Length; i++)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = features[i].Title, FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 8) });
            p.Children.Add(new TextBlock { Text = features[i].Body, FontSize = 13, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap });
            var card = new Border
            {
                Background = new SolidColorBrush(Theme.CardInner),
                BorderBrush = Theme.HairlineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 18, 20, 18),
                MinHeight = 128,
                Child = p,
            };
            Grid.SetColumn(card, i * 2);
            grid.Children.Add(card);
        }
        return grid;
    }

    private UIElement BuildEmbedCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Embed inside LiquidFlow (web mode)",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(Theme.Caption("If you run VoiceBox's server/Docker web mode, open it right here in a LiquidFlow window with a Back button — no window juggling."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var url = new TextBox
        {
            Width = 300,
            Text = "http://localhost:8000",
            Padding = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(url);
        var open = Theme.SecondaryButton("Open embedded");
        open.Margin = new Thickness(10, 0, 0, 0);
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Click += (_, _) =>
        {
            var target = url.Text.Trim();
            if (target.Length == 0) return;
            if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                target = "http://" + target;
            new VoiceBoxWebWindow(target).Show();
        };
        row.Children.Add(open);
        panel.Children.Add(row);
        return Theme.Card2(panel);
    }

    private static void Launch(string exe)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"Failed to launch VoiceBox: {ex.Message}");
        }
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}

/// <summary>Finds an installed VoiceBox (Tauri MSI/NSIS installs + common paths).</summary>
public static class VoiceBoxLocator
{
    public static string? FindExecutable()
    {
        // 1. Uninstall registry (both scopes/views): DisplayName containing "voicebox"
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default),
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view)
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    var name = k?.GetValue("DisplayName") as string;
                    if (name is null || !name.Contains("voicebox", StringComparison.OrdinalIgnoreCase)) continue;
                    // NB: Tauri's NSIS writes these values with literal surrounding quotes.
                    var loc = (k?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    var exe = FindExeUnder(loc);
                    if (exe is not null) return exe;
                    var icon = (k?.GetValue("DisplayIcon") as string)?.Trim().Trim('"');
                    if (icon is not null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon))
                        return icon;
                }
            }
            catch { /* registry view unavailable */ }
        }

        // 2. Common install paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VoiceBox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "voicebox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VoiceBox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "voicebox"),
            // Tauri per-user NSIS default: %LOCALAPPDATA%\<ProductName> directly
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voicebox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceBox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "voicebox"),
        };
        foreach (var dir in candidates)
        {
            var exe = FindExeUnder(dir);
            if (exe is not null) return exe;
        }
        return null;
    }

    private static string? FindExeUnder(string? dir)
    {
        try
        {
            dir = dir?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
            foreach (var name in new[] { "VoiceBox.exe", "voicebox.exe", "Voicebox.exe" })
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            return Directory.EnumerateFiles(dir, "voicebox*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
