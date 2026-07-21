using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FluidVoice.Ui;

/// <summary>
/// Hosts VoiceBox's web UI (server/Docker mode) inside a LiquidFlow-chromed window with a
/// "Back to LiquidFlow" button — the in-app routing the sidebar entry promises.
/// </summary>
public sealed class VoiceBoxWebWindow : Window
{
    private readonly WebView2 _web = new();

    public VoiceBoxWebWindow(string url)
    {
        Title = "VoiceBox";
        Width = 1280;
        Height = 840;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Theme.Bg);
        WindowFx.Apply(this);

        // "← Back to LiquidFlow" lives in the titlebar's leading slot
        var back = new Border
        {
            Background = new SolidColorBrush(Theme.SidebarSelected),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "←  Back to LiquidFlow",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextBrush,
            },
        };
        back.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()?.Activate();
        };

        var outer = new Grid { Background = new SolidColorBrush(Theme.Bg) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titlebar = WindowFx.InstallChrome(this, "VoiceBox", back);
        outer.Children.Add((UIElement)titlebar);
        Grid.SetRow(_web, 1);
        outer.Children.Add(_web);
        Content = outer;

        Loaded += async (_, _) =>
        {
            try
            {
                var userData = Path.Combine(AppPaths.DataDir, "WebView2-VoiceBox");
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Log.Error("voicebox", "WebView2 init failed", ex);
                MessageBox.Show(this,
                    $"Couldn't load {url}.\n\nMake sure VoiceBox's web/server mode is running, then try again.\n\n{ex.Message}",
                    "VoiceBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            }
        };
    }
}
