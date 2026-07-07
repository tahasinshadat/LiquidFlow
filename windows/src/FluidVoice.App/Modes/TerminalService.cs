using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluidVoice.Core;

namespace FluidVoice.Modes;

public sealed record CommandResult(bool Success, string Command, string Output, string? Error, int ExitCode, int ExecutionTimeMs);

/// <summary>
/// Shell execution for Command Mode (TerminalService.swift, adapted:
/// /bin/zsh → PowerShell). 30s timeout, stdout/stderr captured, JSON result.
/// </summary>
public static class TerminalService
{
    public const int TimeoutSeconds = 30;

    public static async Task<CommandResult> ExecuteAsync(string command, string? workingDirectory, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : workingDirectory;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return new CommandResult(false, command, "", "Failed to start PowerShell", -1, (int)sw.ElapsedMilliseconds);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(false, command, await SafeRead(stdoutTask),
                    $"Command timed out after {TimeoutSeconds} seconds", -1, (int)sw.ElapsedMilliseconds);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            sw.Stop();
            var success = proc.ExitCode == 0;
            return new CommandResult(success, command, Truncate(stdout, 12000),
                string.IsNullOrWhiteSpace(stderr) ? null : Truncate(stderr, 4000),
                proc.ExitCode, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, command, "", ex.Message, -1, (int)sw.ElapsedMilliseconds);
        }
    }

    public static string ToJson(CommandResult result, string? purpose) => JsonSerializer.Serialize(new
    {
        success = result.Success,
        command = result.Command,
        output = result.Output,
        error = result.Error,
        exitCode = result.ExitCode,
        executionTimeMs = result.ExecutionTimeMs,
        purpose,
    });

    private static async Task<string> SafeRead(Task<string> task)
    {
        try { return await task; } catch { return ""; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"\n… (truncated, {s.Length} chars total)";
}
