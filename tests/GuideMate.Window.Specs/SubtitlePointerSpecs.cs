using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GuideMate.App;
using Point = System.Windows.Point;

internal static class SubtitlePointerSpecs
{
    public static void Run(MainWindow main, Window overlay, string output)
    {
        var original = (overlay.Left, overlay.Top, overlay.Width);
        var timer = Program.Field<DispatcherTimer>(overlay, "_pointerTimer");
        var placement = overlay.GetType().GetEvent("PlacementChanged")!;
        var saves = 0;
        Action saved = () => saves++;
        placement.AddEventHandler(overlay, saved);
        Window? receiver = null;
        CursorPosition(out var previousCursor);
        var injectedDown = false;
        var downFlag = SystemMetric(23) == 0 ? 2u : 8u;
        var upFlag = downFlag << 1;
        void Sample(bool down, Point point, long at, nint foreground = 123) =>
            Program.Invoke(overlay, "SamplePointerGesture", down, point, at, foreground);
        void Restore()
        {
            overlay.Left = original.Left; overlay.Top = original.Top; overlay.Width = original.Width;
            overlay.UpdateLayout();
        }
        void Button(bool down)
        {
            var result = SendInput(1, [new() { Mouse = new() { Flags = down ? downFlag : upFlag } }], Marshal.SizeOf<MouseEvent>());
            if (result != 1) throw new InvalidOperationException("Native test mouse input failed.");
            injectedDown = down;
        }
        bool Through() => (ReadExtendedStyle(new WindowInteropHelper(overlay).Handle, -20).ToInt64() & 0x08000020) == 0x08000020;
        try
        {
            Program.Check(Through() && timer.IsEnabled, "subtitle defaults to nonactivating click-through with tracking while visible");
            Program.Invoke(main, "SetClickThrough", true); Program.Invoke(main, "SetClickThrough", false);
            Program.Check(Through(), "main click-through toggles do not disable subtitle click-through");
            timer.Stop();
            var point = overlay.PointToScreen(new(80, 20));
            Sample(false, point, 900);
            var initial = Program.Bounds(overlay);
            Sample(true, point, 1000); Sample(false, point + new Vector(2, 1), 1300);
            Program.Check(Program.Bounds(overlay) == initial && saves == 0, "short presses neither move nor save subtitle placement");
            Sample(true, point, 2000); Sample(true, point + new Vector(30, 20), 2100);
            Sample(true, point + new Vector(70, 30), 2600); Sample(false, point, 2700);
            Program.Check(Program.Bounds(overlay) == initial && saves == 0, "early fast swipes cannot become delayed subtitle drags");
            Sample(true, point, 3000); Sample(true, point, 3450);
            Program.Check(!Program.Field<bool>(overlay, "_pointerDragging") && Program.Bounds(overlay) == initial,
                "holding without sliding does not move the subtitle");
            Sample(true, point + new Vector(40, 15), 3460);
            var moved = Program.Bounds(overlay);
            Program.Check(moved.Left == initial.Left + 40 && moved.Top == initial.Top + 15 && moved.Width == initial.Width,
                "long press slides both native axes without changing subtitle width");
            Sample(false, point + new Vector(50, 20), 3500);
            Program.Check(Program.Bounds(overlay).Left == initial.Left + 50 && saves == 1 && Through(),
                "release applies the final position, saves once and keeps click-through");
            Restore();
            var grip = Program.Field<Thumb>(overlay, "_widthGrip");
            point = grip.PointToScreen(new(grip.ActualWidth / 2, grip.ActualHeight / 2));
            var widthBefore = Program.Bounds(overlay);
            Sample(true, point, 4000); Sample(true, point + new Vector(60, 20), 4450); Sample(false, point + new Vector(60, 20), 4460);
            overlay.UpdateLayout();
            var resized = Program.Bounds(overlay);
            Program.Check(Math.Abs(resized.Width - widthBefore.Width - 60) <= 2 && resized.Left == widthBefore.Left
                && resized.Top == widthBefore.Top && overlay.SizeToContent == SizeToContent.Height,
                "holding the corner resizes width in physical pixels while retaining content-driven height");
            point = grip.PointToScreen(new(grip.ActualWidth / 2, grip.ActualHeight / 2));
            Sample(true, point, 5000); Sample(true, point + new Vector(-2000, 0), 5450); Sample(false, point + new Vector(-2000, 0), 5460);
            Program.Check(overlay.Width == overlay.MinWidth, "held corner resize preserves minimum width");
            Restore(); point = overlay.PointToScreen(new(80, 20)); initial = Program.Bounds(overlay);
            Sample(true, point, 6000); Sample(true, point + new Vector(60, 20), 6450, 456); Sample(false, point, 6500);
            Program.Check(Program.Bounds(overlay) == initial && !Program.Field<bool>(overlay, "_pointerHeld"),
                "foreground change cancels the pending hold without moving the subtitle");
            Sample(true, point, 7000); overlay.Hide();
            Program.Check(!timer.IsEnabled && !Program.Field<bool>(overlay, "_pointerHeld"), "hiding stops pointer polling and cancels a hold");
            overlay.Show(); Program.Pump(40);
            Program.Check(timer.IsEnabled && Through(), "showing restores independent click-through and pointer tracking");

            // Native desktop input targets only this receiver window; it is not physical-mouse or game evidence.
            var downs = 0; var ups = 0;
            receiver = new Window { Left = original.Left - 20, Top = original.Top - 20,
                Width = original.Width + 160, Height = overlay.ActualHeight + 160, Topmost = true,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, Background = System.Windows.Media.Brushes.DimGray,
                Content = new TextBlock { Text = "隔离点击接收窗口", Margin = new(20, 70, 20, 20) } };
            receiver.PreviewMouseDown += (_, _) => { downs++; System.Windows.Input.Mouse.Capture(receiver); };
            receiver.PreviewMouseUp += (_, _) => { ups++; System.Windows.Input.Mouse.Capture(null); };
            receiver.Show(); Foreground(new WindowInteropHelper(receiver).Handle);
            MoveWindow(new WindowInteropHelper(overlay).Handle, -1, 0, 0, 0, 0, 0x0013); Program.Pump(60);
            point = overlay.PointToScreen(new(80, 20)); CursorMove((int)point.X, (int)point.Y);
            initial = Program.Bounds(overlay);
            Button(true); Program.Pump(120); Button(false); Program.Pump(90);
            Program.Check(downs == 1 && ups == 1 && Program.Bounds(overlay) == initial,
                "a native short click reaches the underlying desktop window as a balanced down/up pair");
            Button(true); Program.Pump(550);
            Program.Check(Program.Bounds(overlay) == initial, "native hold alone leaves placement unchanged");
            CursorMove((int)point.X + 65, (int)point.Y + 28); Program.Pump(100);
            Button(false); Program.Pump(90);
            moved = Program.Bounds(overlay);
            Program.Check(Math.Abs(moved.Left - initial.Left - 65) <= 2 && Math.Abs(moved.Top - initial.Top - 28) <= 2
                && Through() && CurrentForeground() != new WindowInteropHelper(overlay).Handle,
                "native hold and slide moves the subtitle without activating or capturing it");
            Program.Capture(overlay, Path.Combine(output, "subtitle-held-drag.png"));
        }
        finally
        {
            if (injectedDown) Button(false);
            System.Windows.Input.Mouse.Capture(null); receiver?.Close(); timer.Stop();
            Sample(false, new(), 10000); Restore();
            placement.RemoveEventHandler(overlay, saved);
            Program.Field<Action?>(overlay, "PlacementChanged")?.Invoke();
            Program.Invoke(overlay, "UpdatePointerTracking");
            CursorMove(previousCursor.X, previousCursor.Y);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseEvent { public uint Type; public MouseData Mouse; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, MouseEvent[] events, int size);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool CursorPosition(out CursorPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool CursorMove(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint ReadExtendedStyle(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool MoveWindow(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool Foreground(nint handle);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint CurrentForeground();
    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")] private static extern int SystemMetric(int index);
}
