using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Stt;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Meeting notes: record system audio (+ mic), watch the transcript build live, and get an AI
/// summary when you stop. Past meetings are listed below. Recording state lives in the
/// MeetingService singleton, so it survives navigating away and back.
/// </summary>
public sealed class MeetingsTab : StackPanel
{
    private readonly App.DictationCoordinator? _coordinator;
    private readonly Button _startBtn;
    private readonly TextBlock _status = new() { Foreground = Theme.SubtleBrush, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _timer = new() { Foreground = Theme.TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _micToggle;
    private readonly TextBox _liveBox;
    private readonly StackPanel _pastHost = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public MeetingsTab(App.DictationCoordinator? coordinator = null)
    {
        _coordinator = coordinator;
        var svc = MeetingService.Instance;

        Children.Add(PageChrome.HeaderRow("Meetings", null, null));
        Children.Add(BuildHero());

        // ---- recorder card ----
        var rec = new StackPanel();
        _startBtn = new Button
        {
            Padding = new Thickness(20, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.InkBrush,
            Foreground = new SolidColorBrush(Theme.InkText),
            BorderThickness = new Thickness(0),
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        _startBtn.Click += (_, _) => OnStartStop();

        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        topRow.Children.Add(_startBtn);
        var timerBox = new Border { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = _timer };
        topRow.Children.Add(timerBox);
        rec.Children.Add(topRow);

        _micToggle = Theme.Toggle("Include my microphone", Settings.Current.MeetingIncludeMic, v =>
        {
            Settings.Current.MeetingIncludeMic = v;
            Settings.Current.Save("meeting");
        });
        _micToggle.Margin = new Thickness(0, 12, 0, 0);
        rec.Children.Add(_micToggle);
        rec.Children.Add(_status);

        _liveBox = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 160, MaxHeight = 300, Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Theme.CardInner),
            Foreground = Theme.TextBrush,
            BorderBrush = Theme.HairlineBrush,
        };
        rec.Children.Add(_liveBox);
        Children.Add(Theme.Card2(rec));

        // ---- past meetings ----
        Children.Add(new TextBlock { Text = "Past meetings", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, Margin = new Thickness(2, 8, 0, 10) });
        Children.Add(_pastHost);

        _tick.Tick += (_, _) => UpdateTimer();

        Loaded += (_, _) =>
        {
            svc.StateChanged += OnStateChanged;
            svc.TranscriptUpdated += OnTranscriptUpdated;
            svc.StatusChanged += OnStatusChanged;
            MeetingStore.Changed += OnMeetingsChanged;
            SyncFromState();
            RebuildPast();
        };
        Unloaded += (_, _) =>
        {
            svc.StateChanged -= OnStateChanged;
            svc.TranscriptUpdated -= OnTranscriptUpdated;
            svc.StatusChanged -= OnStatusChanged;
            MeetingStore.Changed -= OnMeetingsChanged;
            _tick.Stop();
        };
    }

    private static UIElement BuildHero()
    {
        var content = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(44, 34, 44, 34),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 780,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        content.Children.Add(new TextBlock
        {
            Text = "Every meeting, remembered.",
            FontFamily = Theme.DisplaySerif,
            FontSize = 30,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 12),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Capture your PC's audio and your mic — watch the transcript build live, get an AI summary when you stop. All on-device.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
        });
        var hero = PageChrome.DarkHero(content);
        ((System.Windows.Controls.Border)hero).MinHeight = 190;
        ((System.Windows.Controls.Border)hero).Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }


    private void OnStateChanged() => Dispatcher.BeginInvoke(SyncFromState);
    private void OnTranscriptUpdated(string t) => Dispatcher.BeginInvoke(() => { _liveBox.Text = t; _liveBox.CaretIndex = t.Length; _liveBox.ScrollToEnd(); });
    private void OnStatusChanged(string s) => Dispatcher.BeginInvoke(() => _status.Text = s);
    private void OnMeetingsChanged() => Dispatcher.BeginInvoke(RebuildPast);

    private void SyncFromState()
    {
        var svc = MeetingService.Instance;
        bool recording = svc.IsRecording;
        _micToggle.IsEnabled = !recording && !svc.IsBusy;
        _startBtn.IsEnabled = !svc.IsBusy;
        _startBtn.Content = recording ? "◼  Stop & summarize" : (svc.IsBusy ? "Working…" : "●  Start meeting");
        _startBtn.Background = recording ? new SolidColorBrush(Theme.Danger) : Theme.InkBrush;

        var t = svc.Transcript;
        if (recording || t.Length > 0)
        {
            _liveBox.Visibility = Visibility.Visible;
            if (_liveBox.Text != t) { _liveBox.Text = t; _liveBox.ScrollToEnd(); }
        }
        else
        {
            _liveBox.Visibility = Visibility.Collapsed;
        }

        if (recording) { _tick.Start(); UpdateTimer(); }
        else { _tick.Stop(); _timer.Text = ""; }
    }

    private void UpdateTimer()
    {
        var svc = MeetingService.Instance;
        if (!svc.IsRecording) { _timer.Text = ""; return; }
        var e = DateTime.Now - svc.StartedAt;
        _timer.Text = $"● {(int)e.TotalMinutes:00}:{e.Seconds:00}";
        _timer.Foreground = new SolidColorBrush(Theme.Danger);
    }

    private async void OnStartStop()
    {
        var svc = MeetingService.Instance;
        if (svc.IsRecording)
        {
            _status.Text = "Stopping…";
            await svc.StopAsync();
            return;
        }
        if (_coordinator is null) { _status.Text = "Recording engine unavailable."; return; }
        var model = SpeechModels.Selected();
        if (!model.IsDownloaded) { _status.Text = "Download a speech model first (Speech Models)."; return; }
        try
        {
            _startBtn.IsEnabled = false;
            _status.Text = "Preparing model…";
            var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
            svc.Start(engine, Settings.Current.MeetingIncludeMic);
            _status.Text = MeetingSummarizerHint();
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't start: {ex.Message}";
            _startBtn.IsEnabled = true;
        }
    }

    private static string MeetingSummarizerHint() =>
        Ai.MeetingSummarizer.IsAvailable
            ? "Recording… the transcript appears live; you'll get an AI summary when you stop."
            : "Recording… (no AI provider set, so you'll get the transcript only — configure one in Speech Models for summaries).";

    private void RebuildPast()
    {
        _pastHost.Children.Clear();
        var meetings = MeetingStore.All;
        if (meetings.Count == 0)
        {
            _pastHost.Children.Add(new TextBlock { Text = "No meetings yet.", Foreground = Theme.SubtleBrush, Margin = new Thickness(2, 4, 0, 0) });
            return;
        }
        foreach (var m in meetings)
            _pastHost.Children.Add(MeetingRow(m));
    }

    private UIElement MeetingRow(Meeting m)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = m.Title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = Theme.TextBrush });
        title.Children.Add(new TextBlock
        {
            Text = $"{TimeSpan.FromSeconds(m.DurationSeconds):h\\:mm\\:ss} · {(m.Summary.Length > 0 ? "summarized" : "transcript only")}",
            FontSize = 11.5, Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 2, 0, 0),
        });
        header.Children.Add(title);

        var body = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        bool built = false;
        header.MouseLeftButtonUp += (_, _) =>
        {
            if (!built) { BuildMeetingBody(body, m); built = true; }
            body.Visibility = body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        };

        var card = new StackPanel();
        card.Children.Add(header);
        card.Children.Add(body);
        return Theme.Card2(card);
    }

    private void BuildMeetingBody(StackPanel body, Meeting m)
    {
        if (m.Summary.Length > 0)
        {
            body.Children.Add(SubHeading("Summary"));
            body.Children.Add(ReadonlyText(m.Summary, 260));
            var copySum = Theme.SecondaryButton("Copy summary");
            copySum.Margin = new Thickness(0, 6, 0, 12);
            copySum.Click += (_, _) => ClipboardService.SetText(m.Summary);
            body.Children.Add(copySum);
        }
        body.Children.Add(SubHeading("Transcript"));
        body.Children.Add(ReadonlyText(m.Transcript, 260));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copyTx = Theme.SecondaryButton("Copy transcript");
        copyTx.Margin = new Thickness(0, 0, 8, 0);
        copyTx.Click += (_, _) => ClipboardService.SetText(m.Transcript);
        actions.Children.Add(copyTx);
        var del = Theme.SecondaryButton("Delete");
        del.Click += (_, _) => MeetingStore.Delete(m.Id);
        actions.Children.Add(del);
        body.Children.Add(actions);
    }

    private static TextBlock SubHeading(string t) => new()
    {
        Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBox ReadonlyText(string text, double maxHeight) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
        Padding = new Thickness(10), MaxHeight = maxHeight,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Theme.CardInner), Foreground = Theme.TextBrush, BorderBrush = Theme.HairlineBrush,
    };
}
