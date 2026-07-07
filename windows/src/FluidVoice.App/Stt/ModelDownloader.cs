using System.IO;
using System.Net.Http;
using FluidVoice.Core;

namespace FluidVoice.Stt;

public enum ModelPreparationPhase { PreparingDownload, Downloading, Loading, Ready, Failed }

public sealed record ModelPreparationProgress(ModelPreparationPhase Phase, double Fraction, string? Error = null);

/// <summary>
/// Downloads model files with the mac app's behavior (WhisperProvider.swift:246-401 /
/// ModelDownloader.swift): 3 retries with 1s/2s/4s backoff, HTML/proxy-page sniffing,
/// byte-exact size validation, atomic move into place.
/// </summary>
public static class ModelDownloader
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    static ModelDownloader()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidVoice-Windows/1.6");
    }

    /// <summary>
    /// Download whatever the model needs: the single GGML file for Whisper, or every
    /// ModelFile (skipping ones already valid) with aggregate progress for sherpa models.
    /// </summary>
    public static async Task DownloadModelAsync(
        SpeechModelInfo model, IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        if (model.Files is not { Count: > 0 } files)
        {
            await DownloadAsync(SpeechModels.DownloadUrl(model), model.LocalPath, model.ExpectedBytes, progress, ct);
            return;
        }

        long totalBytes = files.Sum(f => f.Bytes);
        long doneBytes = 0;
        foreach (var file in files)
        {
            var destination = Path.Combine(model.LocalPath, file.RelativePath);
            if (File.Exists(destination) && new FileInfo(destination).Length == file.Bytes)
            {
                doneBytes += file.Bytes;
                continue;
            }
            var baseBytes = doneBytes;
            var filePart = new Progress<ModelPreparationProgress>(p =>
            {
                if (p.Phase == ModelPreparationPhase.Downloading)
                    progress?.Report(new(ModelPreparationPhase.Downloading,
                        Math.Min(0.999, (baseBytes + p.Fraction * file.Bytes) / totalBytes)));
                else if (p.Phase == ModelPreparationPhase.Failed)
                    progress?.Report(p);
            });
            await DownloadAsync(file.Url, destination, file.Bytes, filePart, ct);
            doneBytes += file.Bytes;
        }
        progress?.Report(new(ModelPreparationPhase.Downloading, 1.0));
    }

    public static async Task DownloadAsync(
        string url, string destinationPath, long expectedBytes,
        IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new(ModelPreparationPhase.PreparingDownload, 0));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct); // 1s, 2s
            try
            {
                await DownloadOnceAsync(url, destinationPath, expectedBytes, progress, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                CleanupPartial(destinationPath);
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log.Warn("download", $"Attempt {attempt + 1} failed for {url}: {ex.Message}");
                CleanupPartial(destinationPath);
            }
        }
        progress?.Report(new(ModelPreparationPhase.Failed, 0, lastError?.Message));
        throw new InvalidOperationException($"Download failed after 3 attempts: {lastError?.Message}", lastError);
    }

    private static async Task DownloadOnceAsync(
        string url, string destinationPath, long expectedBytes,
        IProgress<ModelPreparationProgress>? progress, CancellationToken ct)
    {
        var tempPath = destinationPath + ".download";
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} from server");

        var contentLength = response.Content.Headers.ContentLength;
        if (expectedBytes > 0 && contentLength.HasValue && contentLength.Value != expectedBytes)
            Log.Warn("download", $"Content-Length {contentLength} != expected {expectedBytes} for {url}");

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(tempPath))
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            bool sniffed = false;
            int read;
            long reportEvery = Math.Max(1, (contentLength ?? expectedBytes) / 200);
            long nextReport = 0;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (!sniffed && total == 0 && read >= 2)
                {
                    // Reject HTML/proxy block pages masquerading as model files
                    if (buffer[0] == (byte)'<' && (buffer[1] is (byte)'!' or (byte)'?' or (byte)'/' ||
                        char.IsAsciiLetter((char)buffer[1])))
                        throw new InvalidOperationException("Server returned a web page instead of the model file");
                    sniffed = true;
                }
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                if (total >= nextReport)
                {
                    nextReport = total + reportEvery;
                    var denom = (double)(contentLength ?? expectedBytes);
                    var fraction = denom > 0 ? Math.Min(0.999, total / denom) : 0;
                    progress?.Report(new(ModelPreparationPhase.Downloading, fraction));
                }
            }
        }

        var actual = new FileInfo(tempPath).Length;
        if (expectedBytes > 0 && actual != expectedBytes)
            throw new InvalidOperationException($"Downloaded size {actual:N0} != expected {expectedBytes:N0} bytes");

        File.Move(tempPath, destinationPath, overwrite: true);
        progress?.Report(new(ModelPreparationPhase.Downloading, 1.0));
    }

    private static void CleanupPartial(string destinationPath)
    {
        try { File.Delete(destinationPath + ".download"); } catch { }
    }
}
