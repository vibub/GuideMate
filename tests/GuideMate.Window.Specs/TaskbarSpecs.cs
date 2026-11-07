using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GuideMate.App;

internal static class TaskbarSpecs
{
    internal static void Run()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "随引任务栏隔离回归", Width = 360, Height = 220, ShowActivated = false };
        var foreground = new Window { Title = "任务栏回归前台参照", Width = 280, Height = 160 };
        try
        {
            window.Show(); foreground.Show(); foreground.Activate(); Program.Pump(100);
            var hwnd = new WindowInteropHelper(window).Handle;
            var front = new WindowInteropHelper(foreground).Handle;
            Program.Check(GetForegroundWindow() == front, "reference window owns foreground before background transitions");
            var source = HwndSource.FromHwnd(hwnd)!;
            var shownStyles = new List<long>();
            nint Observe(nint handle, int message, nint wParam, nint lParam, ref bool handled)
            {
                if (message == 0x0047) // WM_WINDOWPOSCHANGED
                {
                    var position = Marshal.PtrToStructure<WindowPos>(lParam);
                    if ((position.Flags & 0x0040) != 0) shownStyles.Add(GetWindowLongPtr(handle, -20).ToInt64());
                }
                return 0;
            }
            source.AddHook(Observe);
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized, WindowState.Minimized })
            {
                window.WindowState = state; foreground.Activate(); Program.Pump(80);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    SetExcluded(window, true); Program.Pump(50);
                    Program.Check(!window.ShowInTaskbar && (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x40080) == 0x80,
                        $"immersive exclusion sets tool-window style, {state}");
                    shownStyles.Clear();
                    SetExcluded(window, false); Program.Pump(80);
                    Program.Check(window.ShowInTaskbar && shownStyles.Count > 0 && shownStyles.All(style => (style & 0x40080) == 0x40000),
                        $"shell receives show notification with final app-window style, {state}");
                    Program.Check(GetForegroundWindow() == front && window.WindowState == state && IsWindowVisible(hwnd),
                        $"taskbar restoration preserves foreground, visibility and placement, {state}");
                    shownStyles.Clear();
                    SetExcluded(window, false);
                    Program.Check(shownStyles.Count == 0, "unchanged taskbar policy does not hide or show again");
                }
            }
            window.WindowState = WindowState.Normal; window.Hide();
            SetExcluded(window, true);
            SetExcluded(window, false); Program.Pump(50);
            Program.Check(!window.IsVisible && !IsWindowVisible(hwnd), "tray-hidden window stays hidden during policy changes");
            source.RemoveHook(Observe);
        }
        finally { window.Close(); foreground.Close(); app.Shutdown(); }
        Console.WriteLine("Taskbar native notification checks passed; Explorer taskbar visuals and physical hotkeys are not asserted.");
    }
    private static void SetExcluded(Window window, bool hidden) => typeof(MainWindow).Assembly.GetType("GuideMate.App.NativeHotkeys")!.GetMethod("HideFromWindowSwitcher")!.Invoke(null, [window, hidden]);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos { public nint Handle, After; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
}

