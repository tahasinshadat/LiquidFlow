using System.Text;
using FluidVoice.Ai;
using FluidVoice.Audio;
using FluidVoice.Core;
using FluidVoice.Stt;
using FluidVoice.Text;

namespace FluidVoice.App;

/// <summary>
/// Drives a meeting recording end to end: captures system audio (+ mic), transcribes the growing
/// buffer in ~20s chunks for a live transcript, and on stop transcribes the tail, asks the LLM for
/// a summary, and saves the meeting. A singleton so recording survives navigating away from the tab.
/// </summary>
public sealed class MeetingService
{
    public static MeetingService Instance { get; } = new();
    private MeetingService() { }

    private MeetingRecorder? _recorder;
    private ISpeechEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _startedAt;
    private int _cursor;
    private readonly StringBuilder _transcript = new();
    private readonly object _tsync = new();

    private const int ChunkSamples = 20 * MeetingRecorder.Rate; // ~20s per transcription chunk

    public bool IsRecording { get; private set; }
    public bool IsBusy { get; private set; } // stopping/summarizing

    public DateTime StartedAt => _startedAt;

    public string Transcript
    {
        get { lock (_tsync) return _transcript.ToString(); }
    }

    /// <summary>Recording started or stopped.</summary>
    public event Action? StateChanged;
    /// <summary>Live transcript grew; arg = full transcript so far.</summary>
    public event Action<string>? TranscriptUpdated;
    /// <summary>Human-readable status ("Summarizing…"); "" clears it.</summary>
    public event Action<string>? StatusChanged;

    public void Start(ISpeechEngine engine, bool includeMic)
    {
        if (IsRecording) return;
        _engine = engine;
        _recorder = new MeetingRecorder(includeMic);
        lock (_tsync) _transcript.Clear();
        _cursor = 0;
        _startedAt = DateTime.Now;
        _recorder.Start();
        IsRecording = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => TranscribeLoopAsync(_cts.Token));
        StateChanged?.Invoke();
    }

    private async Task TranscribeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _recorder is not null)
        {
            await Task.Delay(1500, ct).ContinueWith(_ => { });
            if (ct.IsCancellationRequested) break;
            if ((_recorder?.MixedLength ?? 0) - _cursor < ChunkSamples) continue; // wait for a full chunk
            await TranscribePendingAsync(ct, drainAll: false);
        }
    }

    private async Task TranscribePendingAsync(CancellationToken ct, bool drainAll)
    {
        var recorder = _recorder;
        var engine = _engine;
        if (recorder is null || engine is null) return;

        int end = recorder.MixedLength;
        int take = end - _cursor;
        if (take <= 0) return;
        if (!drainAll && take < MeetingRecorder.Rate) return; // <1s of new audio, wait

        var chunk = recorder.SnapshotMixedFrom(_cursor);
        _cursor = end;
        try
        {
            var raw = await engine.TranscribeAsync(Dsp.Normalize(chunk), ct);
            var text = TranscriptFormatter.Process(raw).Trim();
            if (text.Length > 0)
            {
                lock (_tsync)
                {
                    if (_transcript.Length > 0) _transcript.Append(' ');
                    _transcript.Append(text);
                }
                TranscriptUpdated?.Invoke(Transcript);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("meeting", $"Chunk transcription failed: {ex.Message}");
        }
    }

    /// <summary>Stop capture, transcribe the tail, summarize, and persist. Returns the saved meeting.</summary>
    public async Task<Meeting?> StopAsync()
    {
        if (!IsRecording) return null;
        IsRecording = false;
        IsBusy = true;
        StateChanged?.Invoke();

        _cts?.Cancel();
        try { if (_loop is not null) await _loop; } catch { }

        var duration = DateTime.Now - _startedAt;
        _recorder?.Stop();

        StatusChanged?.Invoke("Finishing transcription…");
        await TranscribePendingAsync(CancellationToken.None, drainAll: true);

        _recorder?.Dispose();
        _recorder = null;

        var transcript = Transcript;
        string summary = "";
        if (!string.IsNullOrWhiteSpace(transcript) && MeetingSummarizer.IsAvailable)
        {
            StatusChanged?.Invoke("Summarizing…");
            try { summary = await MeetingSummarizer.SummarizeAsync(transcript, CancellationToken.None) ?? ""; }
            catch (Exception ex) { Log.Warn("meeting", $"Summary failed: {ex.Message}"); }
        }

        var meeting = new Meeting
        {
            StartedAt = _startedAt,
            DurationSeconds = duration.TotalSeconds,
            Title = $"Meeting · {_startedAt:MMM d, h:mm tt}",
            Transcript = transcript,
            Summary = summary,
        };
        MeetingStore.Save(meeting);

        IsBusy = false;
        StatusChanged?.Invoke("");
        StateChanged?.Invoke();
        return meeting;
    }
}
