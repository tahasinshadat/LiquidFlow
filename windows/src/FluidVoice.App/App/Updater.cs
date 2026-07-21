using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluidVoice.Core;

namespace FluidVoice.App;

public sealed record UpdateInfo(string Version, string Notes, string DownloadUrl, bool IsPrerelease)
{
    /// <summary>Set when the installer is already a local file (from the watched update folder);
    /// DownloadAndRunAsync then runs it in place instead of downloading.</summary>
    public string? LocalFile { get; init; }
}

/// <summary>
/// Update check against GitHub releases (SimpleUpdater.swift): stable vs beta
/// (prerelease flag or -alpha/-beta/-rc suffix), hourly minimum, 24h snooze.
/// Downloads the installer asset and launches it; the installer replaces the app.
/// </summary>
public static class Updater
{
    // Point this at your fork's releases. Assets should be named LiquidFlow-Setup-<version>-<arch>.exe
    public const string Repo = "tahasinshadat/LiquidFlow";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static Updater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidVoice-Windows-Updater");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Check every configured source (the watched local folder + GitHub releases) and return the
    /// single highest available version above the running one, or null if we're current.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var local = CheckLocalFolder(Settings.Current.UpdateFolderPath);
        var github = await CheckGitHubAsync(ct);
        if (local is null) return github;
        if (github is null) return local;
        // both found → prefer the higher version
        var lv = ParseVersion(local.Version);
        var gv = ParseVersion(github.Version);
        return (gv is not null && lv is not null && gv > lv) ? github : local;
    }

    /// <summary>
    /// Scan a folder for a newer installer named LiquidFlow-Setup-&lt;version&gt;-&lt;arch&gt;.exe. This is the
    /// "watch a directory" update source: drop a fresh build in the folder and the app offers it.
    /// </summary>
    public static UpdateInfo? CheckLocalFolder(string? folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                       System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
            var current = ParseVersion(ThisVersion);
            UpdateInfo? best = null;
            Version? bestVer = null;
            foreach (var file in Directory.EnumerateFiles(folder, "LiquidFlow-Setup-*.exe"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var m = Regex.Match(name, @"^LiquidFlow-Setup-(\d+(?:\.\d+)+)-(arm64|x64)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                if (!m.Groups[2].Value.Equals(arch, StringComparison.OrdinalIgnoreCase)) continue;
                var ver = ParseVersion(m.Groups[1].Value);
                if (ver is null || ver <= current) continue;
                if (bestVer is not null && ver <= bestVer) continue;
                bestVer = ver;
                best = new UpdateInfo(m.Groups[1].Value, "Local build in your update folder.", file, false)
                {
                    LocalFile = file,
                };
            }
            return best;
        }
        catch (Exception ex)
        {
            Log.Warn("updater", $"Local update-folder scan failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<UpdateInfo?> CheckGitHubAsync(CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Repo}/releases?per_page=20";
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var current = ParseVersion(ThisVersion);
            UpdateInfo? best = null;
            Version? bestVer = null;

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var tag = rel.GetProperty("tag_name").GetString() ?? "";
                var isPre = (rel.TryGetProperty("prerelease", out var p) && p.GetBoolean())
                            || tag.Contains("-alpha") || tag.Contains("-beta") || tag.Contains("-rc");
                if (isPre && !Settings.Current.BetaReleasesEnabled) continue;

                var ver = ParseVersion(tag);
                if (ver is null || ver <= current) continue;
                if (bestVer is not null && ver <= bestVer) continue;

                var asset = FindInstallerAsset(rel);
                if (asset is null) continue;

                bestVer = ver;
                best = new UpdateInfo(tag.TrimStart('v'),
                    rel.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    asset, isPre);
            }

            Settings.Current.Save(); // records that we checked (last-check tracking could be added here)
            return best;
        }
        catch (Exception ex)
        {
            Log.Warn("updater", $"Update check failed: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> DownloadAndRunAsync(UpdateInfo update, CancellationToken ct)
    {
        try
        {
            string installer;
            if (!string.IsNullOrEmpty(update.LocalFile) && File.Exists(update.LocalFile))
            {
                installer = update.LocalFile; // already local (watched folder) — run it in place
            }
            else
            {
                installer = Path.Combine(Path.GetTempPath(), $"LiquidFlow-Setup-{update.Version}.exe");
                await using var resp = await Http.GetStreamAsync(update.DownloadUrl, ct);
                await using var file = File.Create(installer);
                await resp.CopyToAsync(file, ct);
            }

            // Silent in-place update: the installer detects the existing install, closes the
            // running app, replaces files in the same folder, and relaunches it (see FluidVoice.iss).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/SILENT /NORESTART /SUPPRESSMSGBOXES",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("updater", "Failed to download/run installer", ex);
            return false;
        }
    }

    private static string? FindInstallerAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                   System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        string? fallback = null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var dl = a.GetProperty("browser_download_url").GetString();
            if (dl is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains(arch, StringComparison.OrdinalIgnoreCase)) return dl;
            fallback ??= dl;
        }
        return fallback;
    }

    public static string ThisVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.6.2";

    private static Version? ParseVersion(string tag)
    {
        var cleaned = new string(tag.TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(cleaned, out var v) ? v : null;
    }
}
