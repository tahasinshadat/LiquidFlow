using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Core;

namespace FluidVoice.Ui;

public sealed class SettingsModal : Window
{
    private readonly ContentControl _content = new();
    private readonly TextBlock _headerTitle = new();
    private readonly Dictionary<string, Border> _items = new();
    private readonly List<SettingsSection> _sections;

    private sealed record SettingsSection(string Glyph, string Title, Func<UIElement> Build);

    public SettingsModal(string initialSection = "General")
    {
        Title = "Settings";
        Width = 980;
        Height = 660;
        MinWidth = 880;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        _sections = new List<SettingsSection>
        {
            new(((char)0xE713).ToString(), "General", () => new GeneralTab()),
            new(((char)0xE720).ToString(), "Speech Models", () => new SpeechModelsTab()),
            new(((char)0xE945).ToString(), "AI Enhancement", () => new AiTab()),
            new(((char)0xE790).ToString(), "Formatting", () => new FormattingTab()),
            new(((char)0xE77B).ToString(), "Account", () => new AccountSettingsTab()),
        };

        var shell = new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
        };
        shell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        // content column = fixed header (title + close) over a scrolling body, so the
        // scrollbar lives only in the body and never runs under the close button.
        var contentColumn = new Grid();
        contentColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(40, 30, 14, 6) };
        _headerTitle.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _headerTitle.FontSize = 21;
        _headerTitle.FontWeight = FontWeights.SemiBold;
        _headerTitle.Foreground = Theme.TextBrush;
        _headerTitle.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_headerTitle);

        var close = new Button
        {
            Content = new TextBlock
            {
                Text = ((char)0xE8BB).ToString(), // MDL2 ChromeClose
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
            },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Close settings (Esc)",
        };
        close.Click += (_, _) => Close();
        header.Children.Add(close);
        Grid.SetRow(header, 0);
        contentColumn.Children.Add(header);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _content,
            Padding = new Thickness(40, 4, 26, 26),
            Margin = new Thickness(0, 0, 8, 10), // inset the scrollbar off the rounded corner
        };
        Grid.SetRow(scroller, 1);
        contentColumn.Children.Add(scroller);

        Grid.SetColumn(contentColumn, 1);
        grid.Children.Add(contentColumn);
        shell.Child = grid;
        WindowFx.RoundClip(shell, 18); // clip everything (incl. scrollbar) to the rounded shell
        Content = shell;

        _currentSection = initialSection;
        Loaded += (_, _) => Select(initialSection);
        // dev seam: cycle every section once so a broken tab surfaces immediately in tests
        if (Environment.GetEnvironmentVariable("FLUIDVOICE_SETTINGS_CYCLE") == "1")
            Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                foreach (var sec in _sections) Select(sec.Title);
                Select(initialSection);
            });
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        // Live theme/font swap: recolor the chrome + rebuild the open section so the
        // modal never shows mixed light/dark surfaces.
        FontFamily = Theme.UiFont;
        Action<string> onSettingsChanged = hint => Dispatcher.BeginInvoke(() =>
        {
            if (hint is not ("theme" or "font")) return;
            FontFamily = Theme.UiFont;
            shell.Background = Theme.SurfaceBrush;
            shell.BorderBrush = new SolidColorBrush(Theme.CardBorder);
            if (sidebar is Panel sp) sp.Background = new SolidColorBrush(Theme.Card);
            Select(_currentSection);
        });
        Settings.Changed += onSettingsChanged;
        Closed += (_, _) => Settings.Changed -= onSettingsChanged;
    }

    private string _currentSection = "General";

    private UIElement BuildSidebar()
    {
        var panel = new DockPanel
        {
            Background = new SolidColorBrush(Theme.Card),
            LastChildFill = true,
        };

        var footer = new DockPanel { Margin = new Thickness(24, 0, 24, 24), LastChildFill = false };
        footer.Children.Add(new TextBlock
        {
            Text = $"LiquidFlow {App.Updater.ThisVersion}",
            FontSize = 12,
            Foreground = Theme.SubtleBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        var stack = new StackPanel { Margin = new Thickness(18, 26, 12, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "SETTINGS",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(8, 0, 0, 22),
        });

        foreach (var section in _sections)
            stack.Children.Add(Item(section));

        DockPanel.SetDock(stack, Dock.Top);
        panel.Children.Add(stack);
        return panel;
    }

    private UIElement Item(SettingsSection section)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = section.Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = Theme.TextBrush,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var item = new Border
        {
            Child = row,
            Background = Brushes.Transparent, // always hit-testable, even before first Select
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 11, 12, 11),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
        };
        // Critical: swallow the press so it never reaches the shell's DragMove handler —
        // otherwise DragMove enters a modal loop and eats our MouseLeftButtonUp, and the
        // section never opens (this was the "Speech Models / AI Enhancement do nothing" bug).
        item.MouseLeftButtonDown += (_, e) => e.Handled = true;
        item.MouseLeftButtonUp += (_, _) => Select(section.Title);
        item.MouseEnter += (_, _) => { if (_currentSection != section.Title) item.Background = new SolidColorBrush(Theme.SidebarSelected) { Opacity = 0.5 }; };
        item.MouseLeave += (_, _) => { if (_currentSection != section.Title) item.Background = Brushes.Transparent; };
        _items[section.Title] = item;
        return item;
    }

    private void Select(string title)
    {
        var section = _sections.FirstOrDefault(s => s.Title == title) ?? _sections[0];
        _currentSection = section.Title;
        foreach (var (name, item) in _items)
            item.Background = name == section.Title ? new SolidColorBrush(Theme.SidebarSelected) : Brushes.Transparent;

        _headerTitle.Text = section.Title;
        _headerTitle.Foreground = Theme.TextBrush;
        var body = new StackPanel { LayoutTransform = Theme.PageScale() };
        body.Children.Add(section.Build());
        _content.Content = body;
        _content.Dispatcher.BeginInvoke(() => (_content.Parent as ScrollViewer)?.ScrollToTop());
    }
}

