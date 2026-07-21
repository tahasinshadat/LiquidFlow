using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using FluidVoice.Core;
using FluidVoice.Ui;

namespace FluidVoice.App;

/// <summary>
/// Downloads and installs VoiceBox (github.com/jamiepine/voicebox, MIT) so the sidebar
/// entry can auto-embed it. Uses the NSIS setup with /S — a silent per-user install, no
/// elevation prompt. The download is ~516 MB, so progress is surfaced and cancellable.
/// </summary>
public static class VoiceBoxManager
{
    private const string LatestApi = "https://api.github.com/repos/jamiepine/voicebox/releases/latest";
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static VoiceBoxManager()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("LiquidFlow-VoiceBox-Installer");
    }

    public static string? FindExecutable() => VoiceBoxLocator.FindExecutable();

    /// <summary>
    /// Ensure VoiceBox is installed: find it, or download the latest Windows setup and run it
    /// silently. Reports (phase, fraction 0..1 or -1 for indeterminate).
    /// </summary>
    public static async Task<string?> EnsureInstalledAsync(IProgress<(string Phase, double Pct)> progress, CancellationToken ct)
    {
        var existing = FindExecutable();
        if (existing is not null) return existing;

        progress.Report(("Checking the latest VoiceBox release…", -1));
        var (name, url, size) = await GetWindowsAssetAsync(ct);

        var downloads = Path.Combine(AppPaths.DataDir, "Downloads");
        Directory.CreateDirectory(downloads);
        var setupPath = Path.Combine(downloads, name);

        if (!File.Exists(setupPath) || new FileInfo(setupPath).Length != size)
        {
            var tmp = setupPath + ".part";
            await using (var resp = await Http.GetStreamAsync(url, ct))
            await using (var file = File.Create(tmp))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await resp.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (size > 0)
                        progress.Report(($"Downloading VoiceBox… {done / 1048576} / {size / 1048576} MB", (double)done / size));
                }
            }
            File.Move(tmp, setupPath, overwrite: true);
        }

        // strip mark-of-the-web so SmartScreen can't silently block the silent install
        try { File.Delete(setupPath + ":Zone.Identifier"); } catch { /* not NTFS / no ADS */ }

        progress.Report(("Installing VoiceBox silently — this one-time step can take a few minutes…", -1));
        var psi = name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("msiexec.exe", $"/i \"{setupPath}\" /qn /norestart") { UseShellExecute = true }
            : new ProcessStartInfo(setupPath, "/S") { UseShellExecute = true };
        using (var proc = Process.Start(psi))
        {
            if (proc is not null)
            {
                await proc.WaitForExitAsync(ct);
                Log.Info("voicebox", $"VoiceBox installer exited with code {proc.ExitCode}");
            }
        }

        // the installer registers + copies files; give the locator a while (slow under emulation)
        for (int i = 0; i < 60; i++)
        {
            var exe = FindExecutable();
            if (exe is not null) return exe;
            await Task.Delay(500, ct);
        }
        return null;
    }

    private static async Task<(string Name, string Url, long Size)> GetWindowsAssetAsync(CancellationToken ct)
    {
        var json = await Http.GetStringAsync(LatestApi, ct);
        using var doc = JsonDocument.Parse(json);
        (string, string, long)? msi = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            var size = a.GetProperty("size").GetInt64();
            // Prefer the NSIS setup: Tauri NSIS installs per-user with /S (no elevation).
            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                return (name, url, size);
            if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                msi = (name, url, size);
        }
        if (msi is { } m) return m;
        throw new InvalidOperationException("No Windows installer asset found in the latest VoiceBox release.");
    }
}
