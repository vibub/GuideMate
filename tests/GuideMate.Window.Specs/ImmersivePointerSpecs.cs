using CheckBox = System.Windows.Controls.CheckBox;
using Brushes = System.Windows.Media.Brushes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using GuideMate.App;
using GuideMate.Core;
using Point = System.Windows.Point;

internal static class ImmersivePointerSpecs
{
    public static void Run(MainWindow main)
    {
        var original = new Rect(main.Left, main.Top, main.Width, main.Height);
        var overlay = Program.Field<Window>(main, "_overlay");
        var overlayEnabled = Program.Field<AppSettings>(main, "_settings").SubtitleOverlay;
        var toggle = Program.Field<CheckBox>(main, "_overlayToggle");
        CursorPosition(out var oldCursor);
        var down = false;
        Window? receiver = null;
        var leftDown = SystemMetric(23) == 0 ? 2u : 8u;
        void Button(bool pressed)
        {
            if (SendInput(1, [new() { Mouse = new() { Flags = pressed ? leftDown : leftDown << 1 } }], Marshal.SizeOf<MouseEvent>()) != 1)
                throw new InvalidOperationException("Native test input failed");
            down = pressed;
        }
        void Move(Point point) => CursorMove((int)Math.Round(point.X), (int)Math.Round(point.Y));
        void Click(Point point) { Move(point); Button(true); Program.Pump(40); Button(false); Program.Pump(100); }
        try
        {
            toggle.IsChecked = false;
            main.Left = 240; main.Top = 180; main.Width = 640; main.Height = 360;
            receiver = new Window { Left = 100, Top = 100, Width = 1100, Height = 700,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true, Background = Brushes.DarkSlateGray };
            var received = 0;
            receiver.MouseLeftButtonDown += (_, _) => received++;
            receiver.Show(); receiver.Activate();
            Program.Invoke(main, "SetClickThrough", true); Program.Pump(120);
            var layer = Program.Field<Window>(main, "_controlLayer");
            var layerHandle = new WindowInteropHelper(layer).Handle;
            // Raise the isolated windows over the isolated receiver without activating them.
            MoveWindow(new WindowInteropHelper(main).Handle, -1, 0, 0, 0, 0, 0x0013);
            MoveWindow(layerHandle, -1, 0, 0, 0, 0, 0x0013); Program.Pump(80);
            var progress = Program.Field<Grid>(main, "_edgeProgress");
            var video = Program.Field<Grid>(main, "_videoSurface");
            var videoBottom = video.PointToScreen(new(0, video.ActualHeight));
            var mainBounds = Program.Bounds(main);
            Program.Check(Math.Abs(videoBottom.Y - mainBounds.Top - mainBounds.Height) <= 1
                && Math.Abs(mainBounds.Width / mainBounds.Height - 16d / 9) < 0.005,
                "video fills the window and outer window preserves video aspect ratio");
            Program.Check(Program.Field<Border>(main, "_edgeTrack").Height == 2,
                "visible progress line is only two DIP high");
            var layerBounds = Program.Bounds(layer);
            var progressLeft = progress.PointToScreen(new(0, 0));
            var progressRight = progress.PointToScreen(new(progress.ActualWidth, 0));
            Program.Check(Math.Abs(progressLeft.X - layerBounds.Left) <= 1
                && Math.Abs(progressRight.X - layerBounds.Left - layerBounds.Width) <= 1,
                "progress strip spans the full bottom edge without horizontal insets");
            var progressBottom = progress.PointToScreen(new(0, progress.ActualHeight));
            Program.Check(Math.Abs(progressBottom.Y - layerBounds.Top - layerBounds.Height) <= 1,
                "progress strip touches the window bottom edge");
            Click(main.PointToScreen(new(main.ActualWidth / 2, main.ActualHeight / 2)));
            Program.Check(received == 1, "video click passes through both windows to the desktop receiver");
            Click(main.PointToScreen(new(2, main.ActualHeight / 2)));
            Program.Check(received == 2, "side edge has no resize input surface");
            var position = progress.PointToScreen(new(progress.ActualWidth * 0.65, progress.ActualHeight / 2));
            Click(position); Program.Pump(500);
            Program.Check(received == 2 && Math.Abs(Program.Field<double>(main, "_position") - Program.Field<double>(main, "_duration") * 0.65) < 1.5,
                "clicking the bottom strip seeks the actual local video while click-through remains on");
            Move(progress.PointToScreen(new(progress.ActualWidth * 0.3, progress.ActualHeight / 2)));
            Button(true); Program.Pump(50);
            Move(progress.PointToScreen(new(progress.ActualWidth * 0.8, -20))); Program.Pump(80);
            Button(false); Program.Pump(500);
            Program.Check(received == 2 && Math.Abs(Program.Field<double>(main, "_position") - Program.Field<double>(main, "_duration") * 0.8) < 1.5,
                "progress drag keeps mouse capture outside the thin strip and seeks on release");
            var handle = Program.Field<Thumb>(main, "_dragHandle");
            Move(main.PointToScreen(new(45, 35))); Program.Pump(150);
            var before = Program.Bounds(main);
            var gripPoint = handle.PointToScreen(new(handle.ActualWidth / 2, handle.ActualHeight / 2));
            Move(gripPoint); Button(true); Program.Pump(70);
            Move(gripPoint + new Vector(55, 30)); Program.Pump(100); Button(false); Program.Pump(100);
            var moved = Program.Bounds(main);
            Program.Check(Math.Abs(moved.Left - before.Left - 55) <= 2 && Math.Abs(moved.Top - before.Top - 30) <= 2 && received == 2,
                "native drag button input moves the small window without passing to receiver");
            Program.Check(Program.Bounds(layer) == moved, "input layer follows the moved video window");
            var corner = Program.Field<List<Thumb>>(main, "_resizeGrips").Last();
            var cornerPoint = corner.PointToScreen(new(corner.ActualWidth / 2, corner.ActualHeight / 2));
            Move(cornerPoint); Button(true); Program.Pump(70);
            Move(cornerPoint + new Vector(60, 35)); Program.Pump(100); Button(false); Program.Pump(100);
            Program.Check(Program.Bounds(main).Width > moved.Width && received == 2,
                "native corner input resizes the small window while video stays click-through");
            Program.Check(CurrentForeground() == new WindowInteropHelper(receiver).Handle,
                "small-window controls do not activate over the receiver");
            Program.Invoke(main, "HideToTray"); Program.Pump(40);
            Program.Check(!layer.IsVisible, "hiding main window hides its control layer");
            Program.Invoke(main, "RestoreMainWindow"); Program.Pump(60);
            Program.Check(layer.IsVisible, "restoring main window restores its control layer");
            Program.Invoke(main, "SetClickThrough", false); Program.Pump(60);
            Program.Check(Program.Field<Window?>(main, "_controlLayer") == null && progress.Parent == Program.Field<Grid>(main, "_workspace"),
                "disabling click-through returns original controls to the main window");
        }
        finally
        {
            if (down) Button(false);
            Program.Invoke(main, "SetClickThrough", false);
            receiver?.Close();
            main.Left = original.Left; main.Top = original.Top; main.Width = original.Width; main.Height = original.Height;
            toggle.IsChecked = overlayEnabled; main.SaveSettings();
            CursorMove(oldCursor.X, oldCursor.Y); Program.Pump(60);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseEvent { public uint Type; public MouseData Mouse; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, MouseEvent[] events, int size);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool CursorPosition(out CursorPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool CursorMove(int x, int y);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool MoveWindow(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint CurrentForeground();
    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")] private static extern int SystemMetric(int index);
}