public sealed class AccountSettingsTab : StackPanel
{
    public AccountSettingsTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Label("First name"));
        var nameBox = new TextBox
        {
            Text = Settings.Current.DisplayName,
            Width = 320,
            FontSize = 15,
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        };
        panel.Children.Add(nameBox);

        panel.Children.Add(Theme.Label("Email"));
        panel.Children.Add(new TextBlock
        {
            Text = "Local account",
            FontSize = 15,
            Foreground = Theme.SubtleBrush,
            Margin = new Thickness(0, 0, 0, 18),
        });

        panel.Children.Add(Theme.Label("Profile picture"));
        panel.Children.Add(new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = Theme.GreenBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = Initials(Settings.Current.DisplayName),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        var actions = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 28, 0, 0) };
        var save = Theme.PrimaryButton("Save");
        save.Click += (_, _) =>
        {
            Settings.Current.DisplayName = nameBox.Text.Trim();
            Settings.Current.OnboardingCompleted = true;
            Settings.Current.Save("profile");
        };
        DockPanel.SetDock(save, Dock.Right);
        actions.Children.Add(save);
        var rerun = Theme.SecondaryButton("Run setup wizard");
        rerun.ToolTip = "Walk through hotkey, speech model, and AI setup again";
        rerun.Click += (_, _) =>
        {
            var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (main is null) return;
            Window.GetWindow(this)?.Close();
            // open after the modal's ShowDialog stack unwinds
            main.Dispatcher.BeginInvoke(main.RunSetupWizard);
        };
        actions.Children.Add(rerun);
        panel.Children.Add(actions);

        Children.Add(Theme.Panel(panel, new Thickness(24), new Thickness(0)));
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "FV";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString().ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }
}
