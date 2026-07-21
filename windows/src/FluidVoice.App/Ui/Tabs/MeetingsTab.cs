using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Stt;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Meeting notes hub. List view: dark hero with the Start/Stop control inside it, a live
/// transcript card while recording, and past-meeting cards. Clicking a card opens an in-page
/// detail view (back button, rename-able title, editable notes, full transcript). Recording
/// state lives in the MeetingService singleton, so it survives navigating away.
/// </summary>
public sealed class MeetingsTab : StackPanel
{
    private readonly App.DictationCoordinator? _coordinator;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _noteSave = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private Meeting? _detail; // non-null → detail view is showing
    private Action? _pendingNoteSave;
    private bool _starting;

    // list-view controls (rebuilt by Render)
    private Border? _startPill;
    private TextBlock? _startLabel;
    private Border? _micChip;
    private TextBlock? _micLabel;
    private TextBlock? _timer;
    private TextBlock? _status;
    private Border? _liveCard;
    private TextBox? _liveBox;
    private readonly StackPanel _pastHost = new();

    public MeetingsTab(App.DictationCoordinator? coordinator = null)
    {
        _coordinator = coordinator;
        _tick.Tick += (_, _) => UpdateTimer();
        _noteSave.Tick += (_, _) =>
        {
            _noteSave.Stop();
            _pendingNoteSave?.Invoke();
        };
        Render();

        var svc = MeetingService.Instance;
        Loaded += (_, _) =>
        {
            svc.StateChanged += OnStateChanged;
            svc.TranscriptUpdated += OnTranscriptUpdated;
            svc.StatusChanged += OnStatusChanged;
            MeetingStore.Changed += OnMeetingsChanged;
            if (_detail is null)
            {
                SyncFromState();
                RebuildPast();
            }
        };
        Unloaded += (_, _) =>
        {
            svc.StateChanged -= OnStateChanged;
            svc.TranscriptUpdated -= OnTranscriptUpdated;
            svc.StatusChanged -= OnStatusChanged;
            MeetingStore.Changed -= OnMeetingsChanged;
            _tick.Stop();
            _noteSave.Stop();
            _pendingNoteSave?.Invoke();
        };
    }

    private void OnStateChanged() => Dispatcher.BeginInvoke(() => { if (_detail is null) SyncFromState(); });
    private void OnStatusChanged(string s) => Dispatcher.BeginInvoke(() => { if (_status is not null) _status.Text = s; UpdateStatusVisibility(); });
    private void OnMeetingsChanged() => Dispatcher.BeginInvoke(() => { if (_detail is null) RebuildPast(); });
    private void OnTranscriptUpdated(string t) => Dispatcher.BeginInvoke(() =>
    {
        if (_detail is not null || _liveBox is null) return;
        _liveBox.Text = t;
        _liveBox.CaretIndex = t.Length;
        _liveBox.ScrollToEnd();
    });

    // ---------------------------------------------------------------- rendering

    private void Render()
    {
        Children.Clear();
        if (_detail is null) RenderList();
        else RenderDetail(_detail);
        ScrollToTop();
    }

    private void RenderList()
    {
        Children.Add(PageChrome.HeaderRow("Meetings", null, null));
        Children.Add(BuildHero());
        Children.Add(BuildLiveCard());
        Children.Add(new TextBlock
        {
            Text = "Past meetings",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(2, 8, 0, 14),
        });
        Children.Add(_pastHost);
        SyncFromState();
        RebuildPast();
    }

