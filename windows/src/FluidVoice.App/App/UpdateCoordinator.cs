using FluidVoice.Core;

namespace FluidVoice.App;

/// <summary>
/// Owns the "is an update available?" state so the tray and main window can both surface a
/// one-click Update button + notification. Checks run on startup, hourly, and on demand; the
/// actual install reuses <see cref="Updater.DownloadAndRunAsync"/> (installer replaces the app).
/// </summary>
public static class UpdateCoordinator
{
    /// <summary>The newest available update, or null when we're current.</summary>
    public static UpdateInfo? Pending { get; private set; }

    /// <summary>Raised (on a background thread) whenever <see cref="Pending"/> changes. UI
    /// subscribers must marshal to their dispatcher.</summary>
    public static event Action<UpdateInfo?>? Changed;

    private static volatile bool _installing;
    private static string _lastNotified = "";

    /// <summary>Re-check all sources. <paramref name="interactive"/> = user clicked "Check for
    /// updates" (so we also tell them when they're already current).</summary>
    public static async Task RefreshAsync(bool interactive)
    {
        UpdateInfo? found;
        try
        {
            found = await Updater.CheckAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warn("updater", $"Update check failed: {ex.Message}");
            if (interactive) Notifications.Show("LiquidFlow", "Couldn't check for updates right now.");
            return;
        }

        var changed = Pending?.Version != found?.Version;
        Pending = found;
        if (changed) Changed?.Invoke(found);

        if (found is not null)
        {
            // Notify once per new version (or whenever the user asked explicitly).
            if (interactive || _lastNotified != found.Version)
            {
                _lastNotified = found.Version;
                Notifications.Show("Update available",
                    $"LiquidFlow {found.Version} is ready — click to install.");
            }
        }
        else if (interactive)
        {
            Notifications.Show("LiquidFlow", "You're on the latest version.");
        }
    }

    /// <summary>Install the pending update (download if remote, then run the silent installer).</summary>
    public static async Task<bool> InstallAsync()
    {
        var update = Pending;
        if (update is null || _installing) return false;
        _installing = true;
        try
        {
            Notifications.Show("Updating LiquidFlow", $"Installing {update.Version}…");
            return await Updater.DownloadAndRunAsync(update, CancellationToken.None);
        }
        finally
        {
            _installing = false;
        }
    }

    /// <summary>Poll periodically (hourly) so a build dropped in the update folder is noticed
    /// without restarting the app. Fire-and-forget for the app's lifetime.</summary>
    public static void StartPeriodicChecks()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try { await Task.Delay(TimeSpan.FromHours(1)); } catch { }
                if (!Settings.Current.AutoUpdateCheckEnabled) continue;
                await RefreshAsync(interactive: false);
            }
        });
    }
}
