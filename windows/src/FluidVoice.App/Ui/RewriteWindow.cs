using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Modes;

namespace FluidVoice.Ui;

/// <summary>Edit Mode window (RewriteModeView.swift): shows selection, instruction box, result + Replace/Try Again.</summary>
public sealed class RewriteWindow : Window
{
    private readonly RewriteModeService _service;
    private readonly TextBox _instruction = new();
    private readonly TextBlock _originalBlock = new();
    private readonly TextBox _resultBlock = new();
    private readonly Button _replaceBtn;
    private readonly Button _tryAgainBtn;
    private readonly TextBlock _statusBlock = new();

    public RewriteWindow(RewriteModeService service)
    {
        _service = service;
        Title = "Edit Mode";
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 27));
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "✏️  Edit Mode", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12),
        });

        _originalBlock.Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
        _originalBlock.TextWrapping = TextWrapping.Wrap;
        _originalBlock.MaxHeight = 120;
        root.Children.Add(_originalBlock);

        _instruction.MinHeight = 60;
        _instruction.TextWrapping = TextWrapping.Wrap;
        _instruction.AcceptsReturn = true;
        _instruction.Margin = new Thickness(0, 8, 0, 8);
        _instruction.Background = new SolidColorBrush(Color.FromRgb(39, 39, 42));
        _instruction.Foreground = Brushes.White;
        _instruction.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
        _instruction.Padding = new Thickness(8);
        _instruction.KeyDown += OnInstructionKeyDown;
        root.Children.Add(_instruction);

        var submit = new Button { Content = "Generate  (Enter)", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 8) };
        submit.Click += async (_, _) => await RunAsync();
        root.Children.Add(submit);

        _resultBlock.IsReadOnly = true;
        _resultBlock.TextWrapping = TextWrapping.Wrap;
        _resultBlock.MinHeight = 100;
        _resultBlock.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        _resultBlock.Foreground = Brushes.White;
        _resultBlock.Padding = new Thickness(8);
        _resultBlock.Visibility = Visibility.Collapsed;
        root.Children.Add(_resultBlock);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _replaceBtn = new Button { Content = "Replace Original", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        _replaceBtn.Click += async (_, _) => await AcceptAsync();
        _tryAgainBtn = new Button { Content = "Try Again", Padding = new Thickness(10, 6, 10, 6), Visibility = Visibility.Collapsed };
        _tryAgainBtn.Click += (_, _) => { _service.TryAgain(); _instruction.Focus(); };
        buttons.Children.Add(_replaceBtn);
        buttons.Children.Add(_tryAgainBtn);
        root.Children.Add(buttons);

        _statusBlock.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 120, 120));
        _statusBlock.TextWrapping = TextWrapping.Wrap;
        _statusBlock.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_statusBlock);

        Content = root;
        _service.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
    }

    public void OpenForSession()
    {
        Refresh();
        Show();
        Activate();
        _instruction.Text = "";
        _instruction.Focus();
    }

    private void OnInstructionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        var instruction = _instruction.Text.Trim();
        if (instruction.Length == 0) return;
        await _service.ApplyInstructionAsync(instruction, CancellationToken.None);
    }

    private async Task AcceptAsync()
    {
        Hide();
        await _service.AcceptAsync();
    }

    private void Refresh()
    {
        _originalBlock.Text = _service.IsWriteMode
            ? "✍️  Write mode — no text selected. Describe what to write."
            : $"Selected:\n{_service.OriginalText}";
        var hasResult = _service.RewrittenText.Length > 0;
        _resultBlock.Text = _service.RewrittenText;
        _resultBlock.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        _replaceBtn.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        _tryAgainBtn.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        _statusBlock.Text = _service.IsProcessing ? "Thinking…" : _service.LastError ?? "";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
