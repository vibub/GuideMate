using System.Reflection;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class MapFlickerChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            Console.WriteLine("PASS map flicker: " + name); checks++;
        }
        // 四处分散纹理足以确认平移，但置信度上限为 4/6，不应被当作连续失败。
        using var terrain = new Mat(256, 256, MatType.CV_8UC3, new Scalar(100, 100, 100));
        var random = new Random(481);
        foreach (var center in new[] { new Point(64, 64), new Point(192, 64), new Point(64, 192), new Point(192, 192) })
        {
            for (var i = 0; i < 100; i++)
            {
                var value = random.Next(20, 205);
                Cv2.Circle(terrain, new(center.X + random.Next(-12, 13), center.Y + random.Next(-12, 13)),
                    random.Next(1, 4), new Scalar(value, value, value), -1);
            }
        }
        using var locator = new MapLocator(terrain);
        using var hud = terrain.Clone();
        Cv2.FillPoly(hud, [new Point[] { new(128, 116), new(137, 139), new(128, 133), new(119, 139) }], new Scalar(255, 220, 20));
        using var tracker = new MinimapTracker();
        Check(tracker.Locate(hud, locator, false, false, 0) != null, "四块纹理建立绝对参考");
        Check(tracker.Diagnostics.GlobalMatchMs > 0 && tracker.Diagnostics.LocalMatchMs == 0,
            "首次定位诊断仅记录本次全图耗时");
        for (var i = 1; i <= 3; i++)
        {
            var tracked = tracker.Locate(hud, locator, false, false, i * 100);
            Check(tracked != null && tracker.UsedTemporalMatch && tracker.TrackingConfidence < .7 && !tracker.NeedsWiderSearch,
                "四块有效低置信度地形连续成功不扩区 " + i);
        }
        Check(tracker.Diagnostics.TemporalMatchMs > 0 && tracker.Diagnostics.GlobalMatchMs == 0
            && tracker.Diagnostics.ReferenceAgeMs == 300, "每帧诊断清零且保留实际参考年龄");

        // 让邻域不可用，在旧逻辑 globalDue=true 的 2500 ms 时持有共享全图锁。
        // 新逻辑必须直接返回本帧有效平移；超时后先释放锁再等待任务，避免失败用例遗留后台任务。
        var localField = typeof(MinimapTracker).GetField("_local", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((MapLocator?)localField.GetValue(tracker))?.Dispose(); localField.SetValue(tracker, null);
        var gate = typeof(MapLocator).GetField("_matchGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(locator)!;
        Task<MinimapObservation?> task;
        bool completed;
        Monitor.Enter(gate);
        try
        {
            task = Task.Run(() => tracker.Locate(hud, locator, false, false, 2500));
            completed = task.Wait(TimeSpan.FromSeconds(1));
        }
        finally { Monitor.Exit(gate); }
        var timely = task.GetAwaiter().GetResult();
        Check(completed && timely != null && tracker.UsedTemporalMatch && tracker.Diagnostics.GlobalMatchMs == 0,
            "邻域不可用且全图锁繁忙时有效当前帧仍立即返回");
        var expired = tracker.Locate(hud, locator, false, false, 2501);
        Check(expired != null && !tracker.UsedTemporalMatch && tracker.Diagnostics.GlobalMatchMs > 0,
            "绝对参考超过2.5秒必须重新定位而非沿用旧参考");

        using var blank = new Mat(256, 256, MatType.CV_8UC3, Scalar.Black);
        for (var i = 1; i <= 3; i++)
        {
            Check(tracker.Locate(blank, locator, false, false, 2501 + i * 100) == null
                && tracker.NeedsWiderSearch == (i == 3), "连续实际漏识别到第三帧才请求扩区 " + i);
        }
        Check(tracker.Locate(hud, locator, false, false, 2901) != null && !tracker.NeedsWiderSearch,
            "真实定位恢复后清空连续失败计数");
        tracker.Reset();
        Check(!tracker.NeedsWiderSearch && tracker.Diagnostics.ReferenceAgeMs == null
            && tracker.Diagnostics.TemporalMatchMs == 0 && tracker.Diagnostics.GlobalMatchMs == 0,
            "重置不遗留旧帧诊断和扩区请求");

        using var autoFrame = new Mat(720, 1280, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(autoFrame, new(140, 140), 100, Scalar.White, 2);
        Cv2.FillPoly(autoFrame, [new Point[] { new(140, 126), new(151, 154), new(140, 147), new(129, 154) }], new Scalar(255, 220, 20));
        Check(MinimapDetector.Locate(autoFrame, locator, out var autoTiming, true) == null
            && autoTiming.MatchMs > 0 && autoTiming.LockWaitMs >= 0,
            "整帧候选失败也汇总逐调用全图诊断");
        return checks;
    }
}
