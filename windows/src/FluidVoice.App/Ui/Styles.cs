using System.Windows;
using System.Windows.Markup;
using FluidVoice.Core;

namespace FluidVoice.Ui;

/// <summary>
/// App-wide implicit control styles (buttons, toggle switches, combo boxes, text fields,
/// progress bars, scrollbars, tooltips) so every surface looks designed rather than
/// default-WPF. Parsed once at startup; re-merged when the theme changes.
/// </summary>
public static class Styles
{
    public static void Apply(System.Windows.Application app)
    {
        var dict = Build();
        app.Resources.MergedDictionaries.Add(dict);
        Settings.Changed += _ => app.Dispatcher.BeginInvoke(() =>
        {
            app.Resources.MergedDictionaries.Remove(dict);
            dict = Build();
            app.Resources.MergedDictionaries.Add(dict);
        });
    }

    private static string Hex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static ResourceDictionary Build()
    {
        bool dark = Theme.IsDark;
        string xaml = Template
            .Replace("{ACCENT}", Hex(Theme.Accent))
            .Replace("{FG}", Hex(Theme.Text))
            .Replace("{FIELD}", Hex(Theme.Field))
            .Replace("{FIELDBORDER}", Hex(Theme.CardBorder))
            .Replace("{POPUP}", Hex(Theme.CardInner))
            .Replace("{HOVER}", Hex(Theme.SidebarSelected))
            .Replace("{TRACK}", dark ? "#41444B" : "#DDD9D0")
            .Replace("{THUMB}", dark ? "#5A5E66" : "#C6C2B8")
            .Replace("{HOVERLAYER}", dark ? "#FFFFFF" : "#000000");
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private const string Template = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ============ Button ============ -->
  <Style TargetType="Button">
    <Setter Property="Background" Value="{FIELD}"/>
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="BorderBrush" Value="{FIELDBORDER}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="14,7"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid RenderTransformOrigin="0.5,0.5">
            <Grid.RenderTransform>
              <ScaleTransform x:Name="PressScale" ScaleX="1" ScaleY="1"/>
            </Grid.RenderTransform>
            <Border x:Name="Bg" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="8"/>
            <Border x:Name="HoverLayer" Background="{HOVERLAYER}" Opacity="0" CornerRadius="8"/>
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="Center" VerticalAlignment="Center"
                              TextElement.Foreground="{TemplateBinding Foreground}"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverLayer" Storyboard.TargetProperty="Opacity"
                                     To="0.07" Duration="0:0:0.10"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverLayer" Storyboard.TargetProperty="Opacity"
                                     To="0" Duration="0:0:0.16"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverLayer" Storyboard.TargetProperty="Opacity"
                                     To="0.14" Duration="0:0:0.05"/>
                    <DoubleAnimation Storyboard.TargetName="PressScale" Storyboard.TargetProperty="ScaleX"
                                     To="0.965" Duration="0:0:0.06"/>
                    <DoubleAnimation Storyboard.TargetName="PressScale" Storyboard.TargetProperty="ScaleY"
                                     To="0.965" Duration="0:0:0.06"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="HoverLayer" Storyboard.TargetProperty="Opacity"
                                     To="0.07" Duration="0:0:0.16"/>
                    <DoubleAnimation Storyboard.TargetName="PressScale" Storyboard.TargetProperty="ScaleX"
                                     To="1" Duration="0:0:0.14">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="PressScale" Storyboard.TargetProperty="ScaleY"
                                     To="1" Duration="0:0:0.14">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ CheckBox → toggle switch ============ -->
  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Border x:Name="Track" Width="44" Height="25" CornerRadius="12.5" VerticalAlignment="Center">
              <Border.Background>
                <SolidColorBrush x:Name="TrackBrush" Color="{TRACK}"/>
              </Border.Background>
              <Grid>
                <Ellipse x:Name="Thumb" Width="19" Height="19"
                         HorizontalAlignment="Left" Margin="3,0,3,0">
                  <Ellipse.Fill>
                    <SolidColorBrush x:Name="ThumbBrush" Color="#FDFDFE"/>
                  </Ellipse.Fill>
                  <Ellipse.Effect>
                    <DropShadowEffect BlurRadius="4" ShadowDepth="1" Opacity="0.30" Color="#000000"/>
                  </Ellipse.Effect>
                  <Ellipse.RenderTransform>
                    <TranslateTransform x:Name="ThumbShift" X="0"/>
                  </Ellipse.RenderTransform>
                </Ellipse>
              </Grid>
            </Border>
            <ContentPresenter Margin="11,0,0,0" VerticalAlignment="Center"
                              TextElement.Foreground="{TemplateBinding Foreground}"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X"
                                     To="19" Duration="0:0:0.16">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <ColorAnimation Storyboard.TargetName="TrackBrush" Storyboard.TargetProperty="Color"
                                    To="{ACCENT}" Duration="0:0:0.16"/>
                    <ColorAnimation Storyboard.TargetName="ThumbBrush" Storyboard.TargetProperty="Color"
                                    To="#FFFFFF" Duration="0:0:0.16"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X"
                                     To="0" Duration="0:0:0.16">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <ColorAnimation Storyboard.TargetName="TrackBrush" Storyboard.TargetProperty="Color"
                                    To="{TRACK}" Duration="0:0:0.16"/>
                    <ColorAnimation Storyboard.TargetName="ThumbBrush" Storyboard.TargetProperty="Color"
                                    To="#E8E9EB" Duration="0:0:0.16"/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Track" Property="Opacity" Value="0.9"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ComboBox ============ -->
  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="Padding" Value="10,6"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="Bg" Background="Transparent" CornerRadius="6" Margin="2,1">
            <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bg" Property="Background" Value="{HOVER}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Bg" Property="Background" Value="{HOVER}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="Background" Value="{FIELD}"/>
    <Setter Property="BorderBrush" Value="{FIELDBORDER}"/>
    <Setter Property="Padding" Value="12,7"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <Border x:Name="Bg" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="8"/>
            <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                          Background="Transparent" BorderThickness="0">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border Background="Transparent"/>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              VerticalAlignment="Center" HorizontalAlignment="Left"/>
            <Path Data="M 0 0 L 4 4 L 8 0" Stroke="#9BA0A6" StrokeThickness="1.6"
                  HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,1,12,0"
                  IsHitTestVisible="False"/>
            <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}"
                   AllowsTransparency="True" Placement="Bottom" VerticalOffset="4"
                   PopupAnimation="Fade" StaysOpen="False">
              <Border Background="{POPUP}" BorderBrush="{FIELDBORDER}" BorderThickness="1"
                      CornerRadius="10" Padding="4"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                      MaxHeight="340">
                <Border.Effect>
                  <DropShadowEffect BlurRadius="18" ShadowDepth="4" Opacity="0.45" Color="#000000"/>
                </Border.Effect>
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bg" Property="BorderBrush" Value="{ACCENT}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ TextBox / PasswordBox ============ -->
  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="Background" Value="{FIELD}"/>
    <Setter Property="BorderBrush" Value="{FIELDBORDER}"/>
    <Setter Property="CaretBrush" Value="{FG}"/>
    <Setter Property="Padding" Value="10,7"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SelectionBrush" Value="{ACCENT}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="Bg" Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"
                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Bg" Property="BorderBrush" Value="{ACCENT}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="PasswordBox">
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="Background" Value="{FIELD}"/>
    <Setter Property="BorderBrush" Value="{FIELDBORDER}"/>
    <Setter Property="CaretBrush" Value="{FG}"/>
    <Setter Property="Padding" Value="10,7"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SelectionBrush" Value="{ACCENT}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="PasswordBox">
          <Border x:Name="Bg" Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"
                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Bg" Property="BorderBrush" Value="{ACCENT}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ProgressBar ============ -->
  <Style TargetType="ProgressBar">
    <Setter Property="Height" Value="5"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ProgressBar">
          <Grid>
            <Border x:Name="PART_Track" Background="{TRACK}" CornerRadius="2.5"/>
            <Border x:Name="PART_Indicator" Background="{ACCENT}" CornerRadius="2.5"
                    HorizontalAlignment="Left"/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============ ScrollBar (thin, unobtrusive) ============ -->
  <Style TargetType="ScrollBar">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Width" Value="9"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Border Background="{THUMB}" CornerRadius="4" Margin="2,0" Opacity="0.65"/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/>
        <Setter Property="Height" Value="9"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track">
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Border Background="{THUMB}" CornerRadius="4" Margin="0,2" Opacity="0.65"/>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ============ ToolTip ============ -->
  <Style TargetType="ToolTip">
    <Setter Property="Background" Value="{POPUP}"/>
    <Setter Property="Foreground" Value="{FG}"/>
    <Setter Property="BorderBrush" Value="{FIELDBORDER}"/>
    <Setter Property="Padding" Value="10,6"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>
""";
}
