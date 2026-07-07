using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FluidVoice.Core;
using FluidVoice.Stt;

namespace FluidVoice.Ai;

public sealed record LocalAiModel(string Id, string DisplayName, string Description, string Url, string FileName, long ExpectedBytes);

/// <summary>
/// The OPEN substitute for the proprietary "Fluid Intelligence" runtime:
/// a managed llama.cpp llama-server child process (ARM64-native CPU build)
/// serving a small instruct model over an OpenAI-compatible localhost API.
/// Same prompts, same gating, same post-processing as the cloud path —
/// nothing leaves the machine.
/// </summary>
public static class LocalAiServer
{
    // Pinned llama.cpp release (github.com/ggml-org/llama.cpp) — CPU builds, no CUDA needed on ARM64.
    private const string LlamaTag = "b9892";
    private static readonly string RuntimeZipUrl = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaTag}/llama-{LlamaTag}-bin-win-cpu-arm64.zip"
        : $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaTag}/llama-{LlamaTag}-bin-win-cpu-x64.zip";
    private static readonly long RuntimeZipBytes = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? 11_379_052 : 17_482_680;

    public static readonly IReadOnlyList<LocalAiModel> Models = new List<LocalAiModel>
    {
        new("qwen2.5-1.5b-instruct-q4", "Qwen2.5 1.5B Instruct (recommended)",
            "Fast on-device enhancement. Apache-2.0. ~1.1 GB.",
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            "qwen2.5-1.5b-instruct-q4_k_m.gguf", 1_117_320_736),
        new("qwen2.5-0.5b-instruct-q4", "Qwen2.5 0.5B Instruct (light)",
            "Smallest and fastest; lower quality. Apache-2.0. ~0.4 GB.",
            "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf",
            "qwen2.5-0.5b-instruct-q4_k_m.gguf", 0),
        new("qwen2.5-3b-instruct-q4", "Qwen2.5 3B Instruct (quality)",
            "Best local quality; slower. ~2 GB.",
            "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
            "qwen2.5-3b-instruct-q4_k_m.gguf", 0),
    };

    private static readonly object Sync = new();
    private static Process? _process;
    private static int _port;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static LocalAiModel SelectedModel =>
        Models.FirstOrDefault(m => m.Id == Settings.Current.LocalAiModelId) ?? Models[0];

    public static string ModelName => SelectedModel.Id;
    public static string BaseUrl => $"http://127.0.0.1:{(_port == 0 ? 8788 : _port)}/v1";
    public static bool IsRunning
    {
        get { lock (Sync) return _process is { HasExited: false }; }
    }

    private static string ServerExe => Path.Combine(AppPaths.LocalAiRuntimeDir, "llama-server.exe");
    private static string ModelPath(LocalAiModel m) => Path.Combine(AppPaths.LocalAiModelDir, m.FileName);

    public static bool IsRuntimeInstalled() => File.Exists(ServerExe);

    public static bool IsModelInstalled()
    {
        var m = SelectedModel;
        var path = ModelPath(m);
        if (!File.Exists(path)) return false;
        var len = new FileInfo(path).Length;
        return m.ExpectedBytes > 0 ? len == m.ExpectedBytes : len > 100_000_000;
    }

    /// <summary>Download the llama.cpp runtime + selected model (with progress).</summary>
    public static async Task EnsureInstalledAsync(IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.LocalAiRuntimeDir);
        Directory.CreateDirectory(AppPaths.LocalAiModelDir);

        if (!IsRuntimeInstalled())
        {
            var zipPath = Path.Combine(AppPaths.LocalAiDir, "llama-runtime.zip");
            await ModelDownloader.DownloadAsync(RuntimeZipUrl, zipPath, RuntimeZipBytes, progress, ct);
            ZipFile.ExtractToDirectory(zipPath, AppPaths.LocalAiRuntimeDir, overwriteFiles: true);
            File.Delete(zipPath);
            // some releases nest binaries under build/bin
            if (!File.Exists(ServerExe))
            {
                var nested = Directory.GetFiles(AppPaths.LocalAiRuntimeDir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (nested is not null)
                {
                    foreach (var f in Directory.GetFiles(Path.GetDirectoryName(nested)!))
                        File.Copy(f, Path.Combine(AppPaths.LocalAiRuntimeDir, Path.GetFileName(f)), overwrite: true);
                }
            }
            if (!IsRuntimeInstalled())
                throw new InvalidOperationException("llama-server.exe not found in downloaded runtime");
            Log.Info("localai", $"Installed llama.cpp runtime {LlamaTag}");
        }

        var model = SelectedModel;
        if (!IsModelInstalled())
        {
            await ModelDownloader.DownloadAsync(model.Url, ModelPath(model), model.ExpectedBytes, progress, ct);
            Log.Info("localai", $"Downloaded local AI model {model.Id}");
        }
    }

    /// <summary>Starts llama-server if needed; returns once /health responds.</summary>
    public static async Task EnsureRunningAsync(CancellationToken ct)
    {
        lock (Sync)
        {
            if (_process is { HasExited: false }) return;
        }
        if (!IsRuntimeInstalled() || !IsModelInstalled())
            throw new InvalidOperationException("Local AI is not set up yet — download it in Settings → AI Enhancement");

        var port = GetFreePort();
        var model = SelectedModel;
        var psi = new ProcessStartInfo
        {
            FileName = ServerExe,
            Arguments = $"-m \"{ModelPath(model)}\" --host 127.0.0.1 --port {port} " +
                        $"-c {Math.Clamp(Settings.Current.LocalAiContextTokens, 2048, 8192)} " +
                        "--threads " + Math.Clamp(Environment.ProcessorCount - 2, 2, 8) +
                        " --no-webui --log-disable",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppPaths.LocalAiRuntimeDir,
        };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start llama-server");
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        lock (Sync)
        {
            _process = proc;
            _port = port;
        }
        Log.Info("localai", $"llama-server starting on port {port} with {model.Id}");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (proc.HasExited)
                throw new InvalidOperationException($"llama-server exited with code {proc.ExitCode}");
            try
            {
                var resp = await Http.GetAsync($"http://127.0.0.1:{port}/health", ct);
                if (resp.IsSuccessStatusCode)
                {
                    Log.Info("localai", "llama-server healthy");
                    return;
                }
            }
            catch { }
            await Task.Delay(250, ct);
        }
        throw new TimeoutException("llama-server did not become healthy in 60s");
    }

    public static void Stop()
    {
        lock (Sync)
        {
            if (_process is { HasExited: false })
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }
            _process = null;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
