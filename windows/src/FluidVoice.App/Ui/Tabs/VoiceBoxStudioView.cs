using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluidVoice.App;
using FluidVoice.Core;
using NAudio.Wave;

namespace FluidVoice.Ui;

/// <summary>
/// The VoiceBox tab as a NATIVE LiquidFlow page — our theme, margins, and controls,
/// driving the local native ARM64 VoiceBox server underneath (VoiceBoxApi). This replaces
/// the embedded web UI: same engines and data, but it looks and feels like LiquidFlow.
/// VoiceBox (MIT, jamiepine/voicebox) still powers everything server-side.
/// </summary>
public sealed class VoiceBoxStudioView : StackPanel
{
    private int _tab;
    private bool _ready;
    private string? _storyId;
    private readonly StackPanel _body = new();

    // generate state
    private ComboBox _voice = null!;
    private TextBox _text = null!;
    private TextBox _instruct = null!;
    private StackPanel _instructRow = null!;
    private Button _generate = null!;
    private readonly TextBlock _status = new()
    {
        FontSize = 12.5,
        Foreground = Theme.SubtleBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 10, 0, 0),
    };
    private List<VoiceBoxApi.Profile> _profiles = new();
    private string? _lastWav;
    private WaveOutEvent? _player;
    private AudioFileReader? _reader;

    public VoiceBoxStudioView()
    {
        // No hero on this page — VoiceBox is a dense tool surface; every pixel goes to work.
        // Header carries the Native toggle: on (default) = only fully-native features;
        // off = also Effects, Captures, and the x64-emulation-only engines.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 30) };
        var toggle = Theme.Toggle("Native", Settings.Current.VoiceBoxNativeOnly, v =>
        {
            Settings.Current.VoiceBoxNativeOnly = v;
            Settings.Current.Save("voicebox");
            _tab = 0;
            if (_ready) BuildStudio();
        });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.ToolTip = "On: only features that run fully natively on this chip. Off: also show Effects, Captures, and the engines that need the emulated x64 app.";
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        header.Children.Add(new TextBlock
        {
            Text = "VoiceBox",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Children.Add(header);
        Children.Add(_body);
        Loaded += async (_, _) =>
        {
            if (App.UiCapture.CaptureMode)
            {
                _body.Children.Clear();
                _body.Children.Add(Theme.Card2(SetupPanel("VoiceBox runs natively here — its engine sets itself up on first open.", null)));
                return;
            }
            await EnsureReadyAsync();
        };
        Unloaded += (_, _) => StopPlayback();
    }

    // ── setup / boot ───────────────────────────────────────────────────────

    private StackPanel SetupPanel(string statusText, ProgressStripe? bar)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock
        {
            Text = "Setting up",
            FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        var st = new TextBlock { Text = statusText, FontSize = 13.5, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap };
        p.Children.Add(st);
        if (bar is not null) p.Children.Add(bar);
        p.Tag = st;
        return p;
    }

    private async Task EnsureReadyAsync()
    {
        var bar = new ProgressStripe(420, 8) { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var panel = SetupPanel("Checking the voice engine…", bar);
        var statusText = (TextBlock)panel.Tag;
        _body.Children.Clear();
        _body.Children.Add(Theme.Card2(panel));
        void Set(string text, double pct) => Dispatcher.BeginInvoke(() =>
        {
            statusText.Text = text;
            if (pct < 0) bar.SetIndeterminate(); else bar.SetFraction(pct);
        });

        try
        {
            if (!VoiceBoxNative.IsInstalled)
            {
                var progress = new Progress<(string Phase, double Pct)>(x => Set(x.Phase, x.Pct));
                await VoiceBoxNative.InstallAsync(progress, CancellationToken.None);
            }
            Set("Starting the voice engine…", 0.05);
            var ok = await Task.Run(VoiceBoxNative.StartServer);
            if (ok)
            {
                var boot = new Progress<double>(p => Set("Starting the voice engine…", p));
                ok = await VoiceBoxNative.WaitForServerAsync(TimeSpan.FromSeconds(90), CancellationToken.None, boot);
            }
            if (!ok)
            {
                Set("The voice engine didn't start — see VoiceBoxNative\\server.log. Reopen this tab to retry.", 0);
                return;
            }
            _ = VoiceBoxManager.SeedPresetVoicesAsync();
            _profiles = await VoiceBoxApi.GetProfilesAsync();
            _ready = true;
            BuildStudio();
        }
        catch (Exception ex)
        {
            Log.Error("voicebox", "Native studio setup failed", ex);
            Set($"Setup failed: {ex.Message} — reopen this tab to retry.", 0);
        }
    }

    // ── studio shell ───────────────────────────────────────────────────────

    private string[] CurrentTabs => Settings.Current.VoiceBoxNativeOnly
        ? new[] { "Generate", "Clone", "Voices", "Stories", "History", "Models" }
        : new[] { "Generate", "Clone", "Voices", "Stories", "History", "Effects", "Captures", "Models" };

    // Tab content is cached so switching is instant — no refetch on every click.
    // Mutations call Invalidate(...) for the tabs whose data changed.
    private readonly Dictionary<string, UIElement> _tabCache = new();

    private void Invalidate(params string[] tabs)
    {
        foreach (var t in tabs) _tabCache.Remove(t);
    }

    private static void FadeIn(UIElement el)
    {
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void BuildStudio()
    {
        var tabs = CurrentTabs;
        if (_tab >= tabs.Length) _tab = 0;
        var name = tabs[_tab];
        _body.Children.Clear();
        _body.Children.Add(PageChrome.TabsRow(tabs, _tab, i =>
        {
            _tab = i;
            if (_ready) BuildStudio();
        }));
        if (_tabCache.TryGetValue(name, out var cached))
        {
            _body.Children.Add(cached);
            FadeIn(cached);
            return;
        }
        switch (name)
        {
            case "Generate": { var el = BuildGenerate(); _tabCache[name] = el; _body.Children.Add(el); FadeIn(el); break; }
            case "Clone": { var el = BuildClone(); _tabCache[name] = el; _body.Children.Add(el); FadeIn(el); break; }
            case "Voices": _ = BuildVoicesAsync(); break;
            case "Stories": _ = BuildStoriesAsync(); break;
            case "History": _ = BuildHistoryAsync(); break;
            case "Effects": _ = BuildEffectsAsync(); break;
            case "Captures": _ = BuildCapturesAsync(); break;
            case "Models": _ = BuildModelsAsync(); break;
        }
    }

    private static TextBlock Subtle(string text, double size = 13) => new()
    {
        Text = text, FontSize = size, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap,
    };

    // ── Generate ───────────────────────────────────────────────────────────

    private UIElement BuildGenerate()
    {
        // _status is long-lived (GenerateAsync writes it after awaits) — detach from the
        // previous build first or WPF throws "already the logical child of another element".
        (_status.Parent as Panel)?.Children.Remove(_status);
        var outer = new StackPanel();

        // One composite "generate box": big borderless input on top, controls beneath.
        var box = new StackPanel();
        var placeholder = new TextBlock
        {
            FontSize = 15,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(19, 16, 19, 0),
            IsHitTestVisible = false,
        };
        _text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 104,
            MaxHeight = 260,
            Padding = new Thickness(16, 14, 16, 12),
            FontSize = 15,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        void SyncPlaceholder()
        {
            var sel = SelectedProfile();
            placeholder.Text = sel is null ? "Generate speech…" : $"Generate speech using {sel.Name}…";
            placeholder.Visibility = _text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _text.TextChanged += (_, _) => SyncPlaceholder();
        var inputHost = new Grid();
        inputHost.Children.Add(_text);
        inputHost.Children.Add(placeholder);
        box.Children.Add(inputHost);

        _instructRow = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 16, 10) };
        _instructRow.Children.Add(new TextBlock
        {
            Text = "Delivery instructions (this engine supports them)",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(2, 0, 0, 4),
        });
        _instruct = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(10, 7, 10, 7),
            ToolTip = "e.g. Speak slowly with warmth · Professional, broadcast quality",
        };
        _instructRow.Children.Add(_instruct);
        box.Children.Add(_instructRow);

        box.Children.Add(new Border { Height = 1, Background = Theme.HairlineBrush });

        var bar = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        _generate = Theme.PrimaryButton("Generate");
        _generate.Padding = new Thickness(22, 8, 22, 8);
        _generate.Click += async (_, _) => await GenerateAsync();
        DockPanel.SetDock(_generate, Dock.Right);
        bar.Children.Add(_generate);
        var save = Theme.SecondaryButton("Save WAV…");
        save.Margin = new Thickness(0, 0, 8, 0);
        save.Click += (_, _) => SaveLast();
        DockPanel.SetDock(save, Dock.Right);
        bar.Children.Add(save);
        var stop = Theme.SecondaryButton("Stop");
        stop.Margin = new Thickness(0, 0, 8, 0);
        stop.Click += (_, _) => StopPlayback();
        DockPanel.SetDock(stop, Dock.Right);
        bar.Children.Add(stop);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _voice = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        foreach (var prof in _profiles) _voice.Items.Add(prof.Name);
        if (_voice.Items.Count > 0) _voice.SelectedIndex = 0;
        var engineChip = Theme.Pill("engine", Theme.GreenSoftBrush, Theme.GreenBrush, 11);
        engineChip.Margin = new Thickness(10, 0, 0, 0);
        engineChip.VerticalAlignment = VerticalAlignment.Center;
        void SyncEngineChip()
        {
            var sel = SelectedProfile();
            if (engineChip.Child is TextBlock tb)
                tb.Text = EngineLabel(sel?.PresetEngine ?? sel?.DefaultEngine);
        }
        _voice.SelectionChanged += (_, _) => { SyncInstructVisibility(); SyncEngineChip(); SyncPlaceholder(); };
        left.Children.Add(_voice);
        left.Children.Add(engineChip);
        bar.Children.Add(left);
        box.Children.Add(bar);

        outer.Children.Add(new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Child = box,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.08, Color = Colors.Black },
        });

        _status.Margin = new Thickness(4, 10, 0, 0);
        outer.Children.Add(_status);
        SyncInstructVisibility();
        SyncEngineChip();
        SyncPlaceholder();
        return outer;
    }

    private VoiceBoxApi.Profile? SelectedProfile()
        => _voice.SelectedIndex >= 0 && _voice.SelectedIndex < _profiles.Count ? _profiles[_voice.SelectedIndex] : null;

    private void SyncInstructVisibility()
    {
        var engine = SelectedProfile()?.PresetEngine ?? SelectedProfile()?.DefaultEngine;
        _instructRow.Visibility = engine == "qwen_custom_voice" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string EngineLabel(string? engine) => engine switch
    {
        "kokoro" => "Kokoro (fast)",
        "qwen_custom_voice" => "Qwen CustomVoice",
        "qwen" => "Qwen (cloning)",
        null or "" => "default",
        _ => engine,
    };

    private async Task GenerateAsync()
    {
        var prof = SelectedProfile();
        var text = _text.Text.Trim();
        if (prof is null) { _status.Text = "Add a voice first (Voices tab)."; return; }
        if (text.Length == 0) { _status.Text = "Type something to say first."; return; }
        _generate.IsEnabled = false;
        StopPlayback();
        try
        {
            _status.Text = $"Generating with {prof.Name}…";
            var engine = prof.PresetEngine ?? prof.DefaultEngine;
            var modelSize = engine is "qwen_custom_voice" or "qwen" ? "0.6B" : null; // lighter default for this CPU
            var gen = await VoiceBoxApi.GenerateAsync(prof.Id, text, engine, _instruct?.Text, modelSize);
            for (int i = 0; i < 600; i++)
            {
                await Task.Delay(1000);
                var s = await VoiceBoxApi.GetGenerationAsync(gen.Id);
                if (s is null) continue;
                if (s.Status == "completed")
                {
                    var bytes = await VoiceBoxApi.GetAudioAsync(gen.Id);
                    Directory.CreateDirectory(Ai.VoiceStudio.OutputDir);
                    _lastWav = Path.Combine(Ai.VoiceStudio.OutputDir, $"voicebox-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
                    await File.WriteAllBytesAsync(_lastWav, bytes);
                    _status.Text = $"{prof.Name}: {s.Duration:0.0}s of audio — playing. Saved to the Generated folder.";
                    Invalidate("History", "Stories", "Models");
                    Toasts.Success($"{prof.Name}: {s.Duration:0.0}s of audio — playing");
                    Play(_lastWav);
                    return;
                }
                if (s.Status is "failed" or "error" or "cancelled")
                {
                    _status.Text = $"Generation {s.Status}: {s.Error ?? "unknown error"}";
                    Toasts.Error(_status.Text);
                    return;
                }
                _status.Text = s.Status == "loading_model"
                    ? $"Loading the {EngineLabel(engine)} engine (first use takes a moment)…"
                    : $"Generating with {prof.Name}… ({s.Status})";
            }
            _status.Text = "Timed out waiting for the generation.";
        }
        catch (Exception ex)
        {
            Log.Error("voicebox", "Native studio generation failed", ex);
            _status.Text = $"Couldn't generate: {ex.Message}";
            Toasts.Error(_status.Text);
        }
        finally
        {
            _generate.IsEnabled = true;
        }
    }

    private void Play(string path)
    {
        try
        {
            _reader = new AudioFileReader(path);
            _player = new WaveOutEvent();
            _player.Init(_reader);
            _player.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(StopPlayback);
            _player.Play();
        }
        catch (Exception ex) { _status.Text = $"Saved, but playback failed: {ex.Message}"; }
    }

    private void StopPlayback()
    {
        try { _player?.Stop(); _player?.Dispose(); _reader?.Dispose(); } catch { }
        _player = null;
        _reader = null;
    }

    private void SaveLast()
    {
        if (_lastWav is null || !File.Exists(_lastWav)) { _status.Text = "Generate something first, then save it."; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "WAV audio|*.wav", FileName = Path.GetFileName(_lastWav) };
        if (dlg.ShowDialog() == true)
        {
            File.Copy(_lastWav, dlg.FileName, overwrite: true);
            _status.Text = $"Saved to {dlg.FileName}";
        }
    }

    // ── Clone (one-click voice cloning) ────────────────────────────────────

    private WaveInEvent? _rec;
    private MemoryStream? _recBytes;
    private readonly List<float> _recSamples = new();
    private DispatcherTimer? _recTimer;
    private DateTime _recStart;
    private bool _cloneBusy;

    private UIElement BuildClone()
    {
        StopRecording(discard: true);
        var p = new StackPanel();

        p.Children.Add(Theme.Label("Voice name"));
        var nameBox = new TextBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6, 8, 6), FontSize = 14 };
        nameBox.Text = UniqueVoiceName("My voice");
        p.Children.Add(nameBox);

        // Read-along script: continuous speech is hard to improvise — give them something to read.
        var scriptText = new TextBlock
        {
            FontSize = 14,
            Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        };
        var scriptIdx = Environment.TickCount % CloneScripts.Length;
        scriptText.Text = CloneScripts[scriptIdx];
        var scriptHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var shuffle = Theme.SecondaryButton("New script");
        shuffle.Padding = new Thickness(10, 3, 10, 3);
        shuffle.Click += (_, _) =>
        {
            scriptIdx = (scriptIdx + 1) % CloneScripts.Length;
            scriptText.Text = CloneScripts[scriptIdx];
        };
        DockPanel.SetDock(shuffle, Dock.Right);
        scriptHead.Children.Add(shuffle);
        scriptHead.Children.Add(new TextBlock
        {
            Text = "Read this aloud (or say anything you like)",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var scriptBox = new StackPanel();
        scriptBox.Children.Add(scriptHead);
        scriptBox.Children.Add(scriptText);
        p.Children.Add(new Border
        {
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 14),
            Margin = new Thickness(0, 14, 0, 0),
            Child = scriptBox,
        });

        var micGlyph = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 34,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var mic = new Border
        {
            Width = 92, Height = 92,
            CornerRadius = new CornerRadius(46),
            Background = Theme.InkBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 12),
            Cursor = Cursors.Hand,
            Child = micGlyph,
        };
        p.Children.Add(mic);

        // Live input level so you can SEE the mic picking you up while recording.
        var level = new ProgressStripe(260, 6)
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        p.Children.Add(level);

        var hint = new TextBlock
        {
            Text = "Click the mic and speak naturally — 15–30 seconds is ideal. Click again to finish.",
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        };
        p.Children.Add(hint);

        var st = new TextBlock
        {
            FontSize = 12.5,
            Foreground = Theme.SubtleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Margin = new Thickness(0, 10, 0, 6),
        };
        p.Children.Add(st);
        var cloneBar = new ProgressStripe(320, 6)
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        p.Children.Add(cloneBar);

        mic.MouseLeftButtonUp += async (_, _) =>
        {
            if (_cloneBusy) return;
            if (_rec is null)
            {
                // start
                try
                {
                    _recSamples.Clear();
                    _recBytes = new MemoryStream();
                    _rec = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1), BufferMilliseconds = 50 };
                    double disp = 0;
                    _rec.DataAvailable += (_, e) =>
                    {
                        _recBytes?.Write(e.Buffer, 0, e.BytesRecorded);
                        double sumSq = 0;
                        int n = 0;
                        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                        {
                            var s = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                            _recSamples.Add(s);
                            sumSq += s * s;
                            n++;
                        }
                        // fast attack, slow decay — reads like a real VU meter
                        var rms = n > 0 ? Math.Sqrt(sumSq / n) : 0;
                        disp = Math.Max(Math.Min(1.0, rms * 9), disp * 0.82);
                        var shown = disp;
                        Dispatcher.BeginInvoke(() => level.SetFraction(shown));
                    };
                    _rec.StartRecording();
                    mic.RenderTransformOrigin = new Point(0.5, 0.5);
                    var micScale = new ScaleTransform(1, 1);
                    mic.RenderTransform = micScale;
                    var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 1.07, TimeSpan.FromMilliseconds(600))
                    {
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase(),
                    };
                    micScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                    micScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                    level.Visibility = Visibility.Visible;
                    level.SetFraction(0);
                    _recStart = DateTime.Now;
                    mic.Background = new SolidColorBrush(Theme.Danger);
                    micGlyph.Text = "";
                    _recTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _recTimer.Tick += (_, _) =>
                    {
                        var e = DateTime.Now - _recStart;
                        hint.Text = $"Recording  {(int)e.TotalMinutes}:{e.Seconds:00} — click to finish (auto-stops at 0:30, the engine's limit)";
                        if (e.TotalSeconds >= 30) mic.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = MouseLeftButtonUpEvent });
                    };
                    _recTimer.Start();
                    st.Text = "";
                }
                catch (Exception ex)
                {
                    StopRecording(discard: true);
                    st.Text = $"Couldn't open the microphone: {ex.Message}";
                }
                return;
            }

            // stop + process
            var seconds = (DateTime.Now - _recStart).TotalSeconds;
            var samples = _recSamples.ToArray();
            var raw = _recBytes?.ToArray() ?? Array.Empty<byte>();
            // the cloning engine rejects reference audio over 30.0s — clip defensively
            const int maxSamples = 16000 * 29;
            if (samples.Length > maxSamples)
            {
                samples = samples[..maxSamples];
                raw = raw[..(maxSamples * 2)];
            }
            StopRecording(discard: false);
            mic.Background = Theme.InkBrush;
            if (mic.RenderTransform is ScaleTransform micScaleDone)
            {
                micScaleDone.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                micScaleDone.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                micScaleDone.ScaleX = micScaleDone.ScaleY = 1;
            }
            micGlyph.Text = "";
            hint.Text = "Click the mic and speak naturally — 15–30 seconds is ideal. Click again to finish.";
            level.Visibility = Visibility.Collapsed;
            if (seconds < 3)
            {
                st.Text = "That was under 3 seconds — give it a bit more speech and try again.";
                return;
            }
            // silence check locally, then the same auto-gain dictation uses — a quiet mic
            // otherwise gets rejected server-side ("Audio is too quiet or silent")
            double sq = 0;
            foreach (var s in samples) sq += (double)s * s;
            if (Math.Sqrt(sq / Math.Max(1, samples.Length)) < 0.0015)
            {
                st.Text = "The mic barely picked anything up — watch the level bar while you speak; check your Windows input device or move closer, then try again.";
                return;
            }
            samples = Audio.Dsp.Normalize(samples);
            raw = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                var s16 = (short)Math.Clamp((int)(samples[i] * 32767f), short.MinValue, short.MaxValue);
                raw[2 * i] = (byte)(s16 & 0xFF);
                raw[2 * i + 1] = (byte)((s16 >> 8) & 0xFF);
            }

            _cloneBusy = true;
            cloneBar.Visibility = Visibility.Visible;
            cloneBar.SetIndeterminate();
            try
            {
                // write the sample wav
                var dir = Path.Combine(FluidVoice.Core.AppPaths.DataDir, "Voices", "CloneSamples");
                Directory.CreateDirectory(dir);
                var wavPath = Path.Combine(dir, $"clone-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
                await using (var writer = new WaveFileWriter(wavPath, new WaveFormat(16000, 1)))
                    writer.Write(raw, 0, raw.Length);

                st.Text = "Step 1 of 3 — transcribing your recording locally…";
                var transcript = await TryTranscribeAsync(samples);
                var reference = string.IsNullOrWhiteSpace(transcript)
                    ? "A natural reference recording of my voice."
                    : transcript!;

                _profiles = await VoiceBoxApi.GetProfilesAsync(); // fresh names — avoids stale-list conflicts
                var wanted = string.IsNullOrWhiteSpace(nameBox.Text) ? "My voice" : nameBox.Text.Trim();
                // a same-name cloned profile with no sample = a failed earlier attempt; adopt it
                var orphan = _profiles.FirstOrDefault(x =>
                    x.VoiceType == "cloned" && (x.SampleCount ?? 0) == 0 &&
                    string.Equals(x.Name, wanted, StringComparison.OrdinalIgnoreCase));
                VoiceBoxApi.Profile prof;
                string name;
                if (orphan is not null)
                {
                    prof = orphan;
                    name = orphan.Name;
                    st.Text = $"Step 2 of 3 — finishing “{name}”…";
                }
                else
                {
                    name = UniqueVoiceName(wanted);
                    st.Text = $"Step 2 of 3 — creating “{name}”…";
                    prof = await VoiceBoxApi.CreateClonedProfileAsync(name, "Cloned from a quick in-app recording");
                }
                st.Text = "Step 3 of 3 — uploading your sample…";
                await VoiceBoxApi.UploadSampleAsync(prof.Id, wavPath, reference);
                _profiles = await VoiceBoxApi.GetProfilesAsync();
                nameBox.Text = UniqueVoiceName("My voice");

                var doneMsg = $"“{name}” is ready — pick it on the Generate tab. Cloned voices speak through the Qwen engine (get “Qwen TTS 0.6B” under Models if it's missing); the first generation takes a minute while it loads.";
                st.Text = doneMsg;
                Invalidate("Voices", "Generate");
                Toasts.Success($"“{name}” is ready — pick it on the Generate tab.");
                if (!string.IsNullOrWhiteSpace(transcript))
                    st.Text += $"\nHeard: “{(transcript!.Length > 90 ? transcript[..90] + "…" : transcript)}”";
            }
            catch (Exception ex)
            {
                Log.Error("voicebox", "One-click clone failed", ex);
                st.Text = $"Couldn't clone: {ex.Message}";
                Toasts.Error(st.Text);
            }
            finally
            {
                cloneBar.Visibility = Visibility.Collapsed;
                _cloneBusy = false;
            }
        };

        p.Children.Add(Subtle("Have a recording already? Voices → Add voice → “Clone from my audio”.", 11.5));
        ((TextBlock)p.Children[^1]).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)p.Children[^1]).Margin = new Thickness(0, 14, 0, 0);
        return Theme.Card2(p);
    }

    /// <summary>Original read-along passages (~20–30s aloud) so nobody has to improvise
    /// continuous speech. Varied intonation on purpose: statements, questions, numbers.</summary>
    private static readonly string[] CloneScripts =
    {
        "Here's a little test of my everyday voice. This morning I made coffee, checked the weather, and planned the rest of my week. Tuesday looks busy, but Friday should be quiet. Do I sound natural right now? I hope so! I'll keep talking at a relaxed pace, the way I'd chat with a friend across the table.",
        "Let me describe the room around me. There's a desk, a couple of chairs, and a window with light coming through. Outside, someone is walking a very enthusiastic dog. If I count to five, it sounds like this: one, two, three, four, five. Reading aloud is strangely calming, isn't it? Anyway, that's the tour.",
        "Picture a small kitchen on a Sunday afternoon. Butter melts in a warm pan, onions turn golden, and something smells faintly of garlic and thyme. I taste, adjust, and taste again. Cooking rewards patience more than talent. In about twenty minutes, dinner will be ready, and honestly? I can't wait.",
        "Every city has a sound of its own. Buses sigh at their stops, markets hum with bargaining, and somewhere a street musician tunes an old guitar. I like walking with no destination, turning left simply because the light is better there. Travel isn't about distance; it's about paying attention. That's the whole secret.",
        "Technology is funny. We carry tiny computers that talk to satellites, yet we still lose our keys every single day. My favorite feature is voice: I press a button, speak a thought, and watch it become text. Fifty years ago that was science fiction. Today, it's just a Tuesday. What a time to be alive!",
    };

    private void StopRecording(bool discard)
    {
        try { _rec?.StopRecording(); _rec?.Dispose(); } catch { }
        _rec = null;
        _recTimer?.Stop();
        _recTimer = null;
        if (discard)
        {
            _recBytes?.Dispose();
            _recBytes = null;
            _recSamples.Clear();
        }
    }

    private string UniqueVoiceName(string baseName)
    {
        if (_profiles.All(p => !string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (_profiles.All(p => !string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    /// <summary>Transcribe the clip with LiquidFlow's own local STT (Parakeet by default) so
    /// the clone gets an accurate reference text. Returns null when unavailable.</summary>
    private static async Task<string?> TryTranscribeAsync(float[] samples16k)
    {
        try
        {
            var model = Stt.SpeechModels.Selected();
            if (!model.IsDownloaded) return null;
            using Stt.ISpeechEngine engine = model.Engine == Stt.SpeechEngineKind.Parakeet
                ? new Stt.ParakeetEngine()
                : new Stt.WhisperEngine();
            await engine.PrepareAsync(model, new Progress<Stt.ModelPreparationProgress>(_ => { }), CancellationToken.None);
            var pcm = samples16k;
            if (pcm.Length < 16000)
            {
                var padded = new float[16000];
                Array.Copy(pcm, padded, pcm.Length);
                pcm = padded;
            }
            var raw = await engine.TranscribeAsync(Audio.Dsp.Normalize(pcm), CancellationToken.None);
            var formatted = Text.TranscriptFormatter.Process(raw);
            return string.IsNullOrWhiteSpace(formatted) ? null : formatted.Trim();
        }
        catch (Exception ex)
        {
            Log.Warn("voicebox", $"Clone transcription skipped: {ex.Message}");
            return null;
        }
    }

    // ── Voices ─────────────────────────────────────────────────────────────

    private async Task BuildVoicesAsync()
    {
        var host = new StackPanel();
        _tabCache["Voices"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading voices…"));
        try
        {
            _profiles = await VoiceBoxApi.GetProfilesAsync();
            host.Children.Clear();

            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var add = Theme.PrimaryButton("Add voice");
            add.Click += (_, _) => AddVoiceDialog();
            DockPanel.SetDock(add, Dock.Right);
            bar.Children.Add(add);
            bar.Children.Add(new TextBlock
            {
                Text = $"{_profiles.Count} voices",
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            host.Children.Add(bar);

            var list = new StackPanel();
            foreach (var prof in _profiles)
            {
                var grid = new Grid { Margin = new Thickness(20, 12, 12, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

                var info = new StackPanel();
                var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush };
                name.Inlines.Add(new Run(prof.Name));
                if (!string.IsNullOrEmpty(prof.Language))
                    name.Inlines.Add(new Run($"   {prof.Language}") { Foreground = Theme.SubtleBrush, FontSize = 11.5 });
                info.Children.Add(name);
                if (!string.IsNullOrWhiteSpace(prof.Description))
                {
                    var d = Subtle(prof.Description!, 12.5);
                    d.TextTrimming = TextTrimming.CharacterEllipsis;
                    d.MaxHeight = 20;
                    info.Children.Add(d);
                }
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var chip = Theme.Pill(EngineLabel(prof.PresetEngine ?? prof.DefaultEngine), Theme.GreenSoftBrush, Theme.GreenBrush, 11);
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Margin = new Thickness(8, 0, 8, 0);
                Grid.SetColumn(chip, 1);
                grid.Children.Add(chip);

                var prev = PageChrome.IconButton("\uE768", "Preview this voice", () => _ = PreviewVoiceAsync(prof));
                prev.VerticalAlignment = VerticalAlignment.Center;
                var del = PageChrome.IconButton("", "Delete voice", async () =>
                {
                    try { await VoiceBoxApi.DeleteProfileAsync(prof.Id); } catch { }
                    Invalidate("Generate");
                    Toasts.Info("Voice removed");
                    Rebuild();
                });
                del.VerticalAlignment = VerticalAlignment.Center;
                var rowActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                rowActions.Children.Add(prev);
                rowActions.Children.Add(del);
                Grid.SetColumn(rowActions, 2);
                grid.Children.Add(rowActions);

                list.Children.Add(grid);
                if (prof != _profiles[^1]) list.Children.Add(Theme.Divider());
            }
            host.Children.Add(new Border
            {
                Background = Theme.SurfaceBrush,
                BorderBrush = new SolidColorBrush(Theme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = list,
            });
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load voices: {ex.Message}"));
        }
    }

    private bool _previewBusy;

    /// <summary>Play a short cached sample of a voice; generates it once on first use.</summary>
    private async Task PreviewVoiceAsync(VoiceBoxApi.Profile prof)
    {
        if (_previewBusy) return;
        _previewBusy = true;
        try
        {
            var dir = Path.Combine(FluidVoice.Core.AppPaths.DataDir, "Voices", "Previews");
            Directory.CreateDirectory(dir);
            var cached = Path.Combine(dir, prof.Id + ".wav");
            if (!File.Exists(cached))
            {
                Toasts.Info($"Generating a preview of “{prof.Name}”…");
                var engine = prof.PresetEngine ?? prof.DefaultEngine;
                var gen = await VoiceBoxApi.GenerateAsync(prof.Id,
                    $"Hi, I'm {prof.Name}. This is how I sound.", engine, null,
                    engine is "qwen" or "qwen_custom_voice" ? "0.6B" : null);
                for (int i = 0; i < 120; i++)
                {
                    await Task.Delay(1000);
                    var s = await VoiceBoxApi.GetGenerationAsync(gen.Id);
                    if (s?.Status == "completed")
                    {
                        await File.WriteAllBytesAsync(cached, await VoiceBoxApi.GetAudioAsync(gen.Id));
                        break;
                    }
                    if (s?.Status is "failed" or "error" or "cancelled")
                    {
                        Toasts.Error($"Preview failed: {s.Error ?? s.Status}");
                        return;
                    }
                }
                if (!File.Exists(cached)) { Toasts.Error("Preview timed out."); return; }
                Invalidate("History");
            }
            StopPlayback();
            Play(cached);
        }
        catch (Exception ex)
        {
            Toasts.Error($"Preview failed: {ex.Message}");
        }
        finally
        {
            _previewBusy = false;
        }
    }

    private void AddVoiceDialog()
    {
        var dlg = new Window
        {
            Title = "Add voice",
            Width = 560, Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Theme.Bg),
        };
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(Theme.Label("Type"));
        var mode = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        mode.Items.Add("Preset voice (built-in)");
        mode.Items.Add("Clone from my audio");
        mode.SelectedIndex = 0;
        root.Children.Add(mode);

        // preset fields
        var presetPanel = new StackPanel();
        presetPanel.Children.Add(Theme.Label("Preset voice"));
        var catalog = VoiceBoxManager.PresetCatalog().ToList();
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var v in catalog) combo.Items.Add($"{v.Name}  ·  {EngineLabel(v.Engine)}");
        combo.SelectedIndex = 0;
        presetPanel.Children.Add(combo);
        root.Children.Add(presetPanel);

        // clone fields
        var clonePanel = new StackPanel { Visibility = Visibility.Collapsed };
        var files = new List<string>();
        var filesLabel = Subtle("No audio picked yet — a clean 10–30s clip works best.", 12);
        var pick = Theme.SecondaryButton("Choose audio…");
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Click += (_, _) =>
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.aac",
                Multiselect = true,
            };
            if (ofd.ShowDialog() == true)
            {
                files.Clear();
                files.AddRange(ofd.FileNames);
                filesLabel.Text = string.Join(", ", files.Select(Path.GetFileName));
            }
        };
        clonePanel.Children.Add(pick);
        filesLabel.Margin = new Thickness(0, 6, 0, 10);
        clonePanel.Children.Add(filesLabel);
        clonePanel.Children.Add(Theme.Label("What is said in the clip (helps cloning accuracy)"));
        var refText = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 90,
            Padding = new Thickness(8), FontSize = 13, Margin = new Thickness(0, 0, 0, 12),
        };
        clonePanel.Children.Add(refText);
        clonePanel.Children.Add(Subtle("Cloning uses the Qwen TTS engine — download it once under Models. Generations are slower than presets on CPU.", 11.5));
        root.Children.Add(clonePanel);

        mode.SelectionChanged += (_, _) =>
        {
            presetPanel.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            clonePanel.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        };

        root.Children.Add(Theme.Label("Name"));
        var nameBox = new TextBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 14 };
        nameBox.Text = catalog[0].Name;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) nameBox.Text = catalog[combo.SelectedIndex].Name; };
        root.Children.Add(nameBox);

        var status = Subtle("", 12);
        status.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(status);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = Theme.SecondaryButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => dlg.Close();
        var addBtn = Theme.PrimaryButton("Add");
        addBtn.Click += async (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0) { status.Text = "Give the voice a name."; return; }
            addBtn.IsEnabled = false;
            try
            {
                if (mode.SelectedIndex == 0)
                {
                    if (combo.SelectedIndex < 0) return;
                    var v = catalog[combo.SelectedIndex];
                    await VoiceBoxApi.CreatePresetProfileAsync(name, v.Engine, v.VoiceId, v.Lang, v.Desc);
                }
                else
                {
                    if (files.Count == 0) { status.Text = "Pick at least one audio clip."; addBtn.IsEnabled = true; return; }
                    status.Text = "Creating profile…";
                    var prof = await VoiceBoxApi.CreateClonedProfileAsync(name, "Cloned from my audio");
                    var text = string.IsNullOrWhiteSpace(refText.Text) ? "A reference recording of my voice." : refText.Text.Trim();
                    for (int i = 0; i < files.Count; i++)
                    {
                        status.Text = $"Uploading sample {i + 1}/{files.Count}…";
                        await VoiceBoxApi.UploadSampleAsync(prof.Id, files[i], text);
                    }
                }
                Invalidate("Generate");
                Toasts.Success($"Added “{name}”");
                dlg.Close();
                Rebuild();
            }
            catch (Exception ex)
            {
                status.Text = $"Couldn't add voice: {ex.Message}";
                addBtn.IsEnabled = true;
            }
        };
        btns.Children.Add(cancel);
        btns.Children.Add(addBtn);
        root.Children.Add(btns);
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private void Rebuild() => Dispatcher.BeginInvoke(() =>
    {
        var tabs = CurrentTabs;
        if (_tab < tabs.Length) _tabCache.Remove(tabs[_tab]); // current tab refetches
        BuildStudio();
    });

    // ── History ────────────────────────────────────────────────────────────

    private async Task BuildHistoryAsync()
    {
        var host = new StackPanel();
        _tabCache["History"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading history…"));
        try
        {
            var items = await VoiceBoxApi.GetHistoryAsync(50);
            host.Children.Clear();
            if (items.Count == 0)
            {
                host.Children.Add(Subtle("Nothing generated yet — your generations will show up here."));
                return;
            }
            var list = new StackPanel();
            foreach (var g in items)
            {
                var grid = new Grid { Margin = new Thickness(20, 12, 12, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                var line = new TextBlock { FontSize = 13.5, Foreground = Theme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
                line.Inlines.Add(new Run(g.Text is { Length: > 90 } ? g.Text[..90] + "…" : g.Text ?? ""));
                info.Children.Add(line);
                var meta = $"{g.ProfileName ?? "voice"} · {EngineLabel(g.Engine)}" +
                           (g.Duration is > 0 ? $" · {g.Duration:0.0}s" : "") +
                           (g.Status != "completed" ? $" · {g.Status}" : "");
                info.Children.Add(Subtle(meta, 11.5));
                if (!string.IsNullOrWhiteSpace(g.Error))
                {
                    var err = Subtle(g.Error!.Length > 120 ? g.Error[..120] + "…" : g.Error!, 11);
                    err.Foreground = new SolidColorBrush(Theme.Danger);
                    info.Children.Add(err);
                }
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var fav = PageChrome.IconButton(g.IsFavorited == true ? "" : "",
                    g.IsFavorited == true ? "Unfavorite" : "Favorite", async () =>
                    {
                        try { await VoiceBoxApi.ToggleFavoriteAsync(g.Id); } catch { }
                        Rebuild();
                    });
                actions.Children.Add(fav);
                if (g.Status is "generating" or "loading_model")
                    actions.Children.Add(PageChrome.IconButton("\uE711", "Cancel this generation", async () =>
                    {
                        try { await VoiceBoxApi.CancelGenerationAsync(g.Id); } catch { }
                        Toasts.Info("Generation cancelled");
                        Rebuild();
                    }));
                if (g.Status is "failed" or "error" or "cancelled")
                    actions.Children.Add(PageChrome.IconButton("\uE72C", "Retry this generation", async () =>
                    {
                        try { await VoiceBoxApi.RetryGenerationAsync(g.Id); Toasts.Info("Retrying…"); }
                        catch (Exception ex) { Toasts.Error($"Retry failed: {ex.Message}"); }
                        Rebuild();
                    }));
                if (g.Status == "completed")
                    actions.Children.Add(PageChrome.IconButton("", "Play", async () =>
                    {
                        try
                        {
                            StopPlayback();
                            // cache per generation — replays are instant after the first fetch
                            var cacheDir = Path.Combine(FluidVoice.Core.AppPaths.DataDir, "Voices", "HistoryCache");
                            Directory.CreateDirectory(cacheDir);
                            var tmp = Path.Combine(cacheDir, $"{g.Id}.wav");
                            if (!File.Exists(tmp))
                                await File.WriteAllBytesAsync(tmp, await VoiceBoxApi.GetAudioAsync(g.Id));
                            Play(tmp);
                        }
                        catch { }
                    }));
                actions.Children.Add(PageChrome.IconButton("", "Delete", async () =>
                {
                    try { await VoiceBoxApi.DeleteGenerationAsync(g.Id); } catch { }
                    Rebuild();
                }));
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);

                list.Children.Add(grid);
                if (g != items[^1]) list.Children.Add(Theme.Divider());
            }
            host.Children.Add(new Border
            {
                Background = Theme.SurfaceBrush,
                BorderBrush = new SolidColorBrush(Theme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = list,
            });
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load history: {ex.Message}"));
        }
    }

    // ── Stories (native) ───────────────────────────────────────────────────

    private async Task BuildStoriesAsync()
    {
        var host = new StackPanel();
        _tabCache["Stories"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading stories…"));
        try
        {
            List<VoiceBoxApi.Story>? stories = null;
            for (int attempt = 0; ; attempt++)
            {
                try { stories = await VoiceBoxApi.GetStoriesAsync(); break; }
                catch when (attempt < 2) { await Task.Delay(1500); } // boot race / busy server: retry twice
            }
            var gens = (await VoiceBoxApi.GetHistoryAsync(200)).Where(g => g.Status == "completed").ToList();
            host.Children.Clear();

            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var newBtn = Theme.PrimaryButton("New story");
            newBtn.Click += (_, _) => NewStoryDialog();
            DockPanel.SetDock(newBtn, Dock.Right);
            bar.Children.Add(newBtn);
            bar.Children.Add(new TextBlock
            {
                Text = stories.Count == 0 ? "Stories" : $"{stories.Count} stories",
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            host.Children.Add(bar);

            if (stories.Count == 0)
            {
                host.Children.Add(Subtle("Compose multi-voice narratives: generate lines on the Generate tab, then arrange them here and export one mixed track."));
                return;
            }
            if (_storyId is null || stories.All(s => s.Id != _storyId)) _storyId = stories[0].Id;

            var pick = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var s in stories)
            {
                bool on = s.Id == _storyId;
                var chip = Theme.Pill($"{s.Name}  ({s.ItemCount ?? 0})",
                    on ? Theme.InkBrush : new SolidColorBrush(Theme.SidebarSelected),
                    on ? new SolidColorBrush(Theme.InkText) : Theme.TextBrush, 12);
                chip.Margin = new Thickness(0, 0, 8, 8);
                chip.Cursor = Cursors.Hand;
                var sid = s.Id;
                chip.MouseLeftButtonUp += (_, _) => { _storyId = sid; Rebuild(); };
                pick.Children.Add(chip);
            }
            host.Children.Add(pick);

            var detail = await VoiceBoxApi.GetStoryAsync(_storyId!);
            if (detail is null) { host.Children.Add(Subtle("Couldn't open that story.")); return; }

            string GenLabel(string genId)
            {
                var g = gens.FirstOrDefault(x => x.Id == genId);
                if (g is null) return genId.Length > 8 ? genId[..8] : genId;
                var t = g.Text ?? "";
                return $"{g.ProfileName}:  {(t.Length > 70 ? t[..70] + "…" : t)}";
            }

            var card = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var del = Theme.SecondaryButton("Delete story");
            del.Padding = new Thickness(12, 4, 12, 4);
            del.Click += async (_, _) =>
            {
                try { await VoiceBoxApi.DeleteStoryAsync(detail.Id); } catch { }
                _storyId = null;
                Toasts.Info("Story deleted");
                Rebuild();
            };
            DockPanel.SetDock(del, Dock.Right);
            head.Children.Add(del);
            head.Children.Add(new TextBlock
            {
                Text = detail.Name, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center,
            });
            card.Children.Add(head);

            if (detail.Items.Count == 0)
                card.Children.Add(Subtle("No lines yet — add generated clips below; each lands right after the previous one."));
            foreach (var it in detail.Items.OrderBy(i => i.StartTimeMs))
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var itemId = it.Id;
                var rm = PageChrome.IconButton("", "Remove from story", async () =>
                {
                    try { await VoiceBoxApi.DeleteStoryItemAsync(detail.Id, itemId); } catch { }
                    Rebuild();
                });
                DockPanel.SetDock(rm, Dock.Right);
                row.Children.Add(rm);
                var line = new TextBlock { FontSize = 13, Foreground = Theme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                line.Inlines.Add(new Run($"{TimeSpan.FromMilliseconds(it.StartTimeMs):m\\:ss\\.f}   ")
                {
                    Foreground = Theme.SubtleBrush,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                });
                line.Inlines.Add(new Run(GenLabel(it.GenerationId)));
                row.Children.Add(line);
                card.Children.Add(row);
            }

            var addRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var addBtn = Theme.SecondaryButton("Add line");
            addBtn.Padding = new Thickness(14, 5, 14, 5);
            DockPanel.SetDock(addBtn, Dock.Right);
            var genCombo = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
            foreach (var g in gens) genCombo.Items.Add(GenLabel(g.Id));
            if (genCombo.Items.Count > 0) genCombo.SelectedIndex = 0;
            addBtn.Click += async (_, _) =>
            {
                if (genCombo.SelectedIndex < 0) { return; }
                try { await VoiceBoxApi.AddStoryItemAsync(detail.Id, gens[genCombo.SelectedIndex].Id); } catch { }
                Rebuild();
            };
            addRow.Children.Add(addBtn);
            addRow.Children.Add(genCombo);
            card.Children.Add(addRow);
            if (gens.Count == 0)
                card.Children.Add(Subtle("Generate some clips first — completed generations become the lines you arrange here.", 11.5));

            var st = Subtle("", 12);
            st.Margin = new Thickness(2, 8, 0, 0);
            var actRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var play = Theme.PrimaryButton("Play story");
            play.Click += async (_, _) =>
            {
                try
                {
                    st.Text = "Mixing the story…";
                    StopPlayback();
                    var bytes = await VoiceBoxApi.ExportStoryAudioAsync(detail.Id);
                    var tmp = Path.Combine(Path.GetTempPath(), $"story-{detail.Id[..8]}.wav");
                    await File.WriteAllBytesAsync(tmp, bytes);
                    _lastWav = tmp;
                    st.Text = "Playing the mixed story.";
                    Play(tmp);
                }
                catch (Exception ex) { st.Text = $"Couldn't mix the story: {ex.Message}"; }
            };
            actRow.Children.Add(play);
            var stop = Theme.SecondaryButton("Stop");
            stop.Margin = new Thickness(8, 0, 0, 0);
            stop.Click += (_, _) => StopPlayback();
            actRow.Children.Add(stop);
            var export = Theme.SecondaryButton("Export WAV…");
            export.Margin = new Thickness(8, 0, 0, 0);
            export.Click += async (_, _) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "WAV audio|*.wav", FileName = $"{detail.Name}.wav" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    st.Text = "Mixing and exporting…";
                    var bytes = await VoiceBoxApi.ExportStoryAudioAsync(detail.Id);
                    await File.WriteAllBytesAsync(dlg.FileName, bytes);
                    st.Text = $"Exported to {dlg.FileName}";
                    Toasts.Success(st.Text);
                }
                catch (Exception ex) { st.Text = $"Export failed: {ex.Message}"; }
            };
            actRow.Children.Add(export);
            card.Children.Add(actRow);
            card.Children.Add(st);

            host.Children.Add(Theme.Card2(card));
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load stories: {ex.Message}"));
        }
    }

    private void NewStoryDialog()
    {
        var dlg = new Window
        {
            Title = "New story",
            Width = 460, Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Theme.Bg),
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Theme.Label("Name"));
        var nameBox = new TextBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 14 };
        root.Children.Add(nameBox);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Theme.SecondaryButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => dlg.Close();
        var create = Theme.PrimaryButton("Create");
        create.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
            try
            {
                var s = await VoiceBoxApi.CreateStoryAsync(nameBox.Text.Trim(), null);
                _storyId = s.Id;
            }
            catch (Exception ex) { MessageBox.Show(dlg, ex.Message, "Couldn't create story"); return; }
            Toasts.Success($"Story “{nameBox.Text.Trim()}” created");
            dlg.Close();
            Rebuild();
        };
        btns.Children.Add(cancel);
        btns.Children.Add(create);
        root.Children.Add(btns);
        dlg.Content = root;
        dlg.Loaded += (_, _) => nameBox.Focus();
        dlg.ShowDialog();
    }

    // ── Effects (shown when Native is off) ─────────────────────────────────

    private async Task BuildEffectsAsync()
    {
        var host = new StackPanel();
        _tabCache["Effects"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading effects…"));
        try
        {
            var presets = await VoiceBoxApi.GetEffectPresetsAsync();
            host.Children.Clear();
            host.Children.Add(Subtle("Effect processing needs an audio library with no ARM64 build yet, so effects are pass-through in native mode. For real effect rendering, use the emulated app below.", 12.5));
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var p in presets)
            {
                var row = new StackPanel { Margin = new Thickness(20, 10, 12, 10) };
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
                if (!string.IsNullOrWhiteSpace(p.Description)) row.Children.Add(Subtle(p.Description!, 12));
                list.Children.Add(row);
                if (p != presets[^1]) list.Children.Add(Theme.Divider());
            }
            host.Children.Add(new Border
            {
                Background = Theme.SurfaceBrush,
                BorderBrush = new SolidColorBrush(Theme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = list,
            });
            var open = Theme.SecondaryButton("Open the emulated VoiceBox app (full effects)");
            open.Margin = new Thickness(0, 14, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) => SwitchToEmulated();
            host.Children.Add(open);
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load effects: {ex.Message}"));
        }
    }

    private void SwitchToEmulated()
    {
        Settings.Current.VoiceBoxUseEmulated = true;
        Settings.Current.Save("voicebox");
        (Window.GetWindow(this) as MainWindow)?.CaptureNavigate("VoiceBox");
    }

    // ── Captures (shown when Native is off) ────────────────────────────────

    private async Task BuildCapturesAsync()
    {
        var host = new StackPanel();
        _tabCache["Captures"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading captures…"));
        try
        {
            var caps = await VoiceBoxApi.GetCapturesAsync();
            host.Children.Clear();
            host.Children.Add(Subtle("VoiceBox's own capture/dictation feature. Heads-up: LiquidFlow's Dictation and Meetings tabs are the first-class way to capture speech on this machine.", 12.5));
            if (caps.Count == 0)
            {
                host.Children.Add(Subtle("No captures recorded in VoiceBox yet.", 12.5));
                return;
            }
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var c in caps)
            {
                var row = new DockPanel { Margin = new Thickness(20, 10, 12, 10) };
                var id = c.Id;
                var del = PageChrome.IconButton("", "Delete capture", async () =>
                {
                    try { await VoiceBoxApi.DeleteCaptureAsync(id); } catch { }
                    Rebuild();
                });
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var text = c.TranscriptRefined ?? c.TranscriptRaw ?? "(no transcript)";
                var tb = new TextBlock { Text = text.Length > 140 ? text[..140] + "…" : text, FontSize = 13, Foreground = Theme.TextBrush, TextWrapping = TextWrapping.Wrap };
                row.Children.Add(tb);
                list.Children.Add(row);
                if (c != caps[^1]) list.Children.Add(Theme.Divider());
            }
            host.Children.Add(new Border
            {
                Background = Theme.SurfaceBrush,
                BorderBrush = new SolidColorBrush(Theme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = list,
            });
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load captures: {ex.Message}"));
        }
    }

    // ── Models ─────────────────────────────────────────────────────────────

    private async Task BuildModelsAsync()
    {
        var host = new StackPanel();
        _tabCache["Models"] = host;
        _body.Children.Add(host);
        FadeIn(host);
        host.Children.Add(Subtle("Loading engines…"));
        try
        {
            var models = await VoiceBoxApi.GetModelsAsync();
            static bool EmulatedOnly(string n) =>
                n.StartsWith("chatterbox") || n.StartsWith("luxtts") || n.StartsWith("tada");
            static bool CaptureStack(string n) => n.StartsWith("whisper") || n.StartsWith("qwen3-");
            models = Settings.Current.VoiceBoxNativeOnly
                ? models.Where(m => !EmulatedOnly(m.ModelName) && !CaptureStack(m.ModelName)).ToList()
                : models;
            host.Children.Clear();
            host.Children.Add(Subtle("Voice engines download once and run locally. Kokoro is the fast pick on this machine; the larger engines run on CPU and take noticeably longer per generation.", 12.5));
            if (!Settings.Current.VoiceBoxNativeOnly)
                host.Children.Add(Subtle("Whisper/Qwen3 entries below serve VoiceBox's Captures feature only — LiquidFlow's own speech models live in Settings.", 11.5));

            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var m in models)
            {
                var grid = new Grid { Margin = new Thickness(20, 12, 12, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = m.DisplayName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
                var meta = Subtle(m.Downloaded
                    ? (m.SizeMb is > 0 ? (m.SizeMb >= 1024 ? $"downloaded · {m.SizeMb / 1024.0:0.0} GB" : $"downloaded · {m.SizeMb:0} MB") : "downloaded")
                    : "not downloaded", 11.5);
                info.Children.Add(meta);
                var bar = new ProgressStripe(280, 5)
                {
                    Margin = new Thickness(0, 8, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Visibility = Visibility.Collapsed,
                };
                info.Children.Add(bar);
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                void WatchProgress()
                {
                    // /tasks/active is the reliable source: percent, bytes, AND error details
                    // (the SSE stream goes silent when a task dies — that hid real failures).
                    bar.Visibility = Visibility.Visible;
                    bar.SetIndeterminate();
                    _ = Task.Run(async () =>
                    {
                        for (int tick = 0; tick < 7200; tick++)
                        {
                            await Task.Delay(1000);
                            List<VoiceBoxApi.DownloadTask> tasks;
                            try { tasks = await VoiceBoxApi.GetActiveDownloadsAsync(); }
                            catch { continue; }
                            var t = tasks.FirstOrDefault(x => x.ModelName == m.ModelName);
                            if (t is null) break; // finished or dismissed — refresh below
                            if (t.Status == "error")
                            {
                                var err = t.Error ?? "unknown error";
                                _ = Dispatcher.BeginInvoke(() => Toasts.Error($"{m.DisplayName} download failed: {err}"));
                                try { await VoiceBoxApi.CancelDownloadAsync(m.ModelName); } catch { }
                                break;
                            }
                            var frac = t.Total is > 0 ? (double)(t.Current ?? 0) / t.Total.Value
                                : t.Progress is > 0 ? t.Progress.Value / 100.0 : -1;
                            var mb = t.Total is > 0 ? $" · {(t.Current ?? 0) / 1048576} / {t.Total / 1048576} MB" : "";
                            _ = Dispatcher.BeginInvoke(() =>
                            {
                                if (frac >= 0) bar.SetFraction(frac); else bar.SetIndeterminate();
                                meta.Text = t.Status == "extracting" ? "extracting…"
                                    : frac >= 0 ? $"downloading… {frac * 100:0}%{mb}" : "downloading…";
                            });
                        }
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            Invalidate("Models");
                            if (_tab < CurrentTabs.Length && CurrentTabs[_tab] == "Models") BuildStudio();
                        });
                    });
                }
                if (m.Downloading) WatchProgress();

                if (m.Loaded)
                {
                    var chip = Theme.Pill("Loaded", Theme.GreenSoftBrush, Theme.GreenBrush, 11);
                    chip.VerticalAlignment = VerticalAlignment.Center;
                    chip.Margin = new Thickness(8, 0, 8, 0);
                    Grid.SetColumn(chip, 1);
                    grid.Children.Add(chip);
                }

                var act = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (EmulatedOnly(m.ModelName))
                {
                    var x64Chip = Theme.Pill("x64 app", Theme.InkBrush, new SolidColorBrush(Theme.InkText), 11);
                    x64Chip.VerticalAlignment = VerticalAlignment.Center;
                    x64Chip.Margin = new Thickness(0, 0, 8, 0);
                    act.Children.Add(x64Chip);
                    var open = Theme.SecondaryButton("Use emulated app");
                    open.Padding = new Thickness(12, 4, 12, 4);
                    open.Click += (_, _) => SwitchToEmulated();
                    act.Children.Add(open);
                }
                else if (!m.Downloaded && !m.Downloading)
                {
                    var dl = Theme.SecondaryButton("Download");
                    dl.Padding = new Thickness(12, 4, 12, 4);
                    dl.Click += async (_, _) =>
                    {
                        dl.IsEnabled = false;
                        Toasts.Info($"Downloading {m.DisplayName} in the background");
                        try { await VoiceBoxApi.DownloadModelAsync(m.ModelName); } catch (Exception ex) { Toasts.Error($"Download failed to start: {ex.Message}"); dl.IsEnabled = true; return; }
                        WatchProgress();
                    };
                    act.Children.Add(dl);
                }
                else if (m.Downloading)
                {
                    var cancelDl = Theme.SecondaryButton("Cancel");
                    cancelDl.Padding = new Thickness(12, 4, 12, 4);
                    cancelDl.Click += async (_, _) =>
                    {
                        try { await VoiceBoxApi.CancelDownloadAsync(m.ModelName); } catch { }
                        Toasts.Info($"{m.DisplayName} download cancelled");
                        Rebuild();
                    };
                    act.Children.Add(cancelDl);
                }
                else if (m.Loaded)
                {
                    var un = Theme.SecondaryButton("Unload");
                    un.Padding = new Thickness(12, 4, 12, 4);
                    un.Click += async (_, _) =>
                    {
                        meta.Text = "unloading…";
                        try { await VoiceBoxApi.UnloadModelAsync(m.ModelName); } catch { }
                        Toasts.Info($"{m.DisplayName} unloaded");
                        Rebuild();
                    };
                    act.Children.Add(un);
                }
                else if (m.Downloaded)
                {
                    var rm = Theme.SecondaryButton("Remove");
                    rm.Padding = new Thickness(12, 4, 12, 4);
                    rm.ToolTip = "Delete the downloaded engine from disk (you can re-download it anytime)";
                    rm.Click += async (_, _) =>
                    {
                        rm.IsEnabled = false;
                        bar.Visibility = Visibility.Visible;
                        bar.SetIndeterminate();
                        meta.Text = "removing…";
                        try { await VoiceBoxApi.DeleteModelAsync(m.ModelName); } catch { }
                        Toasts.Info($"{m.DisplayName} removed");
                        Rebuild();
                    };
                    act.Children.Add(rm);
                }
                Grid.SetColumn(act, 2);
                grid.Children.Add(act);

                list.Children.Add(grid);
                if (m != models[^1]) list.Children.Add(Theme.Divider());
            }
            host.Children.Add(new Border
            {
                Background = Theme.SurfaceBrush,
                BorderBrush = new SolidColorBrush(Theme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = list,
            });
            host.Children.Add(Subtle(
                $"Agent voices: an MCP server runs locally at http://127.0.0.1:{VoiceBoxNative.Port}/mcp — point Claude Code or any MCP client at it to give your agents these voices.",
                12));
            ((TextBlock)host.Children[^1]).Margin = new Thickness(2, 12, 0, 0);
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(Subtle($"Couldn't load engines: {ex.Message}"));
        }
    }

}
