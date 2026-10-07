using System.Windows.Automation;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace GuideMate.App;

internal static class Ui
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(35, 39, 42));
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(19, 124, 102));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(105, 114, 119));
    public static TextBlock Text(string text, double size = 13, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 16, 0, 8) };
    public static Button Icon(string glyph, string name, Action action)
    {
        var button = new Button { Content = new TextBlock { Text = glyph, FontFamily = new("Segoe MDL2 Assets"), FontSize = 15 }, ToolTip = name, Width = 34, Height = 32, Padding = new(4) };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
    public static Button Command(string glyph, string name, Action action)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new("Segoe MDL2 Assets"), Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Text(name));
        var button = new Button { Content = panel, ToolTip = name, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
    public static CheckBox Toggle(string name, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = name, IsChecked = value };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }
    public static Slider Slider(double min, double max, double value, Action<double> changed)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = (max - min) / 20 };
        slider.ValueChanged += (_, e) => changed(e.NewValue);
        return slider;
    }
    public static void PlaceVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetWindowRect(handle, out var rect)) return;
        // Keep monitor bounds and window coordinates in physical pixels; mixed-DPI origins are not WPF DIP values.
        var bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var screen = System.Windows.Forms.Screen.FromRectangle(bounds).WorkingArea;
        var left = Math.Clamp(rect.Left, screen.Left, Math.Max(screen.Left, screen.Right - bounds.Width));
        var top = Math.Clamp(rect.Top, screen.Top, Math.Max(screen.Top, screen.Bottom - bounds.Height));
        SetWindowPos(handle, 0, left, top, 0, 0, 0x0015);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint insertAfter, int left, int top, int width, int height, uint flags);
}
