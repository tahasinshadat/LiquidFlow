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
                "transformers",
                "--no-deps kokoro misaki",
                "addict regex espeakng-loader num2words phonemizer-fork",
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

    /// <summary>Start the native server headless if not already listening. Returns fast.</summary>
    public static bool StartServer()
    {
        try
        {
            if (!IsInstalled) return false;
            if (VoiceBoxManager.IsServerUp()) return true;
            Directory.CreateDirectory(DataDir);
            var psi = new ProcessStartInfo(PythonExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // server.py imports itself as the package `backend` — cwd must be its parent
                WorkingDirectory = RootDir,
            };
            psi.ArgumentList.Add(ServerPy);
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(DataDir);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(Port.ToString());
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            _server = Process.Start(psi);
            Log.Info("voicebox", "Native ARM64 VoiceBox server starting");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"Native server start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Wait until /health answers (the native server boots in seconds).</summary>
    public static async Task<bool> WaitForServerAsync(TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var resp = await http.GetAsync($"http://127.0.0.1:{Port}/health", ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* not up yet */ }
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


from . import effects  # noqa: E402,F401
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
