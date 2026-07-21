using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Audio;
using FluidVoice.Core;
using FluidVoice.Stt;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Transforms: the voice-powered actions that rewrite or produce text — Write (rewrite)
/// mode, Command mode, and on-device file transcription. (This hosts what the old
/// Scratchpad workspace held; Scratchpad is now quick notes, matching the reference.)
/// </summary>
public sealed class TransformsTab : StackPanel
{
    private readonly Action? _openCommand;
    private readonly Action? _openRewrite;
    private readonly DictationCoordinator? _coordinator;

    public TransformsTab(Action? openCommand, Action? openRewrite, DictationCoordinator? coordinator)
    {
        _openCommand = openCommand;
        _openRewrite = openRewrite;
        _coordinator = coordinator;
        Build();
    }

    private void Build()
    {
        Children.Add(new TextBlock
        {
            Text = "Voice-powered transforms: rewrite what's selected, run commands, or turn audio files into text — all on-device.",
            FontSize = 14,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -8, 0, 24),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(Card("Write Mode", "Rewrite selected text or dictate a fresh draft into the focused app.", BuildWriteControls()));
        left.Children.Add(Card("Command Mode", "Use voice instructions to operate your PC with confirmation controls.", BuildCommandControls()));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = Card("File Transcription", "Turn an audio file into text locally. Nothing is uploaded.", BuildFileControls());
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Children.Add(grid);
    }

    private static UIElement Card(string title, string subtitle, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(body);
        return Theme.Panel(panel, new Thickness(22), new Thickness(0, 0, 0, 18));
    }

    private UIElement BuildWriteControls()
    {
        var card = new StackPanel();
        card.Children.Add(Theme.Toggle("Enable Write Mode hotkey", Settings.Current.RewriteModeShortcutEnabled, v =>
        {
            Settings.Current.RewriteModeShortcutEnabled = v;
            Settings.Current.Save("hotkey");
        }));
        var open = Theme.PrimaryButton("Open edit window");
        open.Margin = new Thickness(0, 12, 0, 0);
        open.Click += (_, _) => _openRewrite?.Invoke();
        card.Children.Add(open);
        return card;
    }

    private UIElement BuildCommandControls()
    {
        var card = new StackPanel();
        card.Children.Add(Theme.Toggle("Enable Command Mode hotkey", Settings.Current.CommandModeShortcutEnabled, v =>
        {
            Settings.Current.CommandModeShortcutEnabled = v;
            Settings.Current.CommandModeShortcut ??= Input.HotkeyShortcut.RightCtrl();
            Settings.Current.Save("hotkey");
        }));
        card.Children.Add(Theme.Toggle("Ask before destructive commands", Settings.Current.CommandModeConfirmBeforeExecute, v =>
        {
            Settings.Current.CommandModeConfirmBeforeExecute = v;
            Settings.Current.Save();
        }));
        var open = Theme.PrimaryButton("Open command chat");
        open.Margin = new Thickness(0, 12, 0, 0);
        open.Click += (_, _) => _openCommand?.Invoke();
        card.Children.Add(open);
        return card;
    }

    private UIElement BuildFileControls()
    {
        var card = new StackPanel();
        var status = new TextBlock { Foreground = Theme.SubtleBrush, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
        var result = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 220, MaxHeight = 360, Padding = new Thickness(10),
            Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var copyBtn = Theme.SecondaryButton("Copy");
        copyBtn.Margin = new Thickness(0, 0, 8, 0);
        copyBtn.Click += (_, _) => ClipboardService.SetText(result.Text);
        var saveBtn = Theme.SecondaryButton("Save as .txt");
        saveBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text file|*.txt", FileName = "transcript.txt" };
            if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, result.Text);
        };
        buttons.Children.Add(copyBtn);
        buttons.Children.Add(saveBtn);

        var pick = Theme.PrimaryButton("Choose audio file");
        pick.Click += async (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.wma;*.aac|All files|*.*",
            };
            if (dlg.ShowDialog() != true || _coordinator is null) return;
            try
            {
                pick.IsEnabled = false;
                var model = SpeechModels.Selected();
                if (!model.IsDownloaded) { status.Text = "Download a speech model first."; return; }
                status.Text = "Loading model…";
                var engine = await _coordinator.EnsureEngineReadyAsync(model, null, CancellationToken.None);
                status.Text = "Reading audio…";
                var pcm = await Task.Run(() => AudioFileLoader.Load16kMono(dlg.FileName));
                status.Text = $"Transcribing {pcm.Length / 16000.0 / 60:0.0} min of audio…";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = await engine.TranscribeAsync(Dsp.Normalize(pcm), CancellationToken.None);
                status.Text = $"Done in {sw.Elapsed.TotalSeconds:0.0}s";
                result.Text = Text.TranscriptFormatter.Process(text);
                result.Visibility = Visibility.Visible;
                buttons.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                status.Text = $"Failed: {ex.Message}";
            }
            finally
            {
                pick.IsEnabled = true;
            }
        };

        card.Children.Add(pick);
        card.Children.Add(status);
        card.Children.Add(result);
        card.Children.Add(buttons);
        return card;
    }
}
