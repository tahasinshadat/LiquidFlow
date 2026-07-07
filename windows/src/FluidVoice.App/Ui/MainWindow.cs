using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluidVoice.Ui;

/// <summary>Main window placeholder — replaced by the full dashboard/settings UI.</summary>
public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "FluidVoice";
        Width = 900;
        Height = 620;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 27));
        Content = new TextBlock
        {
            Text = "FluidVoice for Windows",
            Foreground = Brushes.White,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // hide to tray instead of quitting (mac: menu-bar app behavior)
        e.Cancel = true;
        Hide();
    }
}
