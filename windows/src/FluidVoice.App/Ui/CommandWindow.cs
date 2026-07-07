using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Modes;

namespace FluidVoice.Ui;

/// <summary>Command Mode chat window (CommandModeView.swift): conversation + follow-up input + confirmation gate.</summary>
public sealed class CommandWindow : Window
{
    private readonly CommandModeService _service;
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new();
    private readonly Border _confirmBar;
    private readonly TextBlock _confirmText = new();

    private static readonly Color CommandRed = Color.FromArgb(255, 255, 89, 89);

    public CommandWindow(CommandModeService service)
    {
        _service = service;
        Title = "Command Mode";
        Width = 520;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(18, 18, 20));
        Topmost = true;

        var grid = new Grid { Margin = new Thickness(0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(16, 12, 16, 8) };
        var title = new TextBlock { Text = "Command Mode", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CommandRed) };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        var newChat = new Button { Content = "New", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Right };
        newChat.Click += (_, _) => _service.NewChat();
        DockPanel.SetDock(newChat, Dock.Right);
        header.Children.Add(newChat);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8, 0, 8, 0) };
        _messages.Margin = new Thickness(8);
        _scroll.Content = _messages;
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(_scroll);

        _confirmBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 89, 89)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
        };
        var confirmPanel = new StackPanel();
        _confirmText.Foreground = Brushes.White;
        _confirmText.TextWrapping = TextWrapping.Wrap;
        _confirmText.FontFamily = new FontFamily("Consolas");
        confirmPanel.Children.Add(new TextBlock { Text = "⚠ Destructive command — run it?", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        confirmPanel.Children.Add(_confirmText);
        var confirmButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var runBtn = new Button { Content = "Run Command", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        runBtn.Click += async (_, _) => await _service.ConfirmPendingAsync();
        var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(10, 4, 10, 4) };
        cancelBtn.Click += (_, _) => _service.CancelPending();
        confirmButtons.Children.Add(runBtn);
        confirmButtons.Children.Add(cancelBtn);
        confirmPanel.Children.Add(confirmButtons);
        _confirmBar.Child = confirmPanel;
        Grid.SetRow(_confirmBar, 2);
        grid.Children.Add(_confirmBar);

        var inputBar = new DockPanel { Margin = new Thickness(12) };
        _input.Background = new SolidColorBrush(Color.FromRgb(39, 39, 42));
        _input.Foreground = Brushes.White;
        _input.Padding = new Thickness(8);
        _input.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
        _input.KeyDown += OnInputKeyDown;
        var send = new Button { Content = "Send", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        send.Click += async (_, _) => await SendAsync();
        DockPanel.SetDock(send, Dock.Right);
        inputBar.Children.Add(send);
        inputBar.Children.Add(_input);
        Grid.SetRow(inputBar, 3);
        grid.Children.Add(inputBar);

        Content = grid;
        _service.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        _service.ConfirmationNeeded += _ => Dispatcher.BeginInvoke(Refresh);
    }

    public void OpenWindow()
    {
        Refresh();
        Show();
        Activate();
        _input.Focus();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private async Task SendAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        _input.Text = "";
        await _service.ProcessUserCommandAsync(text);
    }

    private void Refresh()
    {
        _messages.Children.Clear();
        foreach (var m in _service.Current.Messages)
        {
            var (bg, align) = m.Role switch
            {
                ChatRole.User => (Color.FromArgb(64, 255, 89, 89), HorizontalAlignment.Right),
                ChatRole.Tool => (Color.FromRgb(30, 30, 34), HorizontalAlignment.Left),
                _ => (Color.FromArgb(20, 255, 255, 255), HorizontalAlignment.Left),
            };
            var bubble = new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4),
                MaxWidth = 380,
                HorizontalAlignment = align,
            };
            var content = m.ToolCommand is not null
                ? $"$ {m.ToolCommand}"
                : m.Role == ChatRole.Tool ? SummarizeToolResult(m.Content) : m.Content;
            bubble.Child = new TextBlock
            {
                Text = content,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = m.ToolCommand is not null || m.Role == ChatRole.Tool ? new FontFamily("Consolas") : new FontFamily("Segoe UI"),
                FontSize = 13,
            };
            _messages.Children.Add(bubble);
        }
        if (_service.IsProcessing)
            _messages.Children.Add(new TextBlock { Text = "Working…", Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), Margin = new Thickness(4) });

        _confirmBar.Visibility = _service.PendingCommandJson is not null ? Visibility.Visible : Visibility.Collapsed;
        _confirmText.Text = _service.PendingCommandJson ?? "";

        _scroll.ScrollToEnd();
    }

    private static string SummarizeToolResult(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            var output = doc.RootElement.TryGetProperty("output", out var o) ? o.GetString() ?? "" : "";
            var err = doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
            var body = success ? output : (err ?? output);
            body = body.Trim();
            if (body.Length > 500) body = body[..500] + "…";
            return (success ? "✓ " : "✗ ") + (body.Length == 0 ? "(no output)" : body);
        }
        catch { return json; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
