using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using GuideMate.Vision;
using Cv = OpenCvSharp;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

internal static class VisionCalibrationSpecs
{
    private static readonly Type Calibration = typeof(MainWindow).Assembly.GetType("GuideMate.App.VisionCalibrationWindow")!;
    private static readonly Type OnlineFrame = typeof(MainWindow).Assembly.GetType("GuideMate.App.OnlineVideoFrame")!;

    public static void Run(string directory, string? genshinVideo, string? endfieldVideo)
    {
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated test directory.");
        Directory.CreateDirectory(output);
        var store = new SettingsStore(Path.Combine(output, "profile"));
        var region = new ArrowRegion(40 / 640d, 40 / 360d, 160 / 640d, 160 / 360d);
        var saved = new OnlineVisionProfile(640, 360, region, true, 27, VisionGame.Endfield);
        var settings = new AppSettings { OnlineVisionCalibration = saved, CheckUpdatesOnStartup = false };
        settings.Hotkeys["SeekForward"] = "Mouse5";
        settings.Bookmarks.Add(new() { Url = "https://example.test/saved", Position = 42 });
        store.Save(settings);
        var settingsPath = Path.Combine(output, "profile", "settings.json");
        var originalSettings = File.ReadAllBytes(settingsPath);
        var app = new GuideMate.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var startup = typeof(GuideMate.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object), typeof(StartupEventArgs)], null)!;
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var owner = new Window { Title = "隔离视觉校准检查", Width = 320, Height = 180, ShowInTaskbar = false };
        Window? dialog = null;
        try
        {
            owner.Show();
            using var frame = new Cv.Mat(360, 640, Cv.MatType.CV_8UC3, new Cv.Scalar(28, 28, 28));
            foreach (var game in new[] { VisionGame.Genshin, VisionGame.Endfield })
            {
                using var marker = new Cv.Mat(160, 160, Cv.MatType.CV_8UC3, Cv.Scalar.Black);
                if (game == VisionGame.Endfield) Cv.Cv2.Circle(marker, new(80, 80), 46, new Cv.Scalar(30, 230, 240), -1);
                Cv.Cv2.FillPoly(marker, [new Cv.Point[] { new(80, 35), new(110, 110), new(80, 93), new(50, 110) }],
                    game == VisionGame.Genshin ? new Cv.Scalar(255, 220, 20) : Cv.Scalar.White);
                using var target = new Cv.Mat(frame, game == VisionGame.Genshin ? new Cv.Rect(40, 40, 160, 160) : new Cv.Rect(400, 100, 160, 160));
                marker.CopyTo(target);
            }
            var bytes = frame.ToBytes(".png");
            Window Online() => (Window)Activator.CreateInstance(Calibration, owner, "", "在线视频", 0d,
                Activator.CreateInstance(OnlineFrame, bytes, 640, 360, 0d, "synthetic", "fixture", 640), saved, VisionGame.Genshin)!;

            dialog = Online(); dialog.Show(); Until(() => Ready(dialog));
            var gameBox = Program.Field<ComboBox>(dialog, "_game");
            var analyze = Program.Field<Button>(dialog, "_analyze");
            Program.Check(gameBox.SelectedIndex == 0 && Game(dialog) == VisionGame.Genshin && Angle(dialog) != null && analyze.IsEnabled,
                "online calibration starts in automatic mode and identifies the selected Genshin marker");
            Program.Check(Program.Field<ArrowRegion>(dialog, "_region") == region
                && Program.Field<TextBox>(dialog, "_northAngle").Text == "27"
                && Program.Field<CheckBox>(dialog, "_northLocked").IsChecked == true,
                "automatic game change preserves the saved selection and north calibration");
            Drag(dialog, new(120, 120), new(480, 180));
            var endfieldRegion = Program.Field<ArrowRegion>(dialog, "_region");
            Program.Check(Game(dialog) == VisionGame.Endfield && Angle(dialog) != null && analyze.IsEnabled,
                "releasing a moved calibration box automatically identifies Endfield");
            Program.Check(Program.Field<ComboBox>(dialog, "_preset").Items.Contains("月月放大小地图")
                && Program.Field<ComboBox>(dialog, "_preset").SelectedItem as string == "手动选区",
                "automatic game refreshes matching presets while retaining the user's box");
            Capture(dialog, Path.Combine(output, "automatic-endfield.png"));
            gameBox.SelectedIndex = 1;
            Program.Check(Game(dialog) == VisionGame.Genshin && Angle(dialog) == null && analyze.IsEnabled
                && Program.Field<ArrowRegion>(dialog, "_region") == endfieldRegion,
                "manual override selects the requested detector without moving the selection");
            Drag(dialog, new(480, 180), new(485, 180));
            Program.Check(gameBox.SelectedIndex == 1 && Game(dialog) == VisionGame.Genshin && Angle(dialog) == null,
                "moving the selection never replaces a manual game choice");
            gameBox.SelectedIndex = 0;
            Program.Check(Game(dialog) == VisionGame.Endfield && Angle(dialog) != null,
                "returning to automatic mode immediately recognizes the current selection");
            var beforeCancel = Program.Field<ArrowRegion>(dialog, "_region");
            Program.Invoke(dialog, "BeginSelectionDrag", new Point(230, 0));
            Program.Invoke(dialog, "UpdateSelectionDrag", new Point(390, 160));
            Program.Check(!analyze.IsEnabled && Angle(dialog) == null, "dragging clears preview and disables saving a pending selection");
            Program.Invoke(dialog, "EndSelectionDrag", true);
            Program.Check(Program.Field<ArrowRegion>(dialog, "_region") == beforeCancel && Game(dialog) == VisionGame.Endfield,
                "canceling a selection restores its region and automatic result");
            Drag(dialog, new(230, 0), new(390, 160));
            Program.Check(Game(dialog) == null && Angle(dialog) == null && !analyze.IsEnabled,
                "an unrecognized selection clears the previous game and prevents guessed calibration");
            Program.Check(Program.Field<TextBlock>(dialog, "_gameResult").Text.Contains("手动选择"),
                "unrecognized automatic mode explains the manual selection fallback");
            gameBox.SelectedIndex = 2;
            Program.Check(Game(dialog) == VisionGame.Endfield && analyze.IsEnabled && Angle(dialog) == null,
                "manual selection can be saved even when the current frame has no visible marker");
            dialog.Width = 680; dialog.Height = 520; Program.Pump(100);
            var status = Program.Field<TextBlock>(dialog, "_gameResult");
            Program.Check(status.ActualWidth > 0 && status.ActualHeight > 0
                && status.TranslatePoint(new(0, status.ActualHeight), dialog).Y <= analyze.TranslatePoint(new(), dialog).Y
                && analyze.TranslatePoint(new(0, analyze.ActualHeight), dialog).Y < dialog.ActualHeight,
                "minimum-size calibration renders game status and save controls without overlap");
            Capture(dialog, Path.Combine(output, "manual-minimum.png"));
            dialog.Close(); dialog = null;
            Program.Check(File.ReadAllBytes(settingsPath).SequenceEqual(originalSettings), "canceling calibration preserves the entire saved profile");

            foreach (var manual in new[] { false, true })
            {
                dialog = Online();
                var active = dialog;
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    if (manual) Program.Field<ComboBox>(active, "_game").SelectedIndex = 2;
                    Program.Invoke(active, "AnalyzeAsync");
                }), DispatcherPriority.ApplicationIdle);
                Program.Check(dialog.ShowDialog() == true, "confirmed online calibration closes with a saved result");
                var result = (OnlineVisionProfile)Calibration.GetProperty("OnlineResult")!.GetValue(dialog)!;
                Program.Check(result.Game == (manual ? VisionGame.Endfield : VisionGame.Genshin)
                    && result.Region == region && result.NorthAngle == 27 && result.NorthLocked,
                    $"saved calibration contains the actual {(manual ? "manual" : "automatic")} game and original north/region");
                settings.OnlineVisionCalibration = result; store.Save(settings);
                var reloaded = store.Load();
                Program.Check(reloaded.OnlineVisionCalibration == result && reloaded.Hotkeys["SeekForward"] == "Mouse5"
                    && reloaded.Bookmarks[0].Position == 42,
                    "saved game survives reloading without resetting hotkeys or bookmarks");
                dialog = null;
            }
            if (genshinVideo != null && endfieldVideo != null)
            {
                var ffmpeg = VideoAnalysis.FindFfmpeg() ?? throw new InvalidOperationException("FFmpeg is required for local video checks.");
                foreach (var (path, expected) in new[] { (genshinVideo, VisionGame.Genshin), (endfieldVideo, VisionGame.Endfield) })
                {
                    var previous = new OnlineVisionProfile(1920, 1080, new(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d), true, 19, expected);
                    dialog = (Window)Activator.CreateInstance(Calibration, owner, ffmpeg, path, 60d, null, previous, VisionGame.Genshin)!;
                    dialog.Show(); Until(() => Ready(dialog));
                    Program.Check(Game(dialog) == expected && Angle(dialog) != null && Program.Field<Button>(dialog, "_analyze").IsEnabled,
                        $"real local video preview automatically identifies {expected}");
                    Capture(dialog, Path.Combine(output, $"local-{expected}.png"));
                    Program.Field<ComboBox>(dialog, "_game").SelectedIndex = (int)expected + 1;
                    Program.Field<Slider>(dialog, "_time").Value = 300;
                    Wait((Task)Program.Invoke(dialog, "RefreshAsync")!);
                    Program.Check(Program.Field<ComboBox>(dialog, "_game").SelectedIndex == (int)expected + 1
                        && Game(dialog) == expected && Angle(dialog) != null && Program.Field<ArrowRegion>(dialog, "_region") == previous.Region
                        && Program.Field<TextBox>(dialog, "_northAngle").Text == "19",
                        $"real local preview refresh preserves manual game, region and north for {expected}");
                    dialog.Close(); dialog = null;
                }
            }
            Console.WriteLine("RESULT vision calibration desktop checks passed");
        }
        finally { dialog?.Close(); owner.Close(); Program.Pump(100); app.Shutdown(); }
    }

    private static VisionGame? Game(Window dialog) => (VisionGame?)Calibration.GetProperty("Game", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);
    private static double? Angle(Window dialog) => (double?)Calibration.GetProperty("PreviewAngle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);
    private static bool Ready(Window dialog) => (bool)Calibration.GetProperty("PreviewReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
    private static void Drag(Window dialog, Point from, Point to)
    {
        Program.Invoke(dialog, "BeginSelectionDrag", from);
        Program.Invoke(dialog, "UpdateSelectionDrag", to);
        Program.Invoke(dialog, "EndSelectionDrag", false);
    }
    private static void Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Calibration preview did not become ready.");
            Program.Pump(25);
        }
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Capture(Window dialog, string path)
    {
        Program.Pump(100); dialog.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(dialog);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(dialog.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(dialog);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
