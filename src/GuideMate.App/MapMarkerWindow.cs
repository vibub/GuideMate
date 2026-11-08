using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shapes;

namespace GuideMate.App;

internal sealed class MapMarkerWindow : Window
{
    private readonly Canvas _canvas = new();
    private nint _handle;
    public MapMarkerWindow()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new InvalidOperationException("地图浮层需要 Windows 10 2004 或更新系统以排除自身截图。");
        Title = "随引地图标记"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        ShowActivated = false; Topmost = true; IsHitTestVisible = false; Content = _canvas;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowLongPtr(_handle, -20, GetWindowLongPtr(_handle, -20) | 0x08000020 | 0x80);
        // 不能排除自身截图时禁用叠加，避免标记污染后续定位。
        if (!SetWindowDisplayAffinity(_handle, 0x11))
        { Close(); throw new InvalidOperationException("系统无法将地图浮层排除在截图之外，请使用 Windows 10 2004 或更新系统。"); }
    }

    public void Update(System.Drawing.Rectangle bounds, MapLocation game, MapPoint target)
    {
        var p = game.ToMinimap(target);
        var dpi = GetDpiForWindow(_handle) / 96d;
        var w = bounds.Width / dpi; var h = bounds.Height / dpi;
        var x = p.X / game.Width * w; var y = p.Y / game.Height * h;
        var anchor = game.PlayerPixel ?? new MapPoint(game.Width / 2d, game.Height / 2d);
        var cx = anchor.X / game.Width * w; var cy = anchor.Y / game.Height * h;
        var dx = x - cx; var dy = y - cy;
        var length = Math.Sqrt(dx * dx + dy * dy); var radius = Math.Min(Math.Min(cx, w - cx), Math.Min(cy, h - cy)) * .8;
        var outside = length > radius;
        if (outside) { x = cx + dx / length * radius; y = cy + dy / length * radius; }
        _canvas.Children.Clear();
        Shape marker = outside
            ? new Polygon { Points = new PointCollection { new(0, -8), new(-6, 6), new(6, 6) },
                RenderTransform = new RotateTransform(Math.Atan2(dy, dx) * 180 / Math.PI + 90), Fill = Brushes.Cyan, Stroke = Brushes.Black, StrokeThickness = 1.5 }
            : new Ellipse { Width = 12, Height = 12, Fill = Brushes.Cyan, Stroke = Brushes.Black, StrokeThickness = 2 };
        Canvas.SetLeft(marker, outside ? x : x - 6); Canvas.SetTop(marker, outside ? y : y - 6); _canvas.Children.Add(marker);
        var label = new TextBlock { Text = outside ? "攻略方向" : "攻略", Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), FontSize = 12 };
        Canvas.SetLeft(label, Math.Clamp(x + 10, 0, Math.Max(0, w - 60))); Canvas.SetTop(label, Math.Clamp(y - 8, 0, Math.Max(0, h - 20)));
        _canvas.Children.Add(label);
        Width = w; Height = h;
        if (!IsVisible) Show();
        SetWindowPos(_handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
