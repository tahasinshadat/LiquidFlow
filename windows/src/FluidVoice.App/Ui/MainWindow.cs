using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluidVoice.Core;
using FluidVoice.Modes;

namespace FluidVoice.Ui;

/// <summary>
/// The dashboard + settings window. Tabs mirror the mac SettingsView areas:
/// Home (stats), General (hotkey/overlay/theme), Speech (model picker/download),
/// AI (providers/keys/local AI), Formatting (filler/punctuation/dictionary), History.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TabControl _tabs = new();

    public MainWindow(CommandModeService? commandService = null)
    {
        Title = "FluidVoice";
        Width = 940;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.BgBrush;

        _tabs.Background = Theme.BgBrush;
        _tabs.BorderThickness = new Thickness(0);
        _tabs.Margin = new Thickness(8);

        AddTab("Home", new HomeTab());
        AddTab("General", new GeneralTab());
        AddTab("Speech Models", new SpeechModelsTab());
        AddTab("AI Enhancement", new AiTab());
        AddTab("Formatting", new FormattingTab());
        AddTab("Dictionary", new DictionaryTab());
        AddTab("History", new HistoryTab());

        Content = _tabs;
        Settings.Changed += _ => Dispatcher.BeginInvoke(() => { Background = Theme.BgBrush; });
    }

    public void SelectTab(string header)
    {
        foreach (TabItem t in _tabs.Items)
            if ((string)t.Header == header) { _tabs.SelectedItem = t; break; }
    }

    private void AddTab(string header, UIElement content)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(20), Child = content },
        };
        _tabs.Items.Add(new TabItem { Header = header, Content = scroll, Foreground = Theme.TextBrush });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Hide to tray (menu-bar app behavior); Quit is via the tray menu.
        e.Cancel = true;
        Hide();
    }
}
