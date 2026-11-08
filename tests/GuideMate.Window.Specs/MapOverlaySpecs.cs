using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using Brushes = System.Windows.Media.Brushes;

internal static class MapOverlaySpecs
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAILED: " + label);
            Console.WriteLine("PASS map window: " + label); checks++;
        }
        var type = typeof(MainWindow).Assembly.GetType("GuideMate.App.MapMarkerWindow")!;
        var background = new Window { Title = "随引隔离地图浮层测试", WindowStyle = WindowStyle.None,
            Left = 100, Top = 100, Width = 400, Height = 300, Background = Brushes.Magenta, Topmost = true };
        Window? marker = null;
        try
        {
            background.Show(); background.Activate(); Pump();
            var foreground = GetForegroundWindow();
            marker = (Window)Activator.CreateInstance(type)!;
            var bounds = new System.Drawing.Rectangle(150, 150, 200, 200);
            type.GetMethod("Update")!.Invoke(marker, [bounds, new MapLocation(1, 0, 0, 0, 200, 200, 1), new MapPoint(100, 100)]);
            Pump();
            var canvas = (System.Windows.Controls.Canvas)marker.Content;
            Check(canvas.Children.Count == 1 && canvas.Children[0] is System.Windows.Shapes.Ellipse, "只显示一个小圆点，无文字标签");
            var dot = canvas.Children[0];
            type.GetMethod("Update")!.Invoke(marker, [bounds, new MapLocation(1, 0, 0, 0, 200, 200, 1), new MapPoint(600, 600)]);
            Check(canvas.Children.Count == 1 && ReferenceEquals(dot, canvas.Children[0]), "越界仍复用圆点，不重建控件或显示箭头");
            var handle = new WindowInteropHelper(marker).Handle;
            Check(GetForegroundWindow() == foreground, "显示标记不抢走前台窗口");
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            Check((style & 0x08000020) == 0x08000020, "设置鼠标穿透与禁止激活样式");
            Check(GetWindowDisplayAffinity(handle, out var affinity) && affinity == 0x11, "启用浮层截图排除");
            GetWindowRect(handle, out var rectangle);
            Check(rectangle.Left == 150 && rectangle.Top == 150 && rectangle.Right - rectangle.Left == 200,
                "使用物理屏幕坐标放置地图浮层");
            using var pixel = new System.Drawing.Bitmap(1, 1);
            using (var graphics = System.Drawing.Graphics.FromImage(pixel))
                graphics.CopyFromScreen(250, 250, 0, 0, new System.Drawing.Size(1, 1));
            var color = pixel.GetPixel(0, 0);
            Check(color.R > 240 && color.G < 20 && color.B > 240, "桌面截图获取底层画面，不包含青色攻略标记");
            marker.Hide(); Pump(); Check(!IsWindowVisible(handle), "停止后原生浮层隐藏");
            Console.WriteLine($"PASS {checks} isolated desktop map overlay checks; not a Genshin or physical-input test");
        }
        finally { marker?.Close(); background.Close(); }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
}
