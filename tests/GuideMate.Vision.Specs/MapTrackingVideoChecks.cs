using System.Diagnostics;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class MapTrackingVideoChecks
{
    public static async Task RunAsync(string video, string resources)
    {
        using var locator = new MapLocator(resources);
        var baselineTimes = new List<double>(); var trackingTimes = new List<double>();
        var totalBaseline = 0; var totalTracked = 0; var temporal = 0;
        var totalFrames = 0;
        foreach (var start in new[] { 30, 52, 180, 360 })
        {
            using var tracker = new MinimapTracker();
            var baselineCount = 0; var trackedCount = 0; var maxError = 0d;
            var frames = start == 52 ? 84 : 24;
            totalFrames += frames;
            for (var i = 0; i < frames; i++)
            {
                using var frame = Cv2.ImDecode(await VideoAnalysis.PreviewAsync(VideoAnalysis.FindFfmpeg()!, video, start + i * .1), ImreadModes.Color);
                var watch = Stopwatch.StartNew();
                var baseline = MinimapDetector.Locate(frame, locator);
                baselineTimes.Add(watch.Elapsed.TotalMilliseconds);
                Cv2.ImEncode(".jpg", frame, out var jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 92)]);
                using var captured = Cv2.ImDecode(jpeg, ImreadModes.Color);
                watch.Restart();
                var tracked = tracker.Locate(captured, locator, true, false, i * 100);
                trackingTimes.Add(watch.Elapsed.TotalMilliseconds);
                if (tracker.UsedTemporalMatch) temporal++;
                if (baseline != null) baselineCount++;
                if (tracked != null)
                {
                    trackedCount++;
                    var p = tracked.Location.PlayerPixel!.Value;
                    // 362.1 秒起剪辑从放大的地图切回正常 HUD，已人工核对该帧。
                    var expectedPlayer = start == 360 && i >= 21 ? new MapPoint(170, 124) : new MapPoint(288, 270);
                    if (Math.Abs(p.X + tracked.Region.X - expectedPlayer.X) > 25 || Math.Abs(p.Y + tracked.Region.Y - expectedPlayer.Y) > 25)
                        throw new Exception($"{start + i * .1}s 追踪了错误图标 ({p.X + tracked.Region.X}, {p.Y + tracked.Region.Y})");
                    if (baseline != null)
                    {
                        var expected = baseline.Location.ToMinimap(tracked.Location.Position);
                        var anchor = baseline.Location.PlayerPixel!.Value;
                        var error = Math.Sqrt(Math.Pow(expected.X - anchor.X, 2) + Math.Pow(expected.Y - anchor.Y, 2));
                        maxError = Math.Max(maxError, error);
                        if (error > 8) Console.WriteLine($"difference {start + i * .1:F1}s={error:F2}px temporal={tracker.UsedTemporalMatch} reference anchor={anchor} tracked anchor={p}");
                    }
                }
            }
            totalBaseline += baselineCount; totalTracked += trackedCount;
            Console.WriteLine($"clip {start}s: baseline={baselineCount}/{frames} tracked={trackedCount}/{frames} max difference={maxError:F2} minimap pixels");
            if (maxError > 12) throw new Exception("相对跟踪与地形定位偏差过大");
            using var blank = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
            if (tracker.Locate(blank, locator, true, false, frames * 100) != null) throw new Exception("切换无地图画面仍保留位置");
        }
        double Percentile(List<double> data, double p) => data.Order().ElementAt((int)((data.Count - 1) * p));
        Console.WriteLine($"CONTINUOUS VIDEO baseline={totalBaseline}/{totalFrames} tracked={totalTracked}/{totalFrames} temporal={temporal}; baseline median={Percentile(baselineTimes, .5):F1} p95={Percentile(baselineTimes, .95):F1} ms; tracking median={Percentile(trackingTimes, .5):F1} p95={Percentile(trackingTimes, .95):F1} ms. JPEG92 tracking; decode/capture excluded; not live game FPS.");
        if (totalTracked < totalBaseline || temporal < 40 || Percentile(trackingTimes, .5) >= Percentile(baselineTimes, .5))
            throw new Exception("连续视频稳定性或性能未达到优化目标");
    }
}
