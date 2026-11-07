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
        void Sample(bool down, Point point, long at, nint foreground = 0) =>
            Program.Invoke(overlay, "SamplePointerGesture", down, point, at, foreground == 0 ? CurrentForeground() : foreground);
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
            Program.Check(!Through() && !timer.IsEnabled, "subtitle is interactive when shared click-through is off");
            Program.Invoke(main, "SetClickThrough", true);
            Program.Check(Through() && timer.IsEnabled && Program.Field<bool>(main, "_through"), "shared click-through enables both immersive windows");
            Program.Invoke(main, "SetClickThrough", false);
            Program.Check(!Through() && !timer.IsEnabled, "shared click-through disables subtitle tracking and restores interaction");
            Program.Invoke(main, "SetClickThrough", true);
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
            Program.Check(!Through() && overlay.IsMouseCaptured, "confirmed hold temporarily stops click-through and captures input");
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
            var downs = 0; var ups = 0; var movesWhileHeld = 0; var receiverHeld = false;
            receiver = new Window { Left = original.Left - 20, Top = original.Top - 20,
                Width = original.Width + 160, Height = overlay.ActualHeight + 160, Topmost = true,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, Background = System.Windows.Media.Brushes.DimGray,
                Content = new TextBlock { Text = "隔离点击接收窗口", Margin = new(20, 70, 20, 20) } };
            receiver.PreviewMouseDown += (_, _) => { downs++; receiverHeld = true; System.Windows.Input.Mouse.Capture(receiver); };
            receiver.PreviewMouseUp += (_, _) => { ups++; receiverHeld = false; System.Windows.Input.Mouse.Capture(null); };
            receiver.LostMouseCapture += (_, _) => receiverHeld = false;
            receiver.MouseMove += (_, _) => { if (receiverHeld) movesWhileHeld++; };
            receiver.Show(); Foreground(new WindowInteropHelper(receiver).Handle);
            MoveWindow(new WindowInteropHelper(overlay).Handle, -1, 0, 0, 0, 0, 0x0013); Program.Pump(60);
            point = overlay.PointToScreen(new(80, 20)); CursorMove((int)point.X, (int)point.Y);
            initial = Program.Bounds(overlay);
            Program.Check(CurrentForeground() == new WindowInteropHelper(receiver).Handle
                && WindowFromPoint(new() { X = (int)point.X, Y = (int)point.Y }) == new WindowInteropHelper(receiver).Handle,
                "desktop receiver must be foreground and unobscured before native test input");
            Button(true); Program.Pump(60); Button(false); Program.Pump(150);
            Program.Check(downs == 1 && ups == 1 && Program.Bounds(overlay) == initial,
                $"a native short click reaches the underlying desktop window as a balanced down/up pair (downs={downs}, ups={ups}, through={Through()})");
            Button(true); Program.Pump(550);
            Program.Check(Program.Bounds(overlay) == initial, "native hold alone leaves placement unchanged");
            Program.Check(!Through() && overlay.IsMouseCaptured && !receiver.IsMouseCaptured && !receiverHeld,
                "confirmed native hold cancels receiver capture before subtitle dragging");
            var movesBeforeDrag = movesWhileHeld;
            CursorMove((int)point.X + 65, (int)point.Y + 28); Program.Pump(100);
            Button(false); Program.Pump(90);
            moved = Program.Bounds(overlay);
            Program.Check(Math.Abs(moved.Left - initial.Left - 65) <= 2 && Math.Abs(moved.Top - initial.Top - 28) <= 2
                && Through() && CurrentForeground() != new WindowInteropHelper(overlay).Handle,
                $"native hold and slide moves the subtitle then restores click-through without activating it (delta={moved.Left - initial.Left},{moved.Top - initial.Top}; through={Through()}; foreground={CurrentForeground()}; overlay={new WindowInteropHelper(overlay).Handle})");
            Program.Check(movesWhileHeld == movesBeforeDrag && !overlay.IsMouseCaptured && !receiverHeld,
                "subtitle drag sends no held movement to receiver and leaves no captured press");
            Program.Capture(overlay, Path.Combine(output, "subtitle-held-drag.png"));
            receiver.Close(); receiver = null; Restore();
            using var external = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory, "GuideMate.Window.Specs.exe"), "--subtitle-receiver")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden })!;
            nint externalHandle = 0;
            try
            {
                var deadline = Environment.TickCount64 + 8000;
                while (externalHandle == 0 && Environment.TickCount64 < deadline)
                {
                    Program.Pump(50); external.Refresh(); externalHandle = external.MainWindowHandle;
                }
                Program.Check(externalHandle != 0, "separate-process receiver window initialized");
                Foreground(externalHandle);
                MoveWindow(new WindowInteropHelper(overlay).Handle, -1, 0, 0, 0, 0, 0x0013); Program.Pump(100);
                int State(int index) => (int)SendMessage(externalHandle, 0x8001, index, 0);
                point = overlay.PointToScreen(new(80, 20)); CursorMove((int)point.X, (int)point.Y);
                Program.Check(CurrentForeground() == externalHandle
                    && WindowFromPoint(new() { X = (int)point.X, Y = (int)point.Y }) == externalHandle,
                    "external receiver must be foreground and unobscured before native test input");
                Button(true); Program.Pump(600);
                Program.Check(!Through() && overlay.IsMouseCaptured && State(3) == 0,
                    $"hold takes capture from a separate process (through={Through()}, localCapture={overlay.IsMouseCaptured}, externalHeld={State(3)})");
                var externalMoves = State(2);
                CursorMove((int)point.X + 65, (int)point.Y + 28); Program.Pump(100);
                Button(false); Program.Pump(100);
                Program.Check(State(2) == externalMoves && State(3) == 0 && Through() && !overlay.IsMouseCaptured,
                    "separate process receives no held moves during subtitle drag and retains no pressed capture");
            }
            finally
            {
                if (injectedDown) Button(false);
                if (externalHandle != 0) SendMessage(externalHandle, 0x0010, 0, 0);
                external.WaitForExit(5000);
            }
        }
        finally
        {
            Program.Invoke(main, "SetClickThrough", false);
            if (injectedDown) Button(false);
            System.Windows.Input.Mouse.Capture(null); receiver?.Close(); timer.Stop();
            Sample(false, new(), 10000); Restore();
            placement.RemoveEventHandler(overlay, saved);
            Program.Field<Action?>(overlay, "PlacementChanged")?.Invoke();
            Program.Invoke(overlay, "UpdatePointerTracking");
            CursorMove(previousCursor.X, previousCursor.Y);
        }
    }

    public static void RunCaptureChecks()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var type = typeof(MainWindow).Assembly.GetType("GuideMate.App.SubtitleWindow")!;
        var overlay = (Window)Activator.CreateInstance(type)!;
        overlay.Topmost = false; overlay.ShowActivated = false;
        overlay.Left = 200; overlay.Top = 100; overlay.Show(); Program.Pump(50);
        type.GetMethod("SetClickThrough")!.Invoke(overlay, [true]);
        Program.Field<DispatcherTimer>(overlay, "_pointerTimer").Stop();
        using var external = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(AppContext.BaseDirectory, "GuideMate.Window.Specs.exe"), "--subtitle-receiver")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden })!;
        nint handle = 0;
        try
        {
            var deadline = Environment.TickCount64 + 8000;
            while (handle == 0 && Environment.TickCount64 < deadline)
            { Program.Pump(50); external.Refresh(); handle = external.MainWindowHandle; }
            Program.Check(handle != 0, "background receiver initialized without mouse injection or activation");
            var point = overlay.PointToScreen(new(80, 20));
            void Sample(bool down, long at, nint foreground) => Program.Invoke(overlay, "SamplePointerGesture", down, point, at, foreground);
            int State() => (int)SendMessage(handle, 0x8001, 3, 0);
            SendMessage(handle, 0x8002, 0, 0);
            Sample(true, 1000, handle); Sample(true, 1401, handle); Program.Pump(50);
            Program.Check(State() == 0 && overlay.IsMouseCaptured && Program.Field<bool>(overlay, "_pointerCaptured"),
                "confirmed hold cancels separate-process capture and takes temporary subtitle capture");
            Sample(false, 1450, handle);
            Program.Check(!overlay.IsMouseCaptured && !Program.Field<bool>(overlay, "_pointerCaptured"),
                "release restores pass-through without retained capture");
            SendMessage(handle, 0x8002, 0, 0); Sample(true, 2000, handle); Sample(true, 2401, handle);
            Sample(true, 2450, (nint)123);
            Program.Check(!overlay.IsMouseCaptured && !Program.Field<bool>(overlay, "_pointerHeld"),
                "foreground change cancels a confirmed hold and releases capture");
            Sample(false, 2500, handle);
            SendMessage(handle, 0x8002, 0, 0); Sample(true, 3000, handle); Sample(true, 3401, handle);
            overlay.Hide();
            Program.Check(!overlay.IsMouseCaptured && !Program.Field<bool>(overlay, "_pointerHeld"),
                "hiding during a confirmed hold releases capture");
            overlay.Show(); Program.Field<DispatcherTimer>(overlay, "_pointerTimer").Stop();
            Sample(false, 3500, handle);
            SendMessage(handle, 0x8002, 0, 0); Sample(true, 4000, handle); Sample(true, 4401, handle);
            type.GetMethod("SetClickThrough")!.Invoke(overlay, [false]);
            Program.Check(!overlay.IsMouseCaptured && !Program.Field<bool>(overlay, "_pointerHeld")
                && (ReadExtendedStyle(new WindowInteropHelper(overlay).Handle, -20).ToInt64() & 0x20) == 0,
                "changing preference during a hold releases capture and respects disabled click-through");
            type.GetMethod("SetClickThrough")!.Invoke(overlay, [true]);
            Program.Field<DispatcherTimer>(overlay, "_pointerTimer").Stop();
            Sample(false, 4500, handle);
            SendMessage(handle, 0x8002, 0, 0); Sample(true, 5000, handle); Sample(true, 5401, handle);
            System.Windows.Input.Mouse.Capture(null);
            Program.Check(!Program.Field<bool>(overlay, "_pointerHeld")
                && (ReadExtendedStyle(new WindowInteropHelper(overlay).Handle, -20).ToInt64() & 0x20) != 0,
                "lost capture cancels the gesture and restores the saved click-through preference");
            Console.WriteLine("Background cross-process capture checks passed; no native mouse input injected.");
        }
        finally
        {
            overlay.Close();
            if (handle != 0) SendMessage(handle, 0x0010, 0, 0);
            external.WaitForExit(5000); app.Shutdown();
        }
    }

    public static void RunReceiver()
    {
        var app = new System.Windows.Application();
        var window = new Window { Title = "随引跨进程输入验证", Left = 100, Top = 80, Width = 1100, Height = 700,
            Topmost = false, ShowActivated = false, WindowStyle = WindowStyle.None, Background = System.Windows.Media.Brushes.DimGray,
            Content = new TextBlock { Text = "独立进程：验证长按后不再接收拖动", Margin = new(30, 100, 30, 30) } };
        var downs = 0; var ups = 0; var moves = 0; var held = false;
        window.PreviewMouseDown += (_, _) => { downs++; held = true; System.Windows.Input.Mouse.Capture(window); };
        window.PreviewMouseUp += (_, _) => { ups++; held = false; System.Windows.Input.Mouse.Capture(null); };
        window.LostMouseCapture += (_, _) => held = false;
        window.MouseMove += (_, _) => { if (held) moves++; };
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(
            (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x8002)
                {
                    held = true; downs++; System.Windows.Input.Mouse.Capture(window); handled = true; return 1;
                }
                if (message != 0x8001) return 0;
                handled = true;
                return (int)wParam switch { 0 => downs, 1 => ups, 2 => moves, 3 => held ? 1 : 0, _ => -1 };
            });
        app.Run(window);
    }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
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
