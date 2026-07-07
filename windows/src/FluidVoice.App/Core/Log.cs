using System.IO;
using System.Text;

namespace FluidVoice.Core;

/// <summary>Small rolling file logger (mirrors FileLogger.swift in spirit).</summary>
public static class Log
{
    private static readonly object Sync = new();
    private static string? _file;
    private const long MaxBytes = 4 * 1024 * 1024;

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            _file = Path.Combine(AppPaths.LogDir, "fluidvoice.log");
            Info("log", $"--- FluidVoice started {DateTime.Now:O} ---");
        }
        catch
        {
            _file = null;
        }
    }

    public static void Info(string area, string message) => Write("INFO", area, message);
    public static void Warn(string area, string message) => Write("WARN", area, message);
    public static void Error(string area, string message, Exception? ex = null)
        => Write("ERROR", area, ex is null ? message : $"{message}: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string area, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {area}: {message}";
        System.Diagnostics.Debug.WriteLine(line);
        if (_file is null) return;
        lock (Sync)
        {
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = Path.Combine(AppPaths.LogDir, "fluidvoice.old.log");
                    File.Delete(old);
                    File.Move(_file, old);
                }
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // never crash the app for logging
            }
        }
    }
}