    private UIElement BuildHero()
    {
        var content = new StackPanel
        {
            Margin = PageChrome.HeroPadding,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 780,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        content.Children.Add(new TextBlock
        {
            Text = "Every meeting, remembered.",
            FontFamily = Theme.DisplaySerif,
            FontSize = 30,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Capture your PC's audio and your mic — watch the transcript build live, get AI notes and a title when you stop. All on-device.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        });

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal };
        _startLabel = new TextBlock
        {
            Text = "●  Start meeting",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 23)),
        };
        _startPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 246, 244, 239)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 16, 9),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = _startLabel,
        };
        _startPill.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnStartStop(); };
        actionRow.Children.Add(_startPill);

        _micLabel = new TextBlock
        {
            Text = "",
            FontSize = 12.5,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var micRow = new StackPanel { Orientation = Orientation.Horizontal };
        micRow.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 1, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        micRow.Children.Add(_micLabel);
        _micChip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13, 8, 13, 8),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Include your microphone in the recording",
            Child = micRow,
        };
        _micChip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var svc = MeetingService.Instance;
            if (svc.IsRecording || svc.IsBusy || _starting) return; // capture mix is fixed once started
            Settings.Current.MeetingIncludeMic = !Settings.Current.MeetingIncludeMic;
            Settings.Current.Save("meeting");
            SyncFromState();
        };
        actionRow.Children.Add(_micChip);

        _timer = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 138, 138)),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        actionRow.Children.Add(_timer);
        content.Children.Add(actionRow);

        _status = new TextBlock
        {
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 12, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(_status);

        var hero = PageChrome.DarkHero(content);
        hero.Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private UIElement BuildLiveCard()
    {
        _liveBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 140,
            MaxHeight = 300,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Theme.CardInner),
            Foreground = Theme.TextBrush,
            BorderBrush = Theme.HairlineBrush,
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Live transcript",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(_liveBox);
        _liveCard = Theme.Card2(stack);
        _liveCard.Margin = new Thickness(0, 0, 0, 26);
        _liveCard.Visibility = Visibility.Collapsed;
        return _liveCard;
    }

    private void SyncFromState()
    {
        if (_startPill is null || _startLabel is null) return;
        var svc = MeetingService.Instance;
        bool recording = svc.IsRecording;
        bool busy = svc.IsBusy || _starting;

        _startLabel.Text = recording ? "◼  Stop & summarize" : busy ? "Working…" : "●  Start meeting";
        if (recording)
        {
            _startPill.Background = new SolidColorBrush(Theme.Danger);
            _startLabel.Foreground = Brushes.White;
        }
        else
        {
            _startPill.Background = new SolidColorBrush(Color.FromArgb(235, 246, 244, 239));
            _startLabel.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 23));
        }
        _startPill.Opacity = busy && !recording ? 0.7 : 1;
        _startPill.IsHitTestVisible = !busy || recording;

        if (_micChip is not null && _micLabel is not null)
        {
            _micLabel.Text = Settings.Current.MeetingIncludeMic ? "Mic on" : "Mic off";
            _micChip.Opacity = recording || busy ? 0.55 : 1;
            _micChip.Cursor = recording || busy ? Cursors.Arrow : Cursors.Hand;
        }

        if (_liveCard is not null && _liveBox is not null)
        {
            var transcript = svc.Transcript;
            bool live = recording || svc.IsBusy;
            _liveCard.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            if (live && _liveBox.Text != transcript)
            {
                _liveBox.Text = transcript;
                _liveBox.ScrollToEnd();
            }
        }

        if (recording) { _tick.Start(); UpdateTimer(); }
        else { _tick.Stop(); if (_timer is not null) _timer.Text = ""; }
        UpdateStatusVisibility();
    }

    private void UpdateStatusVisibility()
    {
        if (_status is not null)
            _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTimer()
    {
        if (_timer is null) return;
        var svc = MeetingService.Instance;
        if (!svc.IsRecording) { _timer.Text = ""; return; }
        var e = DateTime.Now - svc.StartedAt;
        _timer.Text = $"{(int)e.TotalMinutes:00}:{e.Seconds:00}";
    }

    private async void OnStartStop()
    {
        var svc = MeetingService.Instance;
        if (svc.IsRecording)
        {
            SetStatus("Stopping…");
            await svc.StopAsync();
            return;
        }
        if (_starting || svc.IsBusy) return;
        if (_coordinator is null) { SetStatus("Recording engine unavailable."); return; }
        var model = SpeechModels.Selected();
        if (!model.IsDownloaded) { SetStatus("Download a speech model first (Speech Models)."); return; }
        try
        {
            _starting = true;
            SyncFromState();
            SetStatus("Preparing model…");
            var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
            svc.Start(engine, Settings.Current.MeetingIncludeMic);
            SetStatus(MeetingSummarizerHint());
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't start: {ex.Message}");
        }
        finally
        {
            _starting = false;
            if (_detail is null) SyncFromState();
        }
    }

    private void SetStatus(string text)
    {
        if (_status is not null) _status.Text = text;
        UpdateStatusVisibility();
    }

    private static string MeetingSummarizerHint() =>
        Ai.MeetingSummarizer.IsAvailable
            ? "Recording… the transcript appears live; you'll get AI notes and a title when you stop."
            : "Recording… (no AI provider set, so you'll get the transcript only — configure one in Speech Models for notes).";

    // ---------------------------------------------------------------- past-meeting cards

    private void RebuildPast()
    {
        _pastHost.Children.Clear();
        var meetings = MeetingStore.All;
        if (meetings.Count == 0)
        {
            _pastHost.Children.Add(new TextBlock
            {
                Text = "No meetings yet — start one above and it will land here.",
                FontSize = 14,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(2, 4, 0, 20),
            });
            return;
        }
        foreach (var m in meetings)
            _pastHost.Children.Add(MeetingCard(m));
    }

    private UIElement MeetingCard(Meeting m)
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(PageChrome.DangerIconButton("", "Delete meeting", () => MeetingStore.Delete(m.Id)));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock
        {
            Text = m.Title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        var preview = new TextBlock
        {
            Text = PreviewOf(m),
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 60,
            Margin = new Thickness(0, 2, 0, 14),
        };
        Grid.SetRow(preview, 1);
        panel.Children.Add(preview);

        var footer = new DockPanel();
        var chip = m.Summary.Length > 0
            ? Theme.Pill("AI notes", Theme.GreenSoftBrush, Theme.GreenBrush, 11)
            : Theme.Pill("Transcript only", new SolidColorBrush(Theme.SidebarSelected), Theme.SubtleBrush, 11);
        DockPanel.SetDock(chip, Dock.Right);
        footer.Children.Add(chip);
        footer.Children.Add(new TextBlock
        {
            Text = $"{m.StartedAt:MMM d, h:mm tt} · {FormatDuration(m.DurationSeconds)}",
            FontSize = 11.5,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(footer, 2);
        panel.Children.Add(footer);

        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 15, 15, 15),
            Margin = new Thickness(0, 0, 0, 14),
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Theme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Theme.HairlineBrush;
        card.MouseLeftButtonUp += (_, _) => ShowDetail(m);
        return card;
    }

    private static string PreviewOf(Meeting m)
    {
        var source = m.Summary.Length > 0 ? m.Summary : m.Transcript;
        var flat = source.Replace("**", "").Replace("\r", "").Replace('\n', ' ').Trim();
        if (flat.Length == 0) return "No transcript captured.";
        return flat.Length <= 240 ? flat : flat[..240] + "…";
    }

    private static string FormatDuration(double seconds) => seconds switch
    {
        >= 3600 => $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60)}m",
        >= 60 => $"{(int)(seconds / 60)} min",
        _ => $"{Math.Max(1, (int)seconds)}s",
    };

    // ---------------------------------------------------------------- detail view

    private void ShowDetail(Meeting m)
    {
        _detail = m;
        Render();
    }

    private void ShowList()
    {
        _noteSave.Stop();
        _pendingNoteSave?.Invoke();
        _detail = null;
        Render();
    }

    private void RenderDetail(Meeting m)
    {
        // back button
        var backRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        backRow.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 1, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        backRow.Children.Add(new TextBlock
        {
            Text = "Meetings",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var back = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 7, 12, 7),
            Margin = new Thickness(-10, 0, 0, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            Child = backRow,
        };
        back.MouseEnter += (_, _) => back.Background = new SolidColorBrush(Theme.SidebarSelected);
        back.MouseLeave += (_, _) => back.Background = Brushes.Transparent;
        back.MouseLeftButtonUp += (_, _) => ShowList();
        Children.Add(back);

        // rename-able title
        var titleBox = new TextBox
        {
            Text = m.Title,
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Click to rename this meeting",
        };
        void CommitTitle()
        {
            var title = titleBox.Text.Trim();
            if (title.Length == 0 || title == m.Title) { titleBox.Text = m.Title; return; }
            m.Title = title;
            MeetingStore.Save(m);
        }
        titleBox.LostFocus += (_, _) => CommitTitle();
        titleBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; CommitTitle(); Keyboard.ClearFocus(); }
        };
        Children.Add(titleBox);

        Children.Add(new TextBlock
        {
            Text = $"{m.StartedAt:dddd, MMM d · h:mm tt} · {FormatDuration(m.DurationSeconds)} · {(m.Summary.Length > 0 ? "AI notes" : "transcript only")}",
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(1, 0, 0, 18),
        });

        // actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 24) };
        if (m.Summary.Length > 0)
        {
            var copyNotes = Theme.SecondaryButton("Copy notes");
            copyNotes.Margin = new Thickness(0, 0, 8, 0);
            copyNotes.Click += (_, _) => ClipboardService.SetText(m.Summary);
            actions.Children.Add(copyNotes);
        }
        var copyTx = Theme.SecondaryButton("Copy transcript");
        copyTx.Margin = new Thickness(0, 0, 8, 0);
        copyTx.Click += (_, _) => ClipboardService.SetText(m.Transcript);
        actions.Children.Add(copyTx);
        if (m.Summary.Length == 0 && Ai.MeetingSummarizer.IsAvailable && m.Transcript.Trim().Length > 0)
        {
            var generate = Theme.SecondaryButton("Generate AI notes");
            generate.Margin = new Thickness(0, 0, 8, 0);
            generate.Click += async (_, _) =>
            {
                generate.IsEnabled = false;
                generate.Content = "Generating…";
                try
                {
                    var notes = await Ai.MeetingSummarizer.SummarizeAsync(m.Transcript, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        m.Summary = notes;
                        MeetingStore.Save(m);
                        if (_detail?.Id == m.Id) Render(); // refresh to show the new notes
                    }
                    else
                    {
                        generate.Content = "No notes returned";
                    }
                }
                catch (Exception ex)
                {
                    generate.Content = "Failed — try again";
                    Log.Warn("meeting", $"Manual summarize failed: {ex.Message}");
                    generate.IsEnabled = true;
                }
            };
            actions.Children.Add(generate);
        }
        actions.Children.Add(DangerButton("Delete meeting", () =>
        {
            MeetingStore.Delete(m.Id);
            ShowList();
        }));
        Children.Add(actions);

        // editable notes
        Children.Add(DetailHeading("Notes", "edits save automatically"));
        var notesBox = new TextBox
        {
            Text = m.Summary,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 150,
            MaxHeight = 340,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Theme.CardInner),
            Foreground = Theme.TextBrush,
            BorderBrush = Theme.HairlineBrush,
            Margin = new Thickness(0, 0, 0, 24),
        };
        notesBox.TextChanged += (_, _) =>
        {
            _pendingNoteSave = () =>
            {
                _pendingNoteSave = null;
                if (m.Summary == notesBox.Text) return;
                m.Summary = notesBox.Text;
                MeetingStore.Save(m);
            };
            _noteSave.Stop();
            _noteSave.Start();
        };
        Children.Add(notesBox);

        // transcript
        Children.Add(DetailHeading("Transcript", null));
        Children.Add(new TextBox
        {
            Text = m.Transcript.Length > 0 ? m.Transcript : "No transcript captured.",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MaxHeight = 420,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Theme.CardInner),
            Foreground = Theme.TextBrush,
            BorderBrush = Theme.HairlineBrush,
            Margin = new Thickness(0, 0, 0, 30),
        });
    }

    private static UIElement DetailHeading(string title, string? hint)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 0, 0, 8) };
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        if (hint is not null)
            row.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11.5,
                Foreground = Theme.SubtleBrush,
                Margin = new Thickness(10, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        return row;
    }

    private static Border DangerButton(string label, Action onClick)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Danger),
        };
        var button = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = text,
        };
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromArgb(26, Theme.Danger.R, Theme.Danger.G, Theme.Danger.B));
            button.BorderBrush = new SolidColorBrush(Theme.Danger);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Theme.HairlineBrush;
        };
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    private void ScrollToTop()
    {
        DependencyObject? current = this;
        while (current is not null && current is not ScrollViewer)
            current = VisualTreeHelper.GetParent(current);
        (current as ScrollViewer)?.ScrollToTop();
    }
}
