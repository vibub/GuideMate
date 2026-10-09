using Button = System.Windows.Controls.Button;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using Point = System.Windows.Point;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;

internal static class Program
{
    private static int _checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is 2 or 4 && args[0] == "--vision-calibration")
        {
            VisionCalibrationSpecs.Run(args[1], args.Length == 4 ? args[2] : null, args.Length == 4 ? args[3] : null);
            return;
        }
        if (args.Length == 2 && args[0] == "--browser-cache")
        {
            BrowserCacheSpecs.Run(args[1]);
            return;
        }
        if (args.Length != 2 || args[0] != "--desktop")
            throw new ArgumentException("Pass --desktop and a new isolated output directory; this check opens temporary desktop windows.");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new test directory.");
        Directory.CreateDirectory(output);
        var profile = Path.Combine(output, "profile");
        var store = new SettingsStore(profile);
        var initial = new AppSettings { Left = 120, Top = 80, Width = 900, Height = 600,
            ImmersiveBounds = new(300, 220, 704, 396), OverlayLeft = 200, OverlayTop = 100, OverlayWidth = 620,
            SubtitleOpacity = 0.65, SubtitleOverlay = true };
        initial.Bookmarks.Add(new() { Url = "https://example.test/saved", Position = 42 });
        store.Save(initial);
        var app = new GuideMate.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        // Load production resources without dispatching its normal-profile startup handler.
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app,
            typeof(GuideMate.App.App).GetMethod("OnStartup", Private, null, [typeof(object), typeof(StartupEventArgs)], null)!);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(19, 124, 102));
        MainWindow? window = null;
        try
        {
            window = Open();
            var overlay = Field<Window>(window, "_overlay");
            Check(Near(overlay.Width, 620) && overlay.SizeToContent == SizeToContent.Height,
                "subtitle restores saved width while retaining automatic height");
            overlay.Left = 240; overlay.Top = 140; overlay.Width = 780;
            Field<Action?>(overlay, "PlacementChanged")?.Invoke();
            Check(store.Load().OverlayWidth == 780 && Near(store.Load().OverlayLeft, 240),
                "subtitle placement event immediately saves width and position");
            Invoke(window, "ToggleImmersive"); Pump(100);
            Check(Near(window.Width, 704) && Near(window.Height, 396) && Near(window.Left, 300),
                "entering immersive restores its separate saved placement");
            window.Left = 310; window.Top = 240; window.Width = 800; window.Height = 450; Pump(60);
            window.SaveSettings();
            Check(store.Load().ImmersiveBounds == new WindowPlacement(310, 240, 800, 450), "immersive actual geometry saves");
            window.WindowState = WindowState.Minimized; Pump(50); window.SaveSettings();
            Check(store.Load().ImmersiveBounds == new WindowPlacement(310, 240, 800, 450),
                "minimized small window saves restore bounds rather than minimized coordinates");
            Invoke(window, "RestoreMainWindow"); Pump(50);
            Invoke(window, "ToggleImmersive"); Pump(80);
            Check(Near(window.Width, 900) && Near(window.Height, 600) && Near(window.Left, 120),
                "exiting immersive restores original normal window bounds");
            Invoke(window, "ToggleImmersive"); Pump(80);
            Check(Near(window.Width, 800) && Near(window.Height, 450) && Near(window.Left, 310),
                "re-entering immersive preserves user resize and position");
            Invoke(window, "HideToTray"); Pump(30); Invoke(window, "RestoreMainWindow"); Pump(60);
            Check(Near(window.Width, 800) && Near(window.Height, 450) && Near(overlay.Width, 780),
                "hide and restore preserve both window placements");
            window.Close(); window = null; Pump(60);
            window = Open();
            Invoke(window, "ToggleImmersive"); Pump(80);
            overlay = Field<Window>(window, "_overlay");
            Check(Near(window.Width, 800) && Near(window.Height, 450) && Near(window.Left, 310),
                "new main window restores saved immersive placement across process-style reload");
            Check(Near(overlay.Width, 780) && Near(overlay.Left, 240) && Near(overlay.Top, 140),
                "new subtitle window restores saved width and position");
            Check(store.Load().Bookmarks[0].Position == 42, "window saving preserves video library");
            var sizeGrip = Descendants(window).OfType<Thumb>().Single(grip =>
                System.Windows.Automation.AutomationProperties.GetName(grip) == "窗口缩放：右下角");
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            var nativeSizeLoop = 0;
            nint Observe(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
            {
                if (message == 0x0231 || message == 0x0112 && (wParam.ToInt64() & 0xFFF0) is 0xF000 or 0xF010) nativeSizeLoop++;
                return 0;
            }
            source.AddHook(Observe);
            var before = Bounds(window);
            Invoke(window, "BeginWindowResize", sizeGrip, ResizeEdges.Right | ResizeEdges.Bottom);
            var pointer = Field<Point>(window, "_resizeOriginMouse");
            Invoke(window, "ResizeWindowAt", new Point(pointer.X + 150, pointer.Y + 90));
            Invoke(window, "CancelWindowResize"); Pump(50);
            var after = Bounds(window);
            Check(after.Width > before.Width && Math.Abs(after.Width - after.Height * 16 / 9) <= 2
                && after.Left == before.Left && after.Top == before.Top,
                "custom corner resize preserves actual video ratio and opposite corner in physical pixels");
            Check(Math.Abs(Field<double?>(window, "_videoAspectRatio")!.Value - 16d / 9) < 0.001,
                "local video state supplies aspect ratio independently of online vision");
            var browser = Field<Microsoft.Web.WebView2.Wpf.WebView2CompositionControl>(window, "_browser");
            Check(Math.Abs(browser.ActualWidth / browser.ActualHeight - 16d / 9) < 0.01,
                "rendered immersive video surface has no extra letterbox from window ratio");
            Pump(600);
            var rendered = browser.PointToScreen(new(browser.ActualWidth, browser.ActualHeight));
            Check(Math.Abs(rendered.X - after.Left - after.Width) <= 2 && Math.Abs(rendered.Y - after.Top - after.Height) <= 2,
                "video composition surface grows to fill actual native window");
            Capture(window, Path.Combine(output, "aspect-resize.png"));
            Check(nativeSizeLoop == 0 && window.ResizeMode == ResizeMode.NoResize,
                "custom resize never enters native sizing/moving loop used by Snap");
            var canceled = Bounds(window); Pump(80);
            Check(Bounds(window) == canceled && Field<Thumb?>(window, "_resizeGrip") == null,
                "canceled resize drops pending moves");
            var names = new Dictionary<string, ResizeEdges> { ["左边"] = ResizeEdges.Left, ["右边"] = ResizeEdges.Right,
                ["上边"] = ResizeEdges.Top, ["下边"] = ResizeEdges.Bottom,
                ["左上角"] = ResizeEdges.Left | ResizeEdges.Top, ["右上角"] = ResizeEdges.Right | ResizeEdges.Top,
                ["左下角"] = ResizeEdges.Left | ResizeEdges.Bottom, ["右下角"] = ResizeEdges.Right | ResizeEdges.Bottom };
            var grips = Descendants(window).OfType<Thumb>().Where(grip =>
                System.Windows.Automation.AutomationProperties.GetName(grip).StartsWith("窗口缩放：")).ToArray();
            Check(grips.Length == 8, "all four edges and corners expose independent resize controls");
            foreach (var immersive in new[] { true, false })
            {
                if (!immersive) { Invoke(window, "ToggleImmersive"); Pump(100); }
                foreach (var pair in names)
                {
                    var grip = grips.Single(item => System.Windows.Automation.AutomationProperties.GetName(item) == "窗口缩放：" + pair.Key);
                    var center = grip.TranslatePoint(new(grip.ActualWidth / 2, grip.ActualHeight / 2), window);
                    Check(window.InputHitTest(center) is DependencyObject hit && IsInside(hit, grip),
                        $"WPF receives input at {pair.Key}, immersive={immersive}");
                    var screen = window.PointToScreen(center);
                    Check(SendMessage(new WindowInteropHelper(window).Handle, 0x0084, 0, Pack(screen)) == 1,
                        $"native hit-test delegates {pair.Key} to custom control, immersive={immersive}");
                    var origin = Bounds(window);
                    Invoke(window, "BeginWindowResize", grip, pair.Value);
                    var originMouse = Field<Point>(window, "_resizeOriginMouse");
                    Invoke(window, "ResizeWindowAt", new Point(originMouse.X + 55, originMouse.Y + 35));
                    Invoke(window, "CancelWindowResize"); Pump(25);
                    var resized = Bounds(window);
                    Check(resized != origin && ((pair.Value & ResizeEdges.Left) == 0 || Math.Abs(resized.Left + resized.Width - origin.Left - origin.Width) <= 2)
                        && ((pair.Value & ResizeEdges.Top) == 0 || Math.Abs(resized.Top + resized.Height - origin.Top - origin.Height) <= 2),
                        $"desktop resize applies {pair.Key} while keeping opposite anchors, immersive={immersive}");
                    Check(!immersive || Math.Abs(resized.Width - resized.Height * 16 / 9) <= 2,
                        $"desktop resize keeps video ratio at {pair.Key}, immersive={immersive}");
                }
            }
            var handle = new WindowInteropHelper(window).Handle;
            var monitor = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            window.Left = monitor.Right / source.CompositionTarget.TransformToDevice.M11 - window.Width / 2;
            window.Top = monitor.Bottom / source.CompositionTarget.TransformToDevice.M22 - window.Height / 2;
            Pump(50);
            var offscreen = Bounds(window);
            Check(offscreen.Left + offscreen.Width > monitor.Right && offscreen.Top + offscreen.Height > monitor.Bottom,
                "normal window lower-right corner can be outside work area");
            var upperLeft = grips.Single(grip => System.Windows.Automation.AutomationProperties.GetName(grip) == "窗口缩放：左上角");
            Invoke(window, "BeginWindowResize", upperLeft, ResizeEdges.Left | ResizeEdges.Top);
            var offscreenMouse = Field<Point>(window, "_resizeOriginMouse");
            Invoke(window, "ResizeWindowAt", new Point(offscreenMouse.X + 60, offscreenMouse.Y + 40));
            Invoke(window, "CancelWindowResize"); Pump(50);
            Check(Bounds(window).Width < offscreen.Width && Bounds(window).Height < offscreen.Height,
                "visible upper-left control resizes window when lower-right is beyond screen and taskbar");
            Invoke(window, "BeginWindowResize", upperLeft, ResizeEdges.Left | ResizeEdges.Top);
            window.WindowState = WindowState.Maximized; Pump(50);
            Check(grips.All(grip => grip.Visibility == Visibility.Collapsed) && Field<Thumb?>(window, "_resizeGrip") == null,
                "maximize cancels resize and hides all grips");
            window.WindowState = WindowState.Normal; Pump(50);
            Check(grips.All(grip => grip.Visibility == Visibility.Visible), "restoring normal state re-enables all grips");
            window.Left = 120; window.Top = 80;
            Invoke(window, "ToggleImmersive"); Pump(80);
            Invoke(window, "BeginWindowResize", upperLeft, ResizeEdges.Left | ResizeEdges.Top);
            Invoke(window, "HideToTray"); Pump(50); Invoke(window, "RestoreMainWindow"); Pump(50);
            Check(Field<Thumb?>(window, "_resizeGrip") == null, "hide and restore never resume canceled resizing");
            var progress = Field<Grid>(window, "_edgeProgress");
            var progressPoint = progress.TranslatePoint(new(progress.ActualWidth / 2, progress.ActualHeight - 2), window);
            Check(window.InputHitTest(progressPoint) is DependencyObject progressHit && IsInside(progressHit, progress),
                "bottom progress line retains input above resize edge");
            Check(nativeSizeLoop == 0, "all custom edges avoid native Snap sizing loop");
            source.RemoveHook(Observe);
            window.Width = 800; window.Height = 450; Pump(50);
            var settingsType = typeof(MainWindow).Assembly.GetType("GuideMate.App.SettingsWindow")!;
            Window Settings() => (Window)Activator.CreateInstance(settingsType,
                window, Field<AppSettings>(window, "_settings"), Field<object>(window, "_keys"),
                (Func<Dictionary<string, string>, int, string?>)((_, _) => null))!;
            var settings = Settings();
            settings.Show(); Pump(100);
            Check(Texts(settings).Any(t => t.Text.Contains("管理员权限") && t.Text.Contains("BetterGI")),
                "rendered hotkey settings include administrator guidance");
            Capture(settings, Path.Combine(output, "hotkey-settings.png"));
            var opacitySlider = Descendants(settings).OfType<Slider>().Single(slider =>
                System.Windows.Automation.AutomationProperties.GetName(slider) == "字幕窗口透明度");
            Check(Near(overlay.Opacity, 0.65) && Math.Abs(opacitySlider.Value - 0.35) < 0.001,
                "subtitle opacity initializes from saved value");
            opacitySlider.Value = 0.55;
            settings.Close();
            Check(Math.Abs(overlay.Opacity - 0.65) < 0.001 && Math.Abs(store.Load().SubtitleOpacity - 0.65) < 0.001,
                "cancel discards unsaved subtitle opacity");
            settings = Settings(); settings.Show(); Pump(80);
            opacitySlider = Descendants(settings).OfType<Slider>().Single(slider =>
                System.Windows.Automation.AutomationProperties.GetName(slider) == "字幕窗口透明度");
            opacitySlider.Value = 0.55;
            var save = Descendants(settings).OfType<Button>().Single(button =>
                System.Windows.Automation.AutomationProperties.GetName(button) == "保存设置");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(80);
            Check(Math.Abs(overlay.Opacity - 0.45) < 0.001 && Math.Abs(store.Load().SubtitleOpacity - 0.45) < 0.001,
                "save immediately applies subtitle opacity and persists it");
            Capture(overlay, Path.Combine(output, "subtitle-opacity.png"));
            overlay.Close(); Field<AppSettings>(window, "_settings").SubtitleOverlay = true; Invoke(window, "UpdateOverlay"); Pump(80);
            Check(Math.Abs(Field<Window>(window, "_overlay").Opacity - 0.45) < 0.001,
                "newly created subtitle window reapplies saved opacity");
        }
        finally { window?.Close(); Pump(100); app.Shutdown(); }
        Console.WriteLine($"{_checks} isolated desktop window checks passed; no physical mouse or game test.");
        MainWindow Open()
        {
            var media = Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm");
            Check(File.Exists(media), "local sample is available without a website or user account");
            var result = new MainWindow(profile, true, media);
            result.Show();
            var deadline = Environment.TickCount64 + 15000;
            while (Field<double>(result, "_duration") <= 0 && Environment.TickCount64 < deadline) Pump(40);
            Check(result.IsLoaded && Field<double>(result, "_duration") > 0, "isolated WPF and WebView2 local sample initialized");
            Check(System.Windows.Application.Current.Windows.OfType<MainWindow>().Count() == 1
                && System.Windows.Application.Current.Windows.OfType<MainWindow>().All(item => Field<bool>(item, "_isolated")),
                "only the intended isolated main window is open");
            Invoke(result, "UpdateOverlay"); Pump(50);
            return result;
        }
    }
    internal static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    internal static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    internal static bool Near(double value, double expected) => Math.Abs(value - expected) < 2;
    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++; Console.WriteLine("PASS " + name);
    }
    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    internal static IEnumerable<TextBlock> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>();
    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node == ancestor) return true;
        return false;
    }
    private static nint Pack(Point point) => (nint)(((long)(ushort)(short)Math.Round(point.Y) << 16) | (ushort)(short)Math.Round(point.X));
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    internal static WindowPlacement Bounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new InvalidOperationException("native bounds unavailable");
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    internal static void Capture(Window window, string path)
    {
        var point = window.PointToScreen(new());
        var corner = window.PointToScreen(new(window.ActualWidth, window.ActualHeight));
        using var bitmap = new System.Drawing.Bitmap((int)(corner.X - point.X), (int)(corner.Y - point.Y));
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
