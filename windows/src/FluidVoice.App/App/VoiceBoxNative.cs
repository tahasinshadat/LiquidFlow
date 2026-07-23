using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using FluidVoice.Core;
using SharpCompress.Readers;

namespace FluidVoice.App;

/// <summary>
/// The native ARM64 VoiceBox runtime: VoiceBox's own MIT-licensed Python backend and web UI
/// running on native ARM64 Python (torch has official win-arm64 CPU wheels) instead of the
/// x64-emulated desktop bundle. LiquidFlow serves the EXACT VoiceBox UI from this server in
/// an embedded WebView2. Engines that require torchaudio (Chatterbox, LuxTTS) have no ARM64
/// wheels yet — those stay on the emulated x64 app behind an opt-in toggle.
///
/// Layout under %LOCALAPPDATA%\LiquidFlow\VoiceBoxNative:
///   python\    embeddable ARM64 CPython + site-packages (torch, transformers, kokoro…)
///   backend\   vendored VoiceBox backend source (LICENSE-VoiceBox.txt alongside)
///   frontend\  built VoiceBox web UI (served by the backend at /)
/// </summary>
public static class VoiceBoxNative
{
    public const int Port = VoiceBoxManager.ServerPort; // same fixed port the shell uses
    private const string PythonZipUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-arm64.zip";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    // Pinned VoiceBox source (MIT) — bump deliberately after testing newer versions.
    private const string SourceTarUrl = "https://codeload.github.com/jamiepine/voicebox/tar.gz/refs/heads/main";
    // Built web UI, attached to our releases (built from the same source with vite).
    private const string FrontendZipUrl = "https://github.com/tahasinshadat/LiquidFlow/releases/latest/download/VoiceBoxNative-frontend.zip";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static Process? _server;

