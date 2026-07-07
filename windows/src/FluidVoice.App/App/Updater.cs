using System.Net.Http;
using System.Text.Json;
using FluidVoice.Core;

namespace FluidVoice.App;

public sealed record UpdateInfo(string Version, string Notes, string DownloadUrl, bool IsPrerelease);

/// <summary>
/// Update check against GitHub releases (SimpleUpdater.swift): stable vs beta
/// (prerelease flag or -alpha/-beta/-rc suffix), hourly minimum, 24h snooze.
/// Downloads the installer asset and launches it; the installer replaces the app.
/// </summary>
public static class Updater
{
    // Point this at your fork's releases. Assets should be named FluidVoice-Setup-<version>-<arch>.exe
    public const string Repo = "altic-dev/FluidVoice-Windows";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static Updater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidVoice-Windows-Updater");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
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
            var tmp = Path.Combine(Path.GetTempPath(), $"FluidVoice-Setup-{update.Version}.exe");
            await using (var resp = await Http.GetStreamAsync(update.DownloadUrl, ct))
            await using (var file = File.Create(tmp))
                await resp.CopyToAsync(file, ct);

            // Silent in-place update: the installer detects the existing install, closes the
            // running app, replaces files in the same folder, and relaunches it (see FluidVoice.iss).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tmp,
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
