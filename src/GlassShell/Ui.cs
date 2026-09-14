using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Data;
using Microsoft.Win32;

namespace GlassShell;

internal sealed class RoundedImage : Border
{
    readonly Image image;
    public ImageSource? Source { get => image.Source; set => image.Source = value; }
    public RoundedImage(double width, double height, double radius)
    {
        Width = width; Height = height; CornerRadius = new CornerRadius(radius); ClipToBounds = true;
        image = new Image { Width = width, Height = height, Stretch = Stretch.UniformToFill,
            Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius) }; Child = image;
    }
}

internal static class Ui
{
    internal static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(Ui), new PropertyMetadata(false));
    internal static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(255, 100, 108));
    internal static readonly FontFamily Font = new(new Uri("pack://application:,,,/GlassShell;component/"), "./Assets/Fonts/#GlassShell DM Sans");
    internal static Brush White = new SolidColorBrush(Color.FromRgb(248, 249, 253));
    internal static Brush Muted = new SolidColorBrush(Color.FromRgb(176, 186, 201));
    internal static Brush Accent = new SolidColorBrush(Color.FromRgb(152, 211, 255));
    internal static readonly Color WindowsAccentColor = ReadWindowsAccent();
    internal static readonly Brush WindowsAccent = new SolidColorBrush(WindowsAccentColor);
    internal static readonly Brush WindowsAccentSurface = new SolidColorBrush(Color.FromArgb(112, WindowsAccentColor.R, WindowsAccentColor.G, WindowsAccentColor.B));
    internal static TextBlock Text(string text, double size = 13, Brush? color = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        Foreground = color ?? White,
        FontWeight = weight ?? FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    internal static Button Button(string text, Action action, double width = double.NaN, double height = 34)
    {
        var label = Text(text);
        foreach (var property in new[] { TextBlock.FontSizeProperty, TextBlock.FontWeightProperty, TextBlock.ForegroundProperty })
            label.SetBinding(property, new Binding(property.Name) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        var button = new Button { Content = label, FontFamily = Font, Width = width, Height = height, Foreground = White, Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderThickness = new Thickness(0), Padding = new Thickness(12, 3, 12, 3), Cursor = Cursors.Hand, FontSize = 13, Margin = new Thickness(3), Focusable = false };
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "ButtonSurface" }; border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), "ButtonSurface")); template.Triggers.Add(hover);
        var selected = new Trigger { Property = IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(54, 255, 255, 255)), "ButtonSurface")); template.Triggers.Add(selected);
        var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), "ButtonSurface")); template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .35)); template.Triggers.Add(disabled);
        button.Template = template; button.Click += (_, e) => { if (e.Source == button) { e.Handled = true; action(); } }; return button;
    }
    internal static Button Icon(string name, string tip, Action action, double size = 34)
    {
        var button = Button("", action, size, size); button.Content = TablerIcon.Create(name, size < 30 ? 16 : size > 45 ? 22 : 18); button.ToolTip = tip; return button;
    }
    internal static ToggleButton Toggle(bool on, Action action)
    {
        var toggle = new ToggleButton { IsChecked = on, Width = 42, Height = 24, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, ToolTip = on ? "Turn off" : "Turn on" };
        var grid = new FrameworkElementFactory(typeof(Grid));
        var track = new FrameworkElementFactory(typeof(Border)) { Name = "Track" }; track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12)); track.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); grid.AppendChild(track);
        var knob = new FrameworkElementFactory(typeof(Ellipse)) { Name = "Knob" }; knob.SetValue(FrameworkElement.WidthProperty, 16.0); knob.SetValue(FrameworkElement.HeightProperty, 16.0); knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); knob.SetValue(FrameworkElement.MarginProperty, new Thickness(4)); knob.SetValue(Shape.FillProperty, White); grid.AppendChild(knob);
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };
        var checkedState = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; checkedState.Setters.Add(new Setter(Border.BackgroundProperty, WindowsAccent, "Track")); checkedState.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob")); template.Triggers.Add(checkedState);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .88)); template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); template.Triggers.Add(disabled);
        toggle.Template = template; toggle.Click += (_, _) => action(); return toggle;
    }
    internal static Button ActionButton(string icon, string label, Action action, double width, double height = 42)
    {
        var button = Button("", action, width, height);
        var text = Text(label); text.Margin = new Thickness(8, 0, 0, 0);
        button.Content = Row(TablerIcon.Create(icon, 17), text); return button;
    }
    internal static StackPanel Row(params UIElement[] elements)
    { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var element in elements) row.Children.Add(element); return row; }
    internal static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Storage.Log("Open failed: " + ex.Message); }
    }
    internal static void OpenProjectNotes()
    {
        var folder = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null)
        {
            var path = System.IO.Path.Combine(folder.FullName, "DESIGN.md");
            if (System.IO.File.Exists(path)) { Open(path); return; }
            folder = folder.Parent;
        }
    }
    static Color ReadWindowsAccent()
    {
        try
        {
            object? raw = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
            if (raw is int signed)
            {
                // DWM stores AccentColor as ABGR, while WPF expects RGB.
                uint value = unchecked((uint)signed); return Color.FromRgb((byte)value, (byte)(value >> 8), (byte)(value >> 16));
            }
        }
        catch (Exception e) { Storage.Log("Read Windows accent: " + e.Message); }
        return SystemParameters.WindowGlassColor;
    }
}



