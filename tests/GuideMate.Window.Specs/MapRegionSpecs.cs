using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using Cv = OpenCvSharp;
using Point = System.Windows.Point;

internal static class MapRegionSpecs
{
    public static void Run()
    {
        var app = new GuideMate.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var startup = typeof(GuideMate.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object), typeof(StartupEventArgs)], null)!;
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        var owner = new Window { Width = 320, Height = 180, ShowInTaskbar = false };
        Window? dialog = null; var checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); Console.WriteLine("PASS map selection: " + name); checks++; }
        var type = typeof(MainWindow).Assembly.GetType("GuideMate.App.MapRegionWindow")!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string name, params object[] values) => type.GetMethod(name, flags)!.Invoke(dialog, values);
        ArrowRegion Region() => (ArrowRegion)type.GetField("_region", flags)!.GetValue(dialog)!;
        try
        {
            owner.Show();
            using var source = new Cv.Mat(600, 800, Cv.MatType.CV_8UC3, new Cv.Scalar(40, 50, 60));
            var original = new ArrowRegion(.1, .1, .25, 200d / 600);
            dialog = (Window)Activator.CreateInstance(type, owner, source.ToBytes(".png"), "隔离地图选区测试", original)!;
            dialog.Show(); Pump();
            Check(Region() == original, "原选区保持不变");
            Call("BeginSelectionDrag", new Point(180, 160)); Call("UpdateSelectionDrag", new Point(230, 190)); Call("EndSelectionDrag", false);
            Check(Math.Abs(Region().X * 800 - 130) < 1 && Math.Abs(Region().Y * 600 - 90) < 1 && Region().Width == .25, "框内拖动平移且保持大小");
            Call("BeginSelectionDrag", new Point(330, 290)); Call("UpdateSelectionDrag", new Point(380, 340)); Call("EndSelectionDrag", false);
            Check(Math.Abs(Region().Width * 800 - 250) < 1 && Math.Abs(Region().Height * 600 - 250) < 1, "右下角等比例放大");
            var before = Region();
            Call("BeginSelectionDrag", new Point(200, 180)); Call("UpdateSelectionDrag", new Point(240, 200)); Call("EndSelectionDrag", true);
            Check(Region() == before, "撤销拖动还原原选区");
            Call("BeginSelectionDrag", new Point(200, 180)); Call("UpdateSelectionDrag", new Point(2000, 1800)); Call("EndSelectionDrag", false);
            Check(Region().IsValid && Math.Abs((Region().X + Region().Width) - 1) < .001, "移动不超出图像边界");
            Call("BeginSelectionDrag", new Point(10, 10)); Call("UpdateSelectionDrag", new Point(150, 150)); Call("EndSelectionDrag", false);
            Check(Math.Abs(Region().Width * 800 - 140) < 1 && Math.Abs(Region().X * 800 - 10) < 1, "框外拖动重新框选");
            before = Region(); Call("BeginSelectionDrag", new Point(50, 50)); Call("UpdateSelectionDrag", new Point(70, 70));
            dialog.Width += 100; Pump();
            Check(Region() == before, "窗口缩放取消未完成拖动");
            Check(type.GetProperty("Result")!.GetValue(dialog) == null, "未保存时不产生配置结果");
            Console.WriteLine($"PASS {checks} map selection checks; application events, not physical mouse input");
        }
        finally { dialog?.Close(); owner.Close(); app.Shutdown(); }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
