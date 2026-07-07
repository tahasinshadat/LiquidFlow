using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FluidVoice.Audio;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// System tray icon + menu (MenuBarManager.swift / MenuBarIconGenerator.swift):
/// an "F" glyph, tinted red while recording; menu with status line, Open,
/// Settings, Custom Dictionary, Microphone device submenu, Check for Updates, Quit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _micMenu;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? DictionaryRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _idleIcon = CreateFIcon(Color.White);
        _recordingIcon = CreateFIcon(Color.FromArgb(255, 89, 89));

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Ready to Record") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Fluid Voice", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Settings...", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Custom Dictionary", null, (_, _) => DictionaryRequested?.Invoke());
        _micMenu = new ToolStripMenuItem("Microphone");
        menu.Items.Add(_micMenu);
        menu.Items.Add("Check for Updates...", null, (_, _) => CheckUpdatesRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Fluid Voice", null, (_, _) => QuitRequested?.Invoke());
        menu.Opening += (_, _) => RefreshMicMenu();

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "FluidVoice",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();

        UpdateStatus(false);
    }

    public void UpdateStatus(bool recording)
    {
        var hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "not set";
        _statusItem.Text = recording ? $"Recording... ({hotkey})" : $"Ready to Record ({hotkey})";
        _icon.Icon = recording ? _recordingIcon : _idleIcon;
        _icon.Text = recording ? "FluidVoice — Recording" : "FluidVoice";
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

    /// <summary>Draws the "F" glyph icon at runtime (like MenuBarIconGenerator.swift).</summary>
    private static Icon CreateFIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var font = new Font("Segoe UI", 20, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            var size = g.MeasureString("F", font);
            var x = (32 - size.Width) / 2;
            var y = (32 - size.Height) / 2;
            g.DrawString("F", font, shadow, x + 1, y + 1);
            g.DrawString("F", font, brush, x, y);
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
