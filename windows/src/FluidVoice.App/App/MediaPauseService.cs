using FluidVoice.Core;
using Windows.Media.Control;

namespace FluidVoice.App;

/// <summary>
/// Pauses playing media while dictating, resumes only if we paused it
/// (MediaPlaybackService.swift). Uses the WinRT system media session —
/// works with Spotify, browsers, media players that integrate with the
/// Windows media flyout.
/// </summary>
public static class MediaPauseService
{
    private static bool _didPause;
    private static readonly object Sync = new();

    public static void PauseIfPlaying()
    {
        if (!Settings.Current.PauseMediaDuringTranscription) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session is null) return;
                var info = session.GetPlaybackInfo();
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    var ok = await session.TryPauseAsync();
                    lock (Sync) _didPause = ok;
                    if (ok) Log.Info("media", "Paused system media for dictation");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("media", $"PauseIfPlaying failed: {ex.Message}");
            }
        });
    }

    public static void ResumeIfWePaused()
    {
        bool shouldResume;
        lock (Sync)
        {
            shouldResume = _didPause;
            _didPause = false; // resume-once guard (MediaPlaybackService.swift:49-54)
        }
        if (!shouldResume) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session is not null)
                {
                    await session.TryPlayAsync(); // explicit play, never toggle
                    Log.Info("media", "Resumed system media");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("media", $"Resume failed: {ex.Message}");
            }
        });
    }
}
