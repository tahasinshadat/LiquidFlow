using Microsoft.Win32;
using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>Launch at login via the Run registry key (SettingsStore+LaunchAtStartup.swift analog).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FluidVoice";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info("startup", $"Launch at startup {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log.Error("startup", "Failed to update startup registration", ex);
        }
    }
}
