using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FluidVoice.Ai;
using FluidVoice.Core;
using NAudio.Wave;

namespace FluidVoice.Ui;

/// <summary>
/// Voices: native ARM64 text-to-speech right inside LiquidFlow — the Kokoro preset voices
/// (Jarvis persona included) running through sherpa-onnx with no x64 emulation. VoiceBox
/// stays the studio for cloning/stories; this page is the instant "say it in a voice" path.
/// </summary>
public sealed class VoicesTab : StackPanel
{
    private readonly TextBlock _status = new()
    {
        FontSize = 12.5,
        Foreground = Theme.SubtleBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 10, 0, 0),
    };
    private readonly ProgressStripe _bar = new(420, 8) { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private ComboBox _voice = null!;
    private TextBox _text = null!;
    private Button _generate = null!;
    private double _speed = 1.0;
    private string? _lastWav;
    private WaveOutEvent? _player;
    private AudioFileReader? _reader;
    private CancellationTokenSource? _cts;

    public VoicesTab()
    {
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(BuildHero());
        Children.Add(VoiceStudio.IsInstalled ? BuildStudio() : BuildInstallCard());
        Children.Add(new TextBlock
        {
            Text = "Runs natively on this ARM chip — Kokoro 82M (Apache-2.0, hexgrad) via sherpa-onnx. For voice cloning, delivery instructions, and multi-track stories, open the VoiceBox tab.",
            FontSize = 11.5,
            FontStyle = FontStyles.Italic,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 18, 0, 0),
        });
    }

    private UIElement BuildHero()
    {
        var content = new StackPanel { Margin = new Thickness(40, 28, 40, 28), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        var title = new TextBlock { FontFamily = Theme.DisplaySerif, FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) };
        title.Inlines.Add(new Run("Say it in "));
        title.Inlines.Add(new Run("any voice") { FontStyle = FontStyles.Italic });
        title.Inlines.Add(new Run("."));
        content.Children.Add(title);
        content.Children.Add(new TextBlock
        {
            Text = "53 built-in voices — Jarvis included — generated natively on your Snapdragon in about a second. No emulation, no waiting for engines to boot.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
        });
        var hero = PageChrome.DarkHero(content);
        ((Border)hero).MinHeight = 190;
        ((Border)hero).Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    // ---- first-run: model download ----

    private UIElement BuildInstallCard()
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock
        {
            Text = "One-time setup",
            FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        p.Children.Add(new TextBlock
        {
            Text = "Download the Kokoro voice pack (126 MB) — all 53 voices, stored locally, works offline.",
            FontSize = 13.5, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap,
        });
        var btn = Theme.PrimaryButton("Download voices (126 MB)");
        btn.Margin = new Thickness(0, 14, 0, 0);
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false;
            _bar.Visibility = Visibility.Visible;
            _cts = new CancellationTokenSource();
            var progress = new Progress<(string Phase, double Pct)>(x => Dispatcher.BeginInvoke(() =>
            {
                _status.Text = x.Phase;
                if (x.Pct < 0) _bar.SetIndeterminate();
                else _bar.SetFraction(x.Pct);
            }));
            try
            {
                await VoiceStudio.DownloadAsync(progress, _cts.Token);
                Build();
            }
            catch (Exception ex)
            {
                _status.Text = $"Download failed: {ex.Message} — check your connection and try again.";
                btn.IsEnabled = true;
                _bar.Visibility = Visibility.Collapsed;
            }
        };
        p.Children.Add(btn);
        p.Children.Add(_bar);
        p.Children.Add(_status);
        return Theme.Card2(p);
    }

    // ---- the studio ----

    private UIElement BuildStudio()
    {
        var p = new StackPanel();

        p.Children.Add(Theme.Label("Voice"));
        _voice = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var v in VoiceStudio.Voices)
            _voice.Items.Add($"{v.Name} — {v.Blurb}");
        _voice.SelectedIndex = 0;
        p.Children.Add(_voice);

        p.Children.Add(Theme.Label("Speed"));
        p.Children.Add(Theme.Slider(0.6, 1.5, _speed, v => _speed = v, v => $"{v:0.00}×"));

        p.Children.Add(Theme.Label("Text"));
        _text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxHeight = 240,
            Padding = new Thickness(10),
            FontSize = 14,
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
        var open = Theme.SecondaryButton("Open folder");
        open.Margin = new Thickness(8, 0, 0, 0);
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(VoiceStudio.OutputDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{VoiceStudio.OutputDir}\"") { UseShellExecute = true });
        };
        row.Children.Add(open);
        p.Children.Add(row);
        p.Children.Add(_status);

        Unloaded += (_, _) => StopPlayback();
        return Theme.Card2(p);
    }

    private async Task GenerateAsync()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) { _status.Text = "Type something to say first."; return; }
        var voice = VoiceStudio.Voices[Math.Max(0, _voice.SelectedIndex)];
        _generate.IsEnabled = false;
        _status.Text = $"Generating with {voice.Name}…";
        _cts = new CancellationTokenSource();
        try
        {
            StopPlayback();
            var (path, seconds, ms) = await VoiceStudio.GenerateAsync(text, voice.Id, (float)_speed, _cts.Token);
            _lastWav = path;
            _status.Text = $"{voice.Name}: {seconds:0.0}s of audio in {ms / 1000.0:0.0}s — saved to Generated folder.";
            _reader = new AudioFileReader(path);
            _player = new WaveOutEvent();
            _player.Init(_reader);
            _player.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(StopPlayback);
            _player.Play();
        }
        catch (Exception ex)
        {
            Log.Error("voices", "TTS generation failed", ex);
            _status.Text = $"Couldn't generate: {ex.Message}";
        }
        finally
        {
            _generate.IsEnabled = true;
        }
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
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV audio|*.wav",
            FileName = Path.GetFileName(_lastWav),
        };
        if (dlg.ShowDialog() == true)
        {
            File.Copy(_lastWav, dlg.FileName, overwrite: true);
            _status.Text = $"Saved to {dlg.FileName}";
        }
    }
}
