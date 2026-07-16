using Microsoft.Win32;
using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>Launch at login via the Run registry key (SettingsStore+LaunchAtStartup.swift analog).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiquidFlow";
    private const string LegacyValueName = "FluidVoice"; // pre-rename autostart entry

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            // Always clear the old FluidVoice autostart so it can't launch the removed exe.
            try { key.DeleteValue(LegacyValueName, throwOnMissingValue: false); } catch { }
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
