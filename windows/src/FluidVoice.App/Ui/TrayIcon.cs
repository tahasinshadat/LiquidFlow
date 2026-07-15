using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FluidVoice.Audio;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// System tray icon + menu (MenuBarManager.swift / MenuBarIconGenerator.swift):
/// the app icon, with a red recording badge while dictating; menu with status line,
/// Open, Settings, Custom Dictionary, Microphone submenu, Check for Updates, Quit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _micMenu;
    private readonly ToolStripMenuItem _updateItem;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? DictionaryRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? InstallUpdateRequested;
    public event Action? BalloonClicked;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _idleIcon = CreateTrayIcon(recording: false);
        _recordingIcon = CreateTrayIcon(recording: true);

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Ready to Record") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open LiquidFlow", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Settings...", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Custom Dictionary", null, (_, _) => DictionaryRequested?.Invoke());
        _micMenu = new ToolStripMenuItem("Microphone");
        menu.Items.Add(_micMenu);
        // Green "install now" item — hidden until an update is found (SetUpdateAvailable).
        _updateItem = new ToolStripMenuItem("Install update")
        {
            Visible = false,
            ForeColor = Color.FromArgb(31, 122, 106),
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        };
        _updateItem.Click += (_, _) => InstallUpdateRequested?.Invoke();
        menu.Items.Add(_updateItem);
        menu.Items.Add("Check for Updates...", null, (_, _) => CheckUpdatesRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit LiquidFlow", null, (_, _) => QuitRequested?.Invoke());
        menu.Opening += (_, _) => RefreshMicMenu();

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "LiquidFlow",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();

        UpdateStatus(false);
    }

    /// <summary>Show/hide the tray "Install update" item (called when the update state changes).</summary>
    public void SetUpdateAvailable(string? version)
    {
        void Apply()
        {
            if (string.IsNullOrEmpty(version))
            {
                _updateItem.Visible = false;
            }
            else
            {
                _updateItem.Text = $"⬆  Install update ({version})";
                _updateItem.Visible = true;
            }
        }
        if (_icon.ContextMenuStrip is { } strip && strip.InvokeRequired) strip.Invoke(Apply);
        else Apply();
    }

    public void UpdateStatus(bool recording)
    {
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "not set";
        _statusItem.Text = recording ? $"Recording... ({hotkey})" : $"Ready to Record ({hotkey})";
        _icon.Icon = recording ? _recordingIcon : _idleIcon;
        _icon.Text = recording ? "LiquidFlow — Recording" : "LiquidFlow";
    }

    public void ShowBalloon(string title, string body)
    {
        try { _icon.ShowBalloonTip(4000, title, body, ToolTipIcon.Info); }
        catch { }
    }

    private void RefreshMicMenu()
    {
        _micMenu.DropDownItems.Clear();
        var devices = AudioRecorder.ListInputDevices();
        var selected = Settings.Current.PreferredInputDeviceId;

        var systemDefault = new ToolStripMenuItem("System Default")
        {
            Checked = string.IsNullOrEmpty(selected),
        };
        systemDefault.Click += (_, _) =>
        {
            Settings.Current.PreferredInputDeviceId = null;
            Settings.Current.Save("PreferredInputDeviceId");
        };
        _micMenu.DropDownItems.Add(systemDefault);
        _micMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var (id, name, isDefault) in devices)
        {
            var label = isDefault ? $"{name} (System Default)" : name;
            var item = new ToolStripMenuItem(label) { Checked = id == selected };
            item.Click += (_, _) =>
            {
                Settings.Current.PreferredInputDeviceId = id;
                Settings.Current.Save("PreferredInputDeviceId");
            };
            _micMenu.DropDownItems.Add(item);
        }
        if (devices.Count == 0)
            _micMenu.DropDownItems.Add(new ToolStripMenuItem("No microphones found") { Enabled = false });
    }

    /// <summary>App icon in the tray; a red dot badge overlays it while recording.</summary>
    private static Icon CreateTrayIcon(bool recording)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            Icon? app = null;
            try { app = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); }
            catch { }
            if (app is not null)
            {
                using var sized = new Icon(app, 32, 32);
                g.DrawIcon(sized, new Rectangle(0, 0, 32, 32));
                app.Dispose();
            }
            else
            {
                // dev fallback (dotnet run host exe has no icon): teal disc + waveform dot
                using var bg = new SolidBrush(Color.FromArgb(255, 34, 148, 138));
                g.FillEllipse(bg, 2, 2, 28, 28);
            }

            if (recording)
            {
                // bottom-right recording badge with a dark ring so it reads on any taskbar
                using var ring = new SolidBrush(Color.FromArgb(230, 20, 20, 22));
                using var dot = new SolidBrush(Color.FromArgb(255, 236, 84, 84));
                g.FillEllipse(ring, 17, 17, 15, 15);
                g.FillEllipse(dot, 19, 19, 11, 11);
            }
        }
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
