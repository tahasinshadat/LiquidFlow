using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;

namespace FluidVoice.Ui;

public sealed class NamePromptDialog : Window
{
    private readonly TextBox _nameBox;

    public NamePromptDialog(string suggestedName)
    {
        Title = "Welcome to FluidVoice";
        Width = 430;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var shell = new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(34),
        };
        shell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Welcome to FluidVoice",
            FontFamily = Theme.DisplaySerif,
            FontSize = 30,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "What should FluidVoice call you?",
            FontSize = 15,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22),
        });

        _nameBox = new TextBox
        {
            Text = suggestedName,
            FontSize = 16,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 22),
        };
        panel.Children.Add(_nameBox);

        var row = new DockPanel { LastChildFill = false };
        var skip = Theme.SecondaryButton("Use system name");
        skip.Click += (_, _) => SaveAndClose(suggestedName);
        row.Children.Add(skip);

        var save = Theme.PrimaryButton("Continue");
        save.Margin = new Thickness(12, 0, 0, 0);
        save.Click += (_, _) => SaveAndClose(_nameBox.Text);
        DockPanel.SetDock(save, Dock.Right);
        row.Children.Add(save);
        panel.Children.Add(row);

        shell.Child = panel;
        Content = shell;

        Loaded += (_, _) =>
        {
            _nameBox.SelectAll();
            _nameBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) SaveAndClose(_nameBox.Text);
            if (e.Key == Key.Escape) SaveAndClose(suggestedName);
        };
    }

    private void SaveAndClose(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? Environment.UserName : name.Trim();
        Settings.Current.DisplayName = trimmed;
        Settings.Current.OnboardingCompleted = true;
        Settings.Current.Save("profile");
        DialogResult = true;
        Close();
    }
}
