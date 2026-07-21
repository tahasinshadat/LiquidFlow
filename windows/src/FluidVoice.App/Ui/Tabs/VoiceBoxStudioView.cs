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

    private void BuildStudio()
    {
        var tabs = CurrentTabs;
        if (_tab >= tabs.Length) _tab = 0;
        _body.Children.Clear();
        _body.Children.Add(PageChrome.TabsRow(tabs, _tab, i =>
        {
            _tab = i;
            if (_ready) BuildStudio();
        }));
        switch (tabs[_tab])
        {
            case "Generate": _body.Children.Add(BuildGenerate()); break;
            case "Clone": _body.Children.Add(BuildClone()); break;
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
        // _status is a long-lived field (GenerateAsync writes it after awaits) — detach it
        // from the previous build's card first, or WPF throws "already the logical child
        // of another element" and the whole tab renders blank.
        (_status.Parent as Panel)?.Children.Remove(_status);
        var p = new StackPanel();
        p.Children.Add(Theme.Label("Voice"));
        _voice = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var prof in _profiles)
            _voice.Items.Add($"{prof.Name}  ·  {EngineLabel(prof.DefaultEngine ?? prof.PresetEngine)}");
        if (_voice.Items.Count > 0) _voice.SelectedIndex = 0;
        _voice.SelectionChanged += (_, _) => SyncInstructVisibility();
        p.Children.Add(_voice);

        _instructRow = new StackPanel();
        _instructRow.Children.Add(Theme.Label("Delivery instructions (this engine supports them)"));
        _instruct = new TextBox
        {
            FontSize = 13.5, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "e.g. Speak slowly with warmth · Professional, broadcast quality",
        };
        _instructRow.Children.Add(_instruct);
        p.Children.Add(_instructRow);

        p.Children.Add(Theme.Label("Text"));
        _text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 110, MaxHeight = 240, Padding = new Thickness(10), FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = "At your service. All systems are online and running well.",
        };
        p.Children.Add(_text);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        _generate = Theme.PrimaryButton("Generate & play");
        _generate.Click += async (_, _) => await GenerateAsync();
        row.Children.Add(_generate);
        var stop = Theme.SecondaryButton("Stop");
        stop.Margin = new Thickness(8, 0, 0, 0);
        stop.Click += (_, _) => StopPlayback();
        row.Children.Add(stop);
        var save = Theme.SecondaryButton("Save WAV…");
        save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += (_, _) => SaveLast();
        row.Children.Add(save);
        p.Children.Add(row);
        p.Children.Add(_status);
        SyncInstructVisibility();
        return Theme.Card2(p);
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
                    Play(_lastWav);
                    return;
                }
                if (s.Status is "failed" or "error" or "cancelled")
                {
                    _status.Text = $"Generation {s.Status}: {s.Error ?? "unknown error"}";
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
                    _rec.DataAvailable += (_, e) =>
                    {
                        _recBytes?.Write(e.Buffer, 0, e.BytesRecorded);
                        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                            _recSamples.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                    };
                    _rec.StartRecording();
                    _recStart = DateTime.Now;
                    mic.Background = new SolidColorBrush(Theme.Danger);
                    micGlyph.Text = "";
                    _recTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _recTimer.Tick += (_, _) =>
                    {
                        var e = DateTime.Now - _recStart;
                        hint.Text = $"Recording  {(int)e.TotalMinutes}:{e.Seconds:00} — click to finish (auto-stops at 1:00)";
                        if (e.TotalSeconds >= 60) mic.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
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
            StopRecording(discard: false);
            mic.Background = Theme.InkBrush;
            micGlyph.Text = "";
            hint.Text = "Click the mic and speak naturally — 15–30 seconds is ideal. Click again to finish.";
            if (seconds < 3)
            {
                st.Text = "That was under 3 seconds — give it a bit more speech and try again.";
                return;
            }

            _cloneBusy = true;
            try
            {
                // write the sample wav
                var dir = Path.Combine(FluidVoice.Core.AppPaths.DataDir, "Voices", "CloneSamples");
                Directory.CreateDirectory(dir);
                var wavPath = Path.Combine(dir, $"clone-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
                await using (var writer = new WaveFileWriter(wavPath, new WaveFormat(16000, 1)))
                    writer.Write(raw, 0, raw.Length);

                st.Text = "Transcribing your recording locally…";
                var transcript = await TryTranscribeAsync(samples);
                var reference = string.IsNullOrWhiteSpace(transcript)
                    ? "A natural reference recording of my voice."
                    : transcript!;

                var name = UniqueVoiceName(string.IsNullOrWhiteSpace(nameBox.Text) ? "My voice" : nameBox.Text.Trim());
                st.Text = $"Creating “{name}”…";
                var prof = await VoiceBoxApi.CreateClonedProfileAsync(name, "Cloned from a quick in-app recording");
                st.Text = "Uploading your sample…";
                await VoiceBoxApi.UploadSampleAsync(prof.Id, wavPath, reference);
                _profiles = await VoiceBoxApi.GetProfilesAsync();
                nameBox.Text = UniqueVoiceName("My voice");

                var doneMsg = $"“{name}” is ready — pick it on the Generate tab.";
                try
                {
                    var qwen = (await VoiceBoxApi.GetModelsAsync()).FirstOrDefault(m => m.ModelName == "qwen-tts-0.6B");
                    if (qwen is { Downloaded: false })
                        doneMsg += "  One more thing: download “Qwen TTS 0.6B” under Models — cloned voices speak through it.";
                }
                catch { }
                st.Text = doneMsg;
                if (!string.IsNullOrWhiteSpace(transcript))
                    st.Text += $"\nHeard: “{(transcript!.Length > 90 ? transcript[..90] + "…" : transcript)}”";
            }
            catch (Exception ex)
            {
                Log.Error("voicebox", "One-click clone failed", ex);
                st.Text = $"Couldn't clone: {ex.Message}";
            }
            finally
            {
                _cloneBusy = false;
            }
        };

        p.Children.Add(Subtle("Have a recording already? Voices → Add voice → “Clone from my audio”.", 11.5));
        ((TextBlock)p.Children[^1]).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)p.Children[^1]).Margin = new Thickness(0, 14, 0, 0);
        return Theme.Card2(p);
    }

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
        _body.Children.Add(host);
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

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

                var del = PageChrome.IconButton("", "Delete voice", async () =>
                {
                    try { await VoiceBoxApi.DeleteProfileAsync(prof.Id); } catch { }
                    Rebuild();
                });
                del.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(del, 2);
                grid.Children.Add(del);

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

    private void Rebuild() => Dispatcher.BeginInvoke(BuildStudio);

    // ── History ────────────────────────────────────────────────────────────

    private async Task BuildHistoryAsync()
    {
        var host = new StackPanel();
        _body.Children.Add(host);
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
                if (g.Status == "completed")
                    actions.Children.Add(PageChrome.IconButton("", "Play", async () =>
                    {
                        try
                        {
                            StopPlayback();
                            var bytes = await VoiceBoxApi.GetAudioAsync(g.Id);
                            var tmp = Path.Combine(Path.GetTempPath(), $"vb-{g.Id[..8]}.wav");
                            await File.WriteAllBytesAsync(tmp, bytes);
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
        _body.Children.Add(host);
        host.Children.Add(Subtle("Loading stories…"));
        try
        {
            List<VoiceBoxApi.Story> stories;
            try { stories = await VoiceBoxApi.GetStoriesAsync(); }
            catch { await Task.Delay(1200); stories = await VoiceBoxApi.GetStoriesAsync(); } // boot race: retry once
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
        _body.Children.Add(host);
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
        _body.Children.Add(host);
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
        _body.Children.Add(host);
        host.Children.Add(Subtle("Loading engines…"));
        try
        {
            var models = await VoiceBoxApi.GetModelsAsync();
            // Chatterbox/LuxTTS/TADA need torchaudio (no ARM64 wheels) — emulation only.
            static bool EmulatedOnly(string n) =>
                n.StartsWith("chatterbox") || n.StartsWith("luxtts") || n.StartsWith("tada");
            // Whisper/Qwen3 here exist only for VoiceBox's Captures/refine — LiquidFlow
            // manages its own speech + AI models in Settings, so don't duplicate them.
            // They stay visible in non-native mode, where the Captures tab can use them.
            static bool CaptureStack(string n) => n.StartsWith("whisper") || n.StartsWith("qwen3-");
            models = Settings.Current.VoiceBoxNativeOnly
                ? models.Where(m => !EmulatedOnly(m.ModelName) && !CaptureStack(m.ModelName)).ToList()
                : models;
            host.Children.Clear();
            host.Children.Add(Subtle("Voice engines download once and run locally. Kokoro is the fast pick on this machine; the larger engines run on CPU and take noticeably longer per generation.", 12.5));
            if (!Settings.Current.VoiceBoxNativeOnly)
                host.Children.Add(Subtle("Whisper/Qwen3 entries below serve VoiceBox's Captures feature only — LiquidFlow's own speech models live in Settings.", 11.5));

            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var anyDownloading = false;
            foreach (var m in models)
            {
                var grid = new Grid { Margin = new Thickness(20, 12, 12, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = m.DisplayName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush });
                var meta = m.Downloaded ? $"downloaded{(m.SizeMb is > 0 ? $" · {m.SizeMb / 1024.0:0.0} GB".Replace("0.3 GB", "313 MB") : "")}" : "not downloaded";
                if (m.SizeMb is > 0 and < 1024) meta = $"downloaded · {m.SizeMb:0} MB";
                info.Children.Add(Subtle(m.Downloading ? "downloading…" : meta, 11.5));
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                if (m.Loaded)
                {
                    var chip = Theme.Pill("Loaded", Theme.GreenSoftBrush, Theme.GreenBrush, 11);
                    chip.VerticalAlignment = VerticalAlignment.Center;
                    chip.Margin = new Thickness(8, 0, 8, 0);
                    Grid.SetColumn(chip, 1);
                    grid.Children.Add(chip);
                }
                anyDownloading |= m.Downloading;

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
                        try { await VoiceBoxApi.DownloadModelAsync(m.ModelName); } catch { }
                        Rebuild();
                    };
                    act.Children.Add(dl);
                }
                else if (m.Loaded)
                {
                    var un = Theme.SecondaryButton("Unload");
                    un.Padding = new Thickness(12, 4, 12, 4);
                    un.Click += async (_, _) =>
                    {
                        try { await VoiceBoxApi.UnloadModelAsync(m.ModelName); } catch { }
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
                        try { await VoiceBoxApi.DeleteModelAsync(m.ModelName); } catch { }
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
            if (anyDownloading)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (_, _) => { t.Stop(); if (IsLoaded && _tab < CurrentTabs.Length && CurrentTabs[_tab] == "Models") Rebuild(); };
                t.Start();
            }
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
