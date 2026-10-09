using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GuideMate.App;

internal static class DanmakuLayoutSpecs
{
    public static void Run(string directory)
    {
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new test directory.");
        Directory.CreateDirectory(output);
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var bounds = new System.Drawing.Rectangle(screen.Left + 40, screen.Top + 40,
            Math.Min(1600, screen.Width - 80), Math.Min(900, screen.Height - 80));
        var backdrop = new Window { Left = 40, Top = 40, Width = 1200, Height = 720,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 32, 40)), WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false };
        var window = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("GuideMate.App.DanmakuWindow")!, true)!;
        object? Call(string name, params object[] args) => window.GetType().GetMethod(name)!.Invoke(window, args);
        object Item(int id, double offset = 0, int mode = 1, string text = "轨道复用测试") => new { id = id.ToString(), text, offset, mode };
        void Update(double time, params object[] items) => Call("Update", JsonSerializer.SerializeToElement(items), time, true, 1d);
        object[] Comments() => Program.Field<IEnumerable>(Program.Field<object>(window, "_surface"), "_comments").Cast<object>().ToArray();
        int Row(object comment) => (int)comment.GetType().GetProperty("Row")!.GetValue(comment)!;
        int Rows() => Math.Max(1, (int)((Program.Field<double>(Program.Field<object>(window, "_surface"), "ActualHeight") - 24) / 42));
        try
        {
            backdrop.Show(); Call("Show"); Call("FitToMonitor", bounds); Program.Pump(80);
            // Exercise real glyph uploads and retained GPU animations, including the 120-sprite limit.
            var snapshots = Enumerable.Range(0, 160).Select(t => JsonSerializer.SerializeToElement(
                Enumerable.Range(t * 12, 240).Select(id => Item(id, Math.Clamp((t * 12 + 239 - id) / 120d, 0, 4))).ToArray())).ToArray();
            void Batch(bool measure, List<double> elapsed, ref int peak)
            {
                Call("Clear");
                for (var t = 0; t < snapshots.Length; t++)
                {
                    var started = Stopwatch.GetTimestamp();
                    Call("Update", snapshots[t], 100d + t * 0.1, false, 1d);
                    if (measure) elapsed.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    peak = Math.Max(peak, Comments().Length);
                }
            }
            var timings = new List<double>(); var peak = 0;
            Batch(false, timings, ref peak);
            var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
            Batch(true, timings, ref peak);
            var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds;
            timings.Sort();
            var report = new { Snapshots = timings.Count, PeakComments = peak, CpuMs = cpuMs,
                UpdateP50Ms = timings[timings.Count / 2], UpdateP95Ms = timings[(int)(timings.Count * 0.95)],
                UpdateMaxMs = timings[^1] };
            File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(report));
            Console.WriteLine("PERFORMANCE " + JsonSerializer.Serialize(report));
            Program.Check(peak is > 0 and <= 120, "dense snapshots retain the bounded sprite budget");

            Call("Clear"); Update(10, Item(1, 4), Item(2, 2), Item(3)); Program.Pump(120);
            using (var image = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
            {
                using var graphics = System.Drawing.Graphics.FromImage(image);
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, image.Size);
                image.Save(Path.Combine(output, "sparse-layout.png"));
            }
            Program.Check(Comments().Length == 3 && Comments().All(c => Row(c) == 0),
                "sparse scrolling comments reuse the top lane instead of descending like stairs");
            Call("Clear"); Update(20, Item(1, text: "甲"), Item(2, text: "乙"));
            var original = Comments();
            Program.Check(original.Select(Row).SequenceEqual([0, 1]), "simultaneous arrivals use separate collision-free lanes");
            Update(21, Item(1, text: "甲"), Item(2, text: "乙"), Item(3, text: "丙"));
            Program.Check(Comments().Length == 3 && Row(Comments()[^1]) == 0, "new arrivals reuse the first lane once a safe gap opens");
            Program.Check(ReferenceEquals(original[1], Comments()[1]) && Row(original[1]) == 1,
                "existing comments keep their sprite and row when an upper lane becomes reusable");
            Update(21, Item(1), Item(2), Item(3));
            Program.Check(Comments().Length == 3, "repeated snapshots do not duplicate or rerasterize accepted comments");
            Call("Clear"); Update(30, Item(1, mode: 5), Item(2, mode: 4), Item(3), Item(4, mode: 6));
            Program.Check(Comments().Select(Row).SequenceEqual([0, Rows() - 1, 1, 2]),
                "top, bottom, forward and reverse comments preserve anchoring and do not collide");
            Call("Clear"); Update(40, Item(1, 4, 6), Item(2, 2, 6), Item(3, 0, 6));
            Program.Check(Comments().Length == 3 && Comments().All(c => Row(c) == 0), "reverse scrolling also reuses safe top lanes");
            Call("Clear"); Update(50, Enumerable.Range(0, 240).Select(id => Item(id)).ToArray());
            Program.Check(Comments().Length == Rows() && Comments().Select(Row).Distinct().Count() == Rows(),
                "a simultaneous burst fills only available lanes without overlapping or exceeding the area");
            Call("Configure", 0.1, 0.65, 2d, 2d); Update(60, Item(1), Item(2));
            Program.Check(Comments().Length == 1 && Row(Comments()[0]) == 0,
                "minimum area and maximum font and speed still admit a safely separated first lane");
            Call("Hide");
            Program.Check(Comments().Length == 0, "hiding clears sprites and lane occupancy");
            Console.WriteLine("11 isolated DirectComposition layout checks passed; no browser, physical input or game test.");
        }
        finally { Call("Close"); backdrop.Close(); Program.Pump(50); }
    }
}
