using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluidVoice.Ui;

/// <summary>
/// Edit a past transcription: fix or delete words. Optionally teaches the change to the custom
/// dictionary (via CorrectionLearner.LearnFromManualEdit) so the same fix applies automatically
/// to future dictations.
/// </summary>
public sealed class EditTranscriptDialog : Window
{
    private readonly TextBox _box;
    private readonly CheckBox _learn;

    public string ResultText { get; private set; }
    public bool AddToDictionary => _learn.IsChecked == true;

    public EditTranscriptDialog(string text)
    {
        ResultText = text;
        Title = "Edit transcription";
        Width = 560;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Theme.Bg);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = "Fix or delete words. The change is saved to your history.",
            Foreground = Theme.SubtleBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });

        _box = new TextBox
        {
            Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 170, MaxHeight = 220, Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Theme.CardInner), Foreground = Theme.TextBrush, BorderBrush = Theme.HairlineBrush,
            FontSize = 14,
        };
        root.Children.Add(_box);

        _learn = Theme.Toggle("Fix these words everywhere (add to dictionary)", true, _ => { });
        _learn.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_learn);
        root.Children.Add(new TextBlock
        {
            Text = "Changed words become dictionary rules; removed words become delete rules — applied to future dictations.",
            Foreground = Theme.SubtleBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Theme.SecondaryButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = Theme.PrimaryButton("Save");
        save.Click += (_, _) => { ResultText = _box.Text.Trim(); DialogResult = true; Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(save);
        root.Children.Add(btns);

        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
    }
}
