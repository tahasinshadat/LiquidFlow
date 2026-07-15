namespace FluidVoice.Core;

/// <summary>One recorded meeting: the live transcript captured from system audio (+ mic) and
/// the AI-generated notes/summary. Persisted by <see cref="MeetingStore"/>.</summary>
public sealed class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; }
    public string Title { get; set; } = "";
    public string Transcript { get; set; } = "";
    /// <summary>AI summary (markdown-ish). Empty if no AI provider was configured.</summary>
    public string Summary { get; set; } = "";
}
