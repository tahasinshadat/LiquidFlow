using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluidVoice.App;
using FluidVoice.Audio;
using FluidVoice.Core;
using FluidVoice.Stt;
using FluidVoice.Typing;

namespace FluidVoice.Ui;

/// <summary>
/// Transforms (reference layout): saved rewrite prompts on global Win+Alt+N hotkeys that
/// apply to whatever is selected in any app (via Write Mode). "My Transforms" cards manage
/// them; the old workspace tools (Write / Command / File transcription) live below.
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
        Children.Clear();
        Children.Add(BuildHeader());
        Children.Add(BuildHero());
        Children.Add(BuildMyTransforms());
        Children.Add(BuildOtherTools());
    }

    private UIElement BuildHeader()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 30) };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock
        {
            Text = "Opt in",
            FontSize = 13.5,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var toggle = Theme.Toggle("", Settings.Current.TransformsEnabled, v =>
        {
            Settings.Current.TransformsEnabled = v;
            Settings.Current.Save("transforms");
        });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(toggle);
        var hint = new Border
        {
            Background = new SolidColorBrush(Theme.SidebarSelected),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Child = KeyChips("Win", "Alt", "N", "  applies to your selection"),
        };
        right.Children.Add(hint);
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "Transforms",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Theme.TextBrush,
        });
        var chip = Theme.Pill("Beta", Theme.InkBrush, new SolidColorBrush(Theme.InkText), 11);
        chip.Margin = new Thickness(12, 4, 0, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(chip);
        row.Children.Add(left);
        return row;
    }

    private static UIElement KeyChips(params string[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in parts)
        {
            if (p.StartsWith(" "))
            {
                row.Children.Add(new TextBlock
                {
                    Text = p.Trim(),
                    FontSize = 12.5,
                    Foreground = Theme.SubtleBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                });
                continue;
            }
            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(Theme.CardInner),
                BorderBrush = Theme.HairlineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock { Text = p, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush },
            });
        }
        return row;
    }

    private UIElement BuildHero()
    {
        var dock = new DockPanel();
        var cluster = (FrameworkElement)PageChrome.IconCluster(46);
        cluster.VerticalAlignment = VerticalAlignment.Center;
        cluster.Margin = new Thickness(0, 0, 34, 0);
        DockPanel.SetDock(cluster, Dock.Right);
        dock.Children.Add(cluster);

        var content = new StackPanel { Margin = new Thickness(40, 28, 20, 28), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = "Transform works anywhere you write",
            FontFamily = Theme.DisplaySerif,
            FontSize = 28,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Apply a Transform to rewrite, clean up, or restructure text after you dictate.",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var tryIt = PageChrome.HeroPill("Try it out");
        tryIt.MouseLeftButtonUp += (_, _) => _openRewrite?.Invoke();
        actions.Children.Add(tryIt);
        var how = new TextBlock
        {
            Text = "How it works",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Select text in any app, press the transform's Win+Alt+number hotkey, and the rewrite window applies its prompt to the selection.",
        };
        actions.Children.Add(how);
        content.Children.Add(actions);
        dock.Children.Add(content);

        var hero = PageChrome.DarkHero(dock);
        hero.Margin = new Thickness(0, 0, 0, 26);
        return hero;
    }

    private UIElement BuildMyTransforms()
    {
        var host = new StackPanel { Margin = new Thickness(0, 0, 0, 26) };

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var reset = new TextBlock
        {
            Text = "  Reset to defaults",
            FontSize = 13,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Cursor = Cursors.Hand,
        };
        reset.MouseLeftButtonUp += (_, _) =>
        {
            Settings.Current.Transforms.Clear();
            Settings.Current.TransformsSeeded = false;
            Settings.SeedDefaultTransforms();
            Settings.Current.Save("transforms");
            Build();
        };
        right.Children.Add(reset);
        var create = new Border
        {
            Background = Theme.InkBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "Create New", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.InkText) },
        };
        create.MouseLeftButtonUp += (_, _) => EditTransform(null);
        right.Children.Add(create);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(new TextBlock
        {
            Text = "My Transforms",
            FontFamily = Theme.DisplaySerif,
            FontSize = 26,
            Foreground = Theme.TextBrush,
        });
        host.Children.Add(head);

        var grid = new WrapPanel();
        foreach (var t in Settings.Current.Transforms.OrderBy(t => t.Slot).ToList())
            grid.Children.Add(TransformCard(t));
        grid.Children.Add(CreateYourOwnCard());
        host.Children.Add(grid);
        return host;
    }

    private UIElement TransformCard(TransformDef t)
    {
        var panel = new StackPanel();
        var chips = (FrameworkElement)KeyChips("Win", "Alt", t.Slot.ToString());
        chips.Margin = new Thickness(0, 0, 0, 20);
        panel.Children.Add(chips);
        panel.Children.Add(new TextBlock
        {
            Text = t.Name,
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock
        {
            Text = t.Description,
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        var card = new Border
        {
            Width = 290,
            MinHeight = 170,
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18),
            Margin = new Thickness(0, 0, 16, 16),
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => EditTransform(t);
        card.MouseEnter += (_, _) => card.BorderBrush = Theme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Theme.HairlineBrush;
        return card;
    }

    private UIElement CreateYourOwnCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Theme.SidebarSelected),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 20),
            Child = new TextBlock
            {
                Text = "+", FontSize = 17, Foreground = Theme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Create your own",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Write your own prompt",
            FontSize = 13,
            Foreground = Theme.SubtleBrush,
        });
        var card = new Border
        {
            Width = 290,
            MinHeight = 170,
            Background = new SolidColorBrush(Theme.CardInner),
            BorderBrush = Theme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18),
            Margin = new Thickness(0, 0, 16, 16),
            Cursor = Cursors.Hand,
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => EditTransform(null);
        card.MouseEnter += (_, _) => card.BorderBrush = Theme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Theme.HairlineBrush;
        return card;
    }

    private void EditTransform(TransformDef? existing)
    {
        var dlg = new TransformDialog(existing) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) { Build(); return; }
        if (existing is null) Settings.Current.Transforms.Add(dlg.Result);
        Settings.Current.Save("transforms");
        Build();
    }

    // ---- the old workspace tools, compact, below the reference content ----

    private UIElement BuildOtherTools()
    {
        var host = new StackPanel();
        host.Children.Add(Theme.Divider(6, 22));
        host.Children.Add(new TextBlock
        {
            Text = "Other tools",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(2, 0, 0, 12),
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
        host.Children.Add(grid);
        return host;
    }

    private static UIElement Card(string title, string subtitle, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Foreground = Theme.SubtleBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(body);
        return Theme.Panel(panel, new Thickness(20), new Thickness(0, 0, 0, 18));
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
        open.Margin = new Thickness(0, 10, 0, 0);
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
        open.Margin = new Thickness(0, 10, 0, 0);
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
            MinHeight = 140, MaxHeight = 300, Padding = new Thickness(10),
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

/// <summary>Create/edit one transform (name, description, prompt, Win+Alt slot).</summary>
public sealed class TransformDialog : Window
{
    private readonly TransformDef _def;
    public TransformDef Result => _def;

    public TransformDialog(TransformDef? existing)
    {
        _def = existing ?? new TransformDef { Slot = NextFreeSlot() };
        Title = existing is null ? "New transform" : "Edit transform";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Theme.Bg);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(Theme.Label("Name"));
        var name = new TextBox { Text = _def.Name, Padding = new Thickness(8, 6, 8, 6), FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(name);
        root.Children.Add(Theme.Label("Description"));
        var desc = new TextBox { Text = _def.Description, Padding = new Thickness(8, 6, 8, 6), FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(desc);
        root.Children.Add(Theme.Label("Prompt (applied to your selected text)"));
        var prompt = new TextBox
        {
            Text = _def.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 120, MaxHeight = 150, Padding = new Thickness(8), FontSize = 13.5,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(prompt);
        root.Children.Add(Theme.Label("Hotkey"));
        var slotRow = new StackPanel { Orientation = Orientation.Horizontal };
        slotRow.Children.Add(new TextBlock { Text = "Win + Alt +", FontSize = 13.5, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var slot = new ComboBox { Width = 70 };
        for (int i = 1; i <= 9; i++) slot.Items.Add(i.ToString());
        slot.SelectedIndex = Math.Clamp(_def.Slot, 1, 9) - 1;
        slotRow.Children.Add(slot);
        root.Children.Add(slotRow);

        var btns = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 18, 0, 0) };
        if (existing is not null)
        {
            var del = Theme.SecondaryButton("Delete");
            del.Click += (_, _) =>
            {
                Settings.Current.Transforms.RemoveAll(t => t.Id == _def.Id);
                Settings.Current.Save("transforms");
                DialogResult = false;
                Close();
            };
            btns.Children.Add(del);
        }
        var save = Theme.PrimaryButton("Save");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(prompt.Text)) return;
            _def.Name = name.Text.Trim();
            _def.Description = desc.Text.Trim();
            _def.Prompt = prompt.Text.Trim();
            _def.Slot = slot.SelectedIndex + 1;
            DialogResult = true;
            Close();
        };
        DockPanel.SetDock(save, Dock.Right);
        var cancel = Theme.SecondaryButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        DockPanel.SetDock(cancel, Dock.Right);
        btns.Children.Add(save);
        btns.Children.Add(cancel);
        root.Children.Add(btns);
        Content = root;
    }

    private static int NextFreeSlot()
    {
        var used = Settings.Current.Transforms.Select(t => t.Slot).ToHashSet();
        for (int i = 1; i <= 9; i++)
            if (!used.Contains(i)) return i;
        return 9;
    }
}