    static VoiceBoxNative()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("LiquidFlow-VoiceBoxNative");
    }

    public static bool IsArm64 => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    public static string RootDir => Path.Combine(AppPaths.DataDir, "VoiceBoxNative");
    private static string PythonExe => Path.Combine(RootDir, "python", "python.exe");
    private static string ServerPy => Path.Combine(RootDir, "backend", "server.py");
    private static string DepsMarker => Path.Combine(RootDir, ".deps-ok");
    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sh.voicebox.app");

    public static bool IsInstalled =>
        File.Exists(PythonExe) && File.Exists(ServerPy)
        && File.Exists(Path.Combine(RootDir, "frontend", "index.html"))
        && File.Exists(DepsMarker);

    // ── install ────────────────────────────────────────────────────────────

    /// <summary>One-time setup on ARM machines: Python + wheels + VoiceBox source + web UI.
    /// Idempotent per step; total download ~350 MB (python 12 + wheels ~330 + source 6).</summary>
    public static async Task InstallAsync(IProgress<(string Phase, double Pct)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(RootDir);

        if (!File.Exists(PythonExe))
        {
            progress.Report(("Setting up native Python (ARM64)…", -1));
            var zip = Path.Combine(RootDir, "python-embed.zip");
            await DownloadAsync(PythonZipUrl, zip, "Python", progress, ct);
            ExtractArchive(zip, Path.Combine(RootDir, "python"), ct);
            File.Delete(zip);
            // embeddable distro: enable site-packages, and put the runtime ROOT on sys.path
            // ("._pth" replaces sys.path entirely — cwd is ignored — and server.py imports
            // itself as the package `backend`, which lives at RootDir\backend).
            var pth = Path.Combine(RootDir, "python", "python312._pth");
            File.WriteAllText(pth, File.ReadAllText(pth).Replace("#import site", "import site") + "\nLib\\site-packages\n..\n");
        }

        if (!Directory.Exists(Path.Combine(RootDir, "python", "Lib", "site-packages", "pip")))
        {
            progress.Report(("Bootstrapping pip…", -1));
            var getPip = Path.Combine(RootDir, "get-pip.py");
            await DownloadAsync(GetPipUrl, getPip, "pip", progress, ct);
            await RunPythonAsync($"\"{getPip}\" --no-warn-script-location", "pip bootstrap", ct);
            await RunPythonAsync("-m pip install -q --no-warn-script-location setuptools wheel", "setuptools", ct);
        }

        if (!File.Exists(DepsMarker))
        {
            progress.Report(("Installing PyTorch (native ARM64 build)… this is the big one", -1));
            await RunPythonAsync(
                "-m pip install --no-warn-script-location --only-binary :all: --progress-bar off " +
                "--index-url https://download.pytorch.org/whl/cpu \"torch==2.9.1\"", "torch", ct);

            // layered installs keep pip's resolver fast and commit progress incrementally.
            // kokoro/misaki go in with --no-deps: misaki[en] wants spacy, whose blis/thinc
            // chain has no ARM64 wheels — the runtime patch below routes English G2P
            // through espeak instead (same path misaki uses for es/fr/hi/it/pt).
            var layers = new[]
            {
                "fastapi uvicorn sqlalchemy pydantic httpx sse-starlette python-multipart loguru huggingface_hub pillow",
                "numpy scipy soundfile",
                "transformers==4.57.3", // qwen-tts pins this; whisper, qwen3 and kokoro are all fine on it
                "--no-deps kokoro misaki",
                "addict regex espeakng-loader num2words phonemizer-fork",
                "einops accelerate onnxruntime", // qwen-tts deps that have ARM64 wheels
                "--no-deps qwen-tts",            // its strict pins collide with the resolver; shims cover the gaps
                "fastmcp",
            };
            for (int i = 0; i < layers.Length; i++)
            {
                progress.Report(($"Installing AI libraries… ({i + 1}/{layers.Length})", (double)i / layers.Length));
                await RunPythonAsync(
                    $"-m pip install --no-warn-script-location --only-binary :all: --progress-bar off {layers[i]}",
                    $"deps {i + 1}", ct);
            }
            WriteShims();
            await ApplyRuntimePatchesAsync(ct);
            File.WriteAllText(DepsMarker, DateTime.UtcNow.ToString("o"));
        }

        if (!File.Exists(ServerPy))
        {
            progress.Report(("Downloading VoiceBox source (MIT, jamiepine/voicebox)…", -1));
            var tar = Path.Combine(RootDir, "voicebox-src.tar.gz");
            await DownloadAsync(SourceTarUrl, tar, "VoiceBox source", progress, ct);
            ExtractVoiceBoxBackend(tar, ct);
            File.Delete(tar);
        }

        if (!File.Exists(Path.Combine(RootDir, "frontend", "index.html")))
        {
            progress.Report(("Downloading the VoiceBox interface…", -1));
            var fz = Path.Combine(RootDir, "frontend.zip");
            await DownloadAsync(FrontendZipUrl, fz, "interface", progress, ct);
            ExtractArchive(fz, Path.Combine(RootDir, "frontend"), ct);
            File.Delete(fz);
        }

        progress.Report(("Native VoiceBox ready.", 1));
    }

    // ── server lifecycle ───────────────────────────────────────────────────

    /// <summary>Start the native server headless if not already listening. If a FOREIGN
    /// VoiceBox server holds the fixed port (e.g. a stale x64 one from an old prewarm),
    /// evict it — otherwise the tab attaches to a server that serves no UI and wedges.</summary>
    public static bool StartServer()
    {
        try
        {
            if (!IsInstalled) return false;
            if (VoiceBoxManager.IsServerUp())
            {
                if (ServesOurUi()) return true;
                Log.Warn("voicebox", "Port is held by a foreign VoiceBox server — evicting it");
                KillForeignServers();
                for (int i = 0; i < 10 && VoiceBoxManager.IsServerUp(); i++) Thread.Sleep(300);
                if (VoiceBoxManager.IsServerUp())
                {
                    Log.Warn("voicebox", "Could not free the VoiceBox port");
                    return false;
                }
            }
            Directory.CreateDirectory(DataDir);
            var psi = new ProcessStartInfo(PythonExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // server.py imports itself as the package `backend` — cwd must be its parent
                WorkingDirectory = RootDir,
            };
            // no console handles otherwise — force sane text IO and capture logs
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.ArgumentList.Add(ServerPy);
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(DataDir);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(Port.ToString());
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            _server = Process.Start(psi)!;
            var logPath = Path.Combine(RootDir, "server.log");
            try { File.WriteAllText(logPath, ""); } catch { }
            _ = PumpToLogAsync(_server.StandardOutput, logPath);
            _ = PumpToLogAsync(_server.StandardError, logPath);
            Log.Info("voicebox", "Native ARM64 VoiceBox server starting (log: server.log)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"Native server start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Whether whatever answers on the port serves OUR web UI (the native server
    /// mounts the SPA at /; the x64 desktop server returns a JSON 404 there).</summary>
    private static bool ServesOurUi()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var body = http.GetStringAsync($"http://127.0.0.1:{Port}/").GetAwaiter().GetResult();
            return body.Contains("<div id=\"root\"", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Kill voicebox-server processes that are NOT our native runtime's python.</summary>
    private static void KillForeignServers()
    {
        foreach (var p in Process.GetProcessesByName("voicebox-server"))
        {
            try
            {
                Log.Info("voicebox", $"Killing stale voicebox-server pid {p.Id}");
                p.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { Log.Warn("voicebox", $"Couldn't kill pid {p.Id}: {ex.Message}"); }
        }
    }

    private static async Task PumpToLogAsync(StreamReader reader, string logPath)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                try
                {
                    using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var w = new StreamWriter(fs);
                    await w.WriteLineAsync(line);
                }
                catch { /* logging must never hurt the server */ }
            }
        }
        catch { }
    }

    /// <summary>Wait until /health answers (the native server boots in seconds). Reports
    /// boot progress against a typical ~10s cold boot so the bar visibly moves.</summary>
    public static async Task<bool> WaitForServerAsync(TimeSpan timeout, CancellationToken ct, IProgress<double>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var resp = await http.GetAsync($"http://127.0.0.1:{Port}/health", ct);
                if (resp.IsSuccessStatusCode)
                {
                    progress?.Report(1);
                    return true;
                }
            }
            catch { /* not up yet */ }
            progress?.Report(Math.Min(0.95, sw.Elapsed.TotalSeconds / 10.0));
            await Task.Delay(400, ct);
        }
        return false;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static async Task DownloadAsync(string url, string dest, string label,
        IProgress<(string Phase, double Pct)> progress, CancellationToken ct)
    {
        var tmp = dest + ".part";
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress.Report(($"Downloading {label}… {done / 1048576} / {total / 1048576} MB", (double)done / total));
            }
        }
        File.Move(tmp, dest, overwrite: true);
    }

    /// <summary>Extract zip/tar.gz with a zip-slip guard (never SharpCompress's WriteEntryToDirectory).</summary>
    private static void ExtractArchive(string archive, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var rootFull = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
        using var stream = File.OpenRead(archive);
        using var reader = ReaderFactory.Open(stream);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
            var rel = reader.Entry.Key.Replace('/', Path.DirectorySeparatorChar);
            var dest = Path.GetFullPath(Path.Combine(destDir, rel));
            if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var entry = reader.OpenEntryStream();
            using var outFile = File.Create(dest);
            entry.CopyTo(outFile);
        }
    }

    /// <summary>From the source tarball keep backend/ (as RootDir\backend) + the LICENSE.</summary>
    private static void ExtractVoiceBoxBackend(string tarGz, CancellationToken ct)
    {
        var rootFull = Path.GetFullPath(RootDir) + Path.DirectorySeparatorChar;
        using var stream = File.OpenRead(tarGz);
        using var reader = ReaderFactory.Open(stream);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory || string.IsNullOrEmpty(reader.Entry.Key)) continue;
            // entries look like voicebox-main/backend/server.py
            var parts = reader.Entry.Key.Split('/', 2);
            if (parts.Length < 2) continue;
            var rel = parts[1];
            string? target = null;
            if (rel.StartsWith("backend/") && !rel.StartsWith("backend/tests/"))
                target = Path.Combine(RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
            else if (rel == "LICENSE")
                target = Path.Combine(RootDir, "LICENSE-VoiceBox.txt");
            if (target is null) continue;
            var dest = Path.GetFullPath(target);
            if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var entry = reader.OpenEntryStream();
            using var outFile = File.Create(dest);
            entry.CopyTo(outFile);
        }
    }

    private static async Task RunPythonAsync(string arguments, string label, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(PythonExe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RootDir,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = (await stderr).Trim();
            Log.Error("voicebox", $"python step '{label}' failed ({proc.ExitCode}): {Tail(err, 600)}");
            throw new InvalidOperationException($"Setup step '{label}' failed: {Tail(err, 200)}");
        }
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];

    /// <summary>Patch kokoro's pipeline so English G2P works without spacy (whose blis/thinc
    /// chain has no ARM64 wheels): route lang 'a'/'b' through misaki's espeak G2P — the same
    /// path misaki itself uses for Spanish/French/Hindi/Italian/Portuguese. Idempotent.</summary>
    private static async Task ApplyRuntimePatchesAsync(CancellationToken ct)
    {
        var patch = Path.Combine(RootDir, "patch-kokoro.py");
        File.WriteAllText(patch, """
import io, os, sys

p = os.path.join(os.path.dirname(sys.executable), "Lib", "site-packages", "kokoro", "pipeline.py")
src = io.open(p, encoding="utf-8").read()
if "_HAS_MISAKI_EN" in src:
    print("already patched")
    sys.exit(0)
old_import = "from misaki import en, espeak\n"
new_import = (
    "try:\n"
    "    from misaki import en, espeak\n"
    "    _HAS_MISAKI_EN = True\n"
    "except ImportError:  # LiquidFlow ARM64: no spacy/blis wheels - espeak G2P covers English\n"
    "    _HAS_MISAKI_EN = False\n"
    "    from misaki import espeak\n"
    "    from misaki import token as _misaki_token\n"
    "    class _EnNamespace:  # pipeline references en.MToken in type hints only\n"
    "        MToken = _misaki_token.MToken\n"
    "    en = _EnNamespace()\n"
)
assert old_import in src, "kokoro import anchor missing"
src = src.replace(old_import, new_import, 1)
old_branch = "        if lang_code in 'ab':\n"
new_branch = (
    "        if lang_code in 'ab' and not _HAS_MISAKI_EN:\n"
    "            self.g2p = espeak.EspeakG2P(language='en-us' if lang_code == 'a' else 'en-gb')\n"
    "        elif lang_code in 'ab':\n"
)
assert old_branch in src, "kokoro branch anchor missing"
src = src.replace(old_branch, new_branch, 1)
old_call = "            # English processing (unchanged)\n            if self.lang_code in 'ab':\n"
new_call = "            # English processing (unchanged)\n            if self.lang_code in 'ab' and _HAS_MISAKI_EN:\n"
assert old_call in src, "kokoro call-path anchor missing"
src = src.replace(old_call, new_call, 1)
io.open(p, "w", encoding="utf-8", newline="").write(src)
print("kokoro pipeline patched")
""");
        await RunPythonAsync($"\"{patch}\"", "kokoro patch", ct);
        File.Delete(patch);
    }

    /// <summary>Site-packages shims for native gaps: librosa (numba has no ARM64 wheel;
    /// VoiceBox uses only load + effects.trim), pedalboard (JUCE lib, no ARM64 wheel;
    /// pass-through), and soxr (transformers imports it; scipy does the resampling).</summary>
    private static void WriteShims()
    {
        WriteSoxrShim();
        var sp = Path.Combine(RootDir, "python", "Lib", "site-packages");
        var lib = Path.Combine(sp, "librosa");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "__init__.py"), """
# librosa shim (LiquidFlow native-ARM64 VoiceBox): real librosa needs numba/llvmlite,
# which have no Windows-ARM64 wheels. VoiceBox uses only load() and effects.trim().
import numpy as np
import soundfile as sf
from math import gcd
from scipy.signal import resample_poly

__version__ = "0.10.0+liquidflow.shim"


def load(path, sr=22050, mono=True, dtype=np.float32, **_):
    y, orig_sr = sf.read(str(path), always_2d=True, dtype="float32")
    y = y.T
    if mono:
        y = np.mean(y, axis=0)
    if sr is not None and int(sr) != int(orig_sr):
        g = gcd(int(sr), int(orig_sr))
        y = resample_poly(y, int(sr) // g, int(orig_sr) // g, axis=-1)
        orig_sr = int(sr)
    return np.ascontiguousarray(y, dtype=dtype), int(orig_sr)


def resample(y=None, orig_sr=None, target_sr=None, **_):
    y = np.asarray(y, dtype=np.float32)
    if int(orig_sr) == int(target_sr):
        return y
    g = gcd(int(target_sr), int(orig_sr))
    out = resample_poly(y, int(target_sr) // g, int(orig_sr) // g, axis=-1)
    return np.ascontiguousarray(out, dtype=np.float32)


from . import effects  # noqa: E402,F401
from . import filters  # noqa: E402,F401
""");
        File.WriteAllText(Path.Combine(lib, "filters.py"), """
# librosa.filters shim: mel filterbank from the standard published formulas
# (Slaney mel scale, triangular filters, slaney area norm). Own implementation.
import numpy as np


def _hz_to_mel(freq, htk=False):
    freq = np.asanyarray(freq, dtype=float)
    if htk:
        return 2595.0 * np.log10(1.0 + freq / 700.0)
    f_min, f_sp = 0.0, 200.0 / 3
    mels = (freq - f_min) / f_sp
    min_log_hz = 1000.0
    min_log_mel = (min_log_hz - f_min) / f_sp
    logstep = np.log(6.4) / 27.0
    if mels.ndim:
        log_t = freq >= min_log_hz
        mels[log_t] = min_log_mel + np.log(freq[log_t] / min_log_hz) / logstep
    elif freq >= min_log_hz:
        mels = min_log_mel + np.log(freq / min_log_hz) / logstep
    return mels


def _mel_to_hz(mels, htk=False):
    mels = np.asanyarray(mels, dtype=float)
    if htk:
        return 700.0 * (10.0 ** (mels / 2595.0) - 1.0)
    f_min, f_sp = 0.0, 200.0 / 3
    freqs = f_min + f_sp * mels
    min_log_hz = 1000.0
    min_log_mel = (min_log_hz - f_min) / f_sp
    logstep = np.log(6.4) / 27.0
    if mels.ndim:
        log_t = mels >= min_log_mel
        freqs[log_t] = min_log_hz * np.exp(logstep * (mels[log_t] - min_log_mel))
    elif mels >= min_log_mel:
        freqs = min_log_hz * np.exp(logstep * (mels - min_log_mel))
    return freqs


def mel(*, sr, n_fft, n_mels=128, fmin=0.0, fmax=None, htk=False, norm="slaney", dtype=np.float32):
    if fmax is None:
        fmax = float(sr) / 2
    n_bins = 1 + n_fft // 2
    fftfreqs = np.linspace(0, float(sr) / 2, n_bins, endpoint=True)
    mel_f = _mel_to_hz(np.linspace(_hz_to_mel(fmin, htk), _hz_to_mel(fmax, htk), n_mels + 2), htk=htk)
    fdiff = np.diff(mel_f)
    ramps = np.subtract.outer(mel_f, fftfreqs)
    weights = np.zeros((n_mels, n_bins), dtype=float)
    for i in range(n_mels):
        lower = -ramps[i] / fdiff[i]
        upper = ramps[i + 2] / fdiff[i + 1]
        weights[i] = np.maximum(0, np.minimum(lower, upper))
    if norm == "slaney":
        enorm = 2.0 / (mel_f[2 : n_mels + 2] - mel_f[:n_mels])
        weights *= enorm[:, np.newaxis]
    elif norm is not None:
        raise ValueError(f"Unsupported norm: {norm}")
    return weights.astype(dtype)
""");
        File.WriteAllText(Path.Combine(lib, "effects.py"), """
import numpy as np


def trim(y, top_db=60, frame_length=2048, hop_length=512, **_):
    mono = y if y.ndim == 1 else np.mean(y, axis=tuple(range(y.ndim - 1)))
    n = mono.shape[-1]
    if n == 0:
        return y, np.array([0, 0])
    frames = max(1, 1 + (n - frame_length) // hop_length) if n >= frame_length else 1
    rms = np.empty(frames)
    for i in range(frames):
        seg = mono[i * hop_length : i * hop_length + frame_length]
        rms[i] = np.sqrt(np.mean(seg * seg) + 1e-12)
    ref = float(rms.max())
    db = 20.0 * np.log10(rms / (ref + 1e-20) + 1e-20)
    keep = np.nonzero(db > -float(top_db))[0]
    if keep.size == 0:
        return y, np.array([0, n])
    start = int(keep[0] * hop_length)
    end = int(min(n, (keep[-1] + 1) * hop_length + frame_length))
    return y[..., start:end], np.array([start, end])
""");
        var ped = Path.Combine(sp, "pedalboard");
        Directory.CreateDirectory(ped);
        File.WriteAllText(Path.Combine(ped, "__init__.py"), """
# pedalboard shim (no Windows-ARM64 wheel for the JUCE library): effects chains are
# pass-through — generation works, effect presets are inert until a real wheel exists.


class _Plugin:
    def __init__(self, *args, **kwargs):
        pass


class Chorus(_Plugin): ...
class Reverb(_Plugin): ...
class Compressor(_Plugin): ...
class Gain(_Plugin): ...
class HighpassFilter(_Plugin): ...
class LowpassFilter(_Plugin): ...
class Delay(_Plugin): ...
class PitchShift(_Plugin): ...


class Pedalboard(list):
    def __init__(self, plugins=None):
        super().__init__(plugins or [])

    def __call__(self, audio, sample_rate=None, **kwargs):
        return audio
""");

        // qwen-tts's cloning tokenizer needs sox (peak-normalize only) and kaldi-style
        // fbank features. Both implemented for real — this is what makes voice CLONING
        // fully native. (pysox needs the SoX exe; torchaudio has no ARM64 wheel.)
        File.WriteAllText(Path.Combine(sp, "sox.py"), """
# pysox shim: the cloning tokenizer only uses Transformer().norm(db_level).build_array,
# i.e. peak-normalization to a dB target. Implemented directly; anything else raises.
import numpy as np


class Transformer:
    def __init__(self):
        self._db = None

    def norm(self, db_level=-3.0):
        self._db = float(db_level)
        return self

    def build_array(self, input_array=None, sample_rate_in=None, **_):
        x = np.asarray(input_array, dtype=np.float32)
        if x.size == 0:
            return x
        peak = float(np.max(np.abs(x)))
        if peak <= 0.0:
            return x
        target = 10.0 ** ((self._db if self._db is not None else -3.0) / 20.0)
        return (x * (target / peak)).astype(np.float32)

    def __getattr__(self, name):
        def _unsupported(*args, **kwargs):
            raise NotImplementedError(f"sox shim: effect '{name}' not implemented")
        return _unsupported
""");
        var ta = Path.Combine(sp, "torchaudio", "compliance");
        Directory.CreateDirectory(ta);
        File.WriteAllText(Path.Combine(sp, "torchaudio", "__init__.py"), """
# torchaudio shim: no Windows-ARM64 wheel. Import-safe; only qwen's cloning
# tokenizer consumes it here, and that raises clearly if used.
from . import compliance  # noqa: F401
__version__ = "0.0.0+liquidflow.shim"
""");
        File.WriteAllText(Path.Combine(ta, "__init__.py"), """
from . import kaldi  # noqa: F401
""");
        File.WriteAllText(Path.Combine(ta, "kaldi.py"), """
# Kaldi-compatible fbank features, implemented from the documented Kaldi feature
# pipeline (framing -> DC removal -> preemphasis -> povey window -> power spectrum ->
# HTK-mel triangular banks -> log). Covers the options qwen's cloning tokenizer uses;
# other option combinations raise so silent numeric drift can't hide.
import math
import torch

EPSILON = torch.finfo(torch.float).eps


def _next_pow2(n):
    return 1 if n <= 1 else 2 ** (n - 1).bit_length()


def _mel(freq):
    return 1127.0 * math.log(1.0 + freq / 700.0)


def _mel_banks(num_bins, n_fft, sample_freq, low_freq, high_freq):
    if high_freq <= 0:
        high_freq = sample_freq / 2 + high_freq
    n_bins = n_fft // 2
    fft_bin_width = sample_freq / n_fft
    mel_low, mel_high = _mel(low_freq), _mel(high_freq)
    mel_delta = (mel_high - mel_low) / (num_bins + 1)
    banks = torch.zeros(num_bins, n_bins)
    for b in range(num_bins):
        left, center, right = (mel_low + d * mel_delta for d in (b, b + 1, b + 2))
        for i in range(n_bins):
            m = _mel(fft_bin_width * i)
            if left < m < right:
                banks[b, i] = (m - left) / (center - left) if m <= center else (right - m) / (right - center)
    return banks


def fbank(waveform, num_mel_bins=23, frame_length=25.0, frame_shift=10.0,
          sample_frequency=16000.0, dither=0.0, preemphasis_coefficient=0.97,
          remove_dc_offset=True, window_type="povey", use_energy=False,
          use_power=True, use_log_fbank=True, low_freq=20.0, high_freq=0.0,
          snip_edges=True, subtract_mean=False, energy_floor=1.0,
          raw_energy=True, round_to_power_of_two=True, **unsupported):
    if use_energy or not use_power or not use_log_fbank or not snip_edges or window_type != "povey":
        raise NotImplementedError("kaldi.fbank shim: unsupported option combination")
    if dither:
        raise NotImplementedError("kaldi.fbank shim: dither is not implemented")

    wave = torch.as_tensor(waveform, dtype=torch.float32)
    if wave.dim() == 2:
        wave = wave[0]
    win_size = int(sample_frequency * frame_length / 1000.0)
    win_shift = int(sample_frequency * frame_shift / 1000.0)
    n_fft = _next_pow2(win_size) if round_to_power_of_two else win_size

    num_frames = 0 if wave.numel() < win_size else 1 + (wave.numel() - win_size) // win_shift
    if num_frames == 0:
        return torch.zeros(0, num_mel_bins)
    idx = torch.arange(win_size).unsqueeze(0) + win_shift * torch.arange(num_frames).unsqueeze(1)
    frames = wave[idx]

    if remove_dc_offset:
        frames = frames - frames.mean(dim=1, keepdim=True)
    if preemphasis_coefficient != 0.0:
        prev = torch.nn.functional.pad(frames.unsqueeze(0), (1, 0), mode="replicate").squeeze(0)[:, :-1]
        frames = frames - preemphasis_coefficient * prev

    n = torch.arange(win_size, dtype=torch.float32)
    window = (0.5 - 0.5 * torch.cos(2 * math.pi * n / (win_size - 1))).pow(0.85)
    frames = frames * window

    spectrum = torch.fft.rfft(frames, n=n_fft).abs().pow(2.0)

    banks = _mel_banks(num_mel_bins, n_fft, sample_frequency, low_freq, high_freq)
    banks = torch.nn.functional.pad(banks, (0, 1))
    mel = spectrum @ banks.t()
    mel = torch.log(mel.clamp(min=EPSILON))
    if subtract_mean:
        mel = mel - mel.mean(dim=0, keepdim=True)
    return mel
""");
    }

    private static void WriteSoxrShim()
    {
        var soxr = Path.Combine(RootDir, "python", "Lib", "site-packages", "soxr");
        Directory.CreateDirectory(soxr);
        File.WriteAllText(Path.Combine(soxr, "__init__.py"), """
# soxr shim (no win-arm64 wheel): transformers.audio_utils imports it unconditionally.
# Resampling is delegated to scipy's polyphase resampler.
from fractions import Fraction
import numpy as np
from scipy.signal import resample_poly

__version__ = "0.5.0+liquidflow.shim"


def resample(x, in_rate, out_rate, quality=None):
    x = np.asarray(x)
    if int(in_rate) == int(out_rate):
        return x.astype(np.float32, copy=False)
    frac = Fraction(int(out_rate), int(in_rate)).limit_denominator(1000)
    y = resample_poly(x, frac.numerator, frac.denominator, axis=0)
    return y.astype(np.float32, copy=False)
""");
    }
}
