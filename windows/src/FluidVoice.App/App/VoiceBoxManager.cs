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

    // ── Backend pre-warm ───────────────────────────────────────────────────
    // The slow part of "opening VoiceBox" is voicebox-server.exe (its Python AI backend,
    // x64 under emulation here). VoiceBox's desktop shell REUSES any voicebox-server
    // already listening on its fixed port, so booting the server headless ahead of time
    // makes the tab open in seconds.

    /// <summary>VoiceBox's fixed backend port (SERVER_PORT in its Tauri shell).</summary>
    public const int ServerPort = 17493;

    /// <summary>Quick TCP probe: is a VoiceBox backend already listening?</summary>
    public static bool IsServerUp()
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            return c.ConnectAsync("127.0.0.1", ServerPort).Wait(300) && c.Connected;
        }
        catch { return false; }
    }

    /// <summary>Start voicebox-server.exe headless (no window) with the exact arguments the
    /// VoiceBox shell would use. Returns true if the server is up or was just spawned.
    /// `--parent-pid` ties its lifetime to LiquidFlow, so nothing is left orphaned.</summary>
    public static bool PrewarmServer(bool force = false)
    {
        try
        {
            if (!force && !Settings.Current.VoiceBoxPrewarmEnabled) return false;
            if (IsServerUp()) return true;
            var exe = FindExecutable();
            if (exe is null) return false;
            var server = Path.Combine(Path.GetDirectoryName(exe)!, "voicebox-server.exe");
            if (!File.Exists(server)) return false;
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sh.voicebox.app");
            Directory.CreateDirectory(dataDir);
            var psi = new ProcessStartInfo(server)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(server)!,
            };
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(dataDir);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(ServerPort.ToString());
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(psi);
            Log.Info("voicebox", "Pre-warming VoiceBox's AI backend (headless) so the tab opens fast");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"VoiceBox pre-warm failed: {ex.Message}");
            return false;
        }
    }

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

    // ── Built-in voice library ─────────────────────────────────────────────
    // VoiceBox's preset voices (Kokoro 82M speakers + Qwen CustomVoice speakers) live inside
    // the engine models, but only become usable once a PROFILE exists for them. Fresh installs
    // have zero profiles, which reads as "no built-in voices". We seed one preset profile per
    // catalog voice (plus a curated "Jarvis" persona) straight into voicebox.db — exactly the
    // rows VoiceBox's own "New Profile → preset" flow would create.

    private static string VoiceBoxDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sh.voicebox.app", "voicebox.db");

    /// <summary>Insert the full preset-voice catalog as ready profiles. Idempotent: does
    /// nothing if any preset profile already exists (first seed or user-curated). Returns
    /// how many profiles were added.</summary>
    public static Task<int> SeedPresetVoicesAsync() => Task.Run(() =>
    {
        var db = VoiceBoxDbPath;
        if (!File.Exists(db)) return 0;
        try
        {
            using var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False;Default Timeout=5");
            con.Open();
            using (var probe = con.CreateCommand())
            {
                probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='profiles'";
                if (Convert.ToInt32(probe.ExecuteScalar()) == 0) return 0;
            }
            using (var existing = con.CreateCommand())
            {
                existing.CommandText = "SELECT COUNT(*) FROM profiles WHERE voice_type='preset'";
                if (Convert.ToInt64(existing.ExecuteScalar()) > 0) return 0;
            }

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
            int added = 0;
            using var tx = con.BeginTransaction();
            foreach (var (engine, voiceId, name, lang, desc) in PresetCatalog())
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO profiles (id, name, description, language, voice_type, preset_engine, preset_voice_id, default_engine, created_at, updated_at) " +
                    "VALUES ($id, $name, $desc, $lang, 'preset', $engine, $vid, $engine, $now, $now)";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$desc", desc);
                cmd.Parameters.AddWithValue("$lang", lang);
                cmd.Parameters.AddWithValue("$engine", engine);
                cmd.Parameters.AddWithValue("$vid", voiceId);
                cmd.Parameters.AddWithValue("$now", now);
                cmd.ExecuteNonQuery();
                added++;
            }
            tx.Commit();
            Log.Info("voicebox", $"Seeded {added} built-in voice profiles into voicebox.db");
            return added;
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"Preset voice seeding skipped: {ex.Message}");
            return 0;
        }
    });

    public static IEnumerable<(string Engine, string VoiceId, string Name, string Lang, string Desc)> PresetCatalog()
    {
        const string K = "kokoro";
        const string Q = "qwen_custom_voice";

        // Curated persona first so it tops the list.
        yield return (K, "bm_george", "Jarvis",
            "en", "British AI-assistant persona built on the Kokoro 'George' preset. Add the Robotic or Radio effect preset for the full J.A.R.V.I.S. sound.");

        // Kokoro 82M — runs realtime on CPU (~330 MB one-time model download inside VoiceBox).
        var kokoro = new (string Id, string Name, string Gender, string Lang, string Tag)[]
        {
            ("af_alloy", "Alloy", "female", "en", "American English"),
            ("af_aoede", "Aoede", "female", "en", "American English"),
            ("af_bella", "Bella", "female", "en", "American English"),
            ("af_heart", "Heart", "female", "en", "American English"),
            ("af_jessica", "Jessica", "female", "en", "American English"),
            ("af_kore", "Kore", "female", "en", "American English"),
            ("af_nicole", "Nicole", "female", "en", "American English"),
            ("af_nova", "Nova", "female", "en", "American English"),
            ("af_river", "River", "female", "en", "American English"),
            ("af_sarah", "Sarah", "female", "en", "American English"),
            ("af_sky", "Sky", "female", "en", "American English"),
            ("am_adam", "Adam", "male", "en", "American English"),
            ("am_echo", "Echo", "male", "en", "American English"),
            ("am_eric", "Eric", "male", "en", "American English"),
            ("am_fenrir", "Fenrir", "male", "en", "American English"),
            ("am_liam", "Liam", "male", "en", "American English"),
            ("am_michael", "Michael", "male", "en", "American English"),
            ("am_onyx", "Onyx", "male", "en", "American English"),
            ("am_puck", "Puck", "male", "en", "American English"),
            ("am_santa", "Santa", "male", "en", "American English"),
            ("bf_alice", "Alice (UK)", "female", "en", "British English"),
            ("bf_emma", "Emma (UK)", "female", "en", "British English"),
            ("bf_isabella", "Isabella (UK)", "female", "en", "British English"),
            ("bf_lily", "Lily (UK)", "female", "en", "British English"),
            ("bm_daniel", "Daniel (UK)", "male", "en", "British English"),
            ("bm_fable", "Fable (UK)", "male", "en", "British English"),
            ("bm_george", "George (UK)", "male", "en", "British English"),
            ("bm_lewis", "Lewis (UK)", "male", "en", "British English"),
            ("ef_dora", "Dora (Spanish)", "female", "es", "Spanish"),
            ("em_alex", "Alex (Spanish)", "male", "es", "Spanish"),
            ("em_santa", "Santa (Spanish)", "male", "es", "Spanish"),
            ("ff_siwis", "Siwis (French)", "female", "fr", "French"),
            ("hf_alpha", "Alpha (Hindi)", "female", "hi", "Hindi"),
            ("hf_beta", "Beta (Hindi)", "female", "hi", "Hindi"),
            ("hm_omega", "Omega (Hindi)", "male", "hi", "Hindi"),
            ("hm_psi", "Psi (Hindi)", "male", "hi", "Hindi"),
            ("if_sara", "Sara (Italian)", "female", "it", "Italian"),
            ("im_nicola", "Nicola (Italian)", "male", "it", "Italian"),
            ("jf_alpha", "Alpha (Japanese)", "female", "ja", "Japanese"),
            ("jf_gongitsune", "Gongitsune (Japanese)", "female", "ja", "Japanese"),
            ("jf_nezumi", "Nezumi (Japanese)", "female", "ja", "Japanese"),
            ("jf_tebukuro", "Tebukuro (Japanese)", "female", "ja", "Japanese"),
            ("jm_kumo", "Kumo (Japanese)", "male", "ja", "Japanese"),
            ("pf_dora", "Dora (Portuguese)", "female", "pt", "Portuguese"),
            ("pm_alex", "Alex (Portuguese)", "male", "pt", "Portuguese"),
            ("pm_santa", "Santa (Portuguese)", "male", "pt", "Portuguese"),
            ("zf_xiaobei", "Xiaobei (Chinese)", "female", "zh", "Chinese"),
            ("zf_xiaoni", "Xiaoni (Chinese)", "female", "zh", "Chinese"),
            ("zf_xiaoxiao", "Xiaoxiao (Chinese)", "female", "zh", "Chinese"),
            ("zf_xiaoyi", "Xiaoyi (Chinese)", "female", "zh", "Chinese"),
            ("zm_yunjian", "Yunjian (Chinese)", "male", "zh", "Chinese"),
            ("zm_yunxi", "Yunxi (Chinese)", "male", "zh", "Chinese"),
            ("zm_yunxia", "Yunxia (Chinese)", "male", "zh", "Chinese"),
            ("zm_yunyang", "Yunyang (Chinese)", "male", "zh", "Chinese"),
        };
        foreach (var v in kokoro)
            yield return (K, v.Id, v.Name, v.Lang, $"Built-in Kokoro preset · {v.Tag} · {v.Gender}. Runs realtime on CPU.");

        // Qwen CustomVoice — 9 curated speakers with natural-language delivery control.
        var qwen = new (string Id, string Name, string Lang, string Desc)[]
        {
            ("Ryan", "Ryan (Qwen)", "en", "Dynamic male voice with strong rhythmic drive"),
            ("Aiden", "Aiden (Qwen)", "en", "Sunny American male voice with a clear midrange"),
            ("Vivian", "Vivian (Qwen)", "zh", "Bright, slightly edgy young female voice"),
            ("Serena", "Serena (Qwen)", "zh", "Warm, gentle young female voice"),
            ("Uncle_Fu", "Uncle Fu (Qwen)", "zh", "Seasoned male voice with a low, mellow timbre"),
            ("Dylan", "Dylan (Qwen)", "zh", "Youthful Beijing male voice with a clear, natural timbre"),
            ("Eric", "Eric (Qwen, Chinese)", "zh", "Lively Chengdu male voice with a slightly husky brightness"),
            ("Ono_Anna", "Ono Anna (Qwen)", "ja", "Playful Japanese female voice with a light, nimble timbre"),
            ("Sohee", "Sohee (Qwen)", "ko", "Warm Korean female voice with rich emotion"),
        };
        foreach (var v in qwen)
            yield return (Q, v.Id, v.Name, v.Lang, $"Built-in Qwen CustomVoice preset — {v.Desc}. Supports natural-language delivery instructions.");
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
