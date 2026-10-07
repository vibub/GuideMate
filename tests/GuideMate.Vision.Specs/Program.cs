using System.Diagnostics;
using System.Text.Json;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name); checks++;
}
double Error(double actual, double expected) => Math.Abs((actual - expected + 540) % 360 - 180);
using var original = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
Cv2.FillPoly(original, [new Point[] { new(80, 34), new(116, 115), new(80, 95), new(44, 115) }], new Scalar(255, 220, 20));
for (var angle = 0; angle < 360; angle += 10)
{
    using var rotation = Cv2.GetRotationMatrix2D(new Point2f(80, 80), -angle, 1);
    using var rotated = new Mat(); Cv2.WarpAffine(original, rotated, rotation, new(160, 160));
    var detected = ArrowDetector.Detect(rotated);
    Check(detected != null && Error(detected.Angle, angle) < 6, "synthetic rotation " + angle);
}
using var blank = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
Check(ArrowDetector.Detect(blank) == null, "blank frame has no direction");
Check(ArrowDetector.Detect(blank, true) == null, "low-resolution blank frame has no direction");
Cv2.Circle(blank, new(80, 80), 28, new Scalar(255, 220, 20), -1);
Check(ArrowDetector.Detect(blank) == null, "cyan circular map icon is not an arrow");
Check(ArrowDetector.Detect(blank, true) == null, "low-resolution cyan circle is rejected");
blank.SetTo(Scalar.Black);
Cv2.FillPoly(blank, [new Point[] { new(80, 34), new(116, 115), new(44, 115) }], new Scalar(255, 220, 20));
Check(ArrowDetector.Detect(blank) == null, "triangle without rear notch is rejected");
Check(ArrowDetector.Detect(blank, true) == null, "low-resolution triangle without a notch is rejected");
using var clipped = new Mat(original, new Rect(50, 20, 65, 115));
Check(ArrowDetector.Detect(clipped) == null, "clipped arrow is rejected");
Check(ArrowDetector.Detect(clipped, true) == null, "low-resolution clipped arrow is rejected");
using var color = new Mat(160, 160, MatType.CV_8UC3, new Scalar(255, 220, 20));
Check(ArrowDetector.Detect(color) == null, "solid cyan screen is rejected");
Check(ArrowDetector.Detect(color, true) == null, "low-resolution solid cyan screen is rejected");
var onlineFlag = Array.IndexOf(args, "--online-frame");
if (onlineFlag >= 0)
{
    using var source = Cv2.ImRead(args[onlineFlag + 1]);
    using var crop = new Mat(source, VideoAnalysis.PixelRegion(new(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d),
        new(source.Width, source.Height, 1)));
    using var scaled = new Mat(); Cv2.Resize(crop, scaled, new(160, 160));
    var detection = ArrowDetector.Detect(scaled, true);
    Check(detection != null && Error(detection.Angle, 50) < 20, "captured low-resolution live guide northeast arrow");
}
var robustnessFlag = Array.IndexOf(args, "--robustness");
if (robustnessFlag >= 0)
{
    var baselineFlag = Array.IndexOf(args, "--baseline");
    checks += ArrowRobustnessChecks.Run(args[robustnessFlag + 1], args[robustnessFlag + 2],
        baselineFlag >= 0 ? args[baselineFlag + 1] : null);
}
var overlapFlag = Array.IndexOf(args, "--overlap-review");
if (overlapFlag >= 0) checks += ArrowRobustnessChecks.ReviewOverlaps(args[overlapFlag + 1]);
var ffmpeg = VideoAnalysis.FindFfmpeg();
Check(ffmpeg != null, "existing ffmpeg is located without installing an SDK");
var framesFlag = Array.IndexOf(args, "--frames");
if (framesFlag >= 0)
{
    foreach (var path in Directory.GetFiles(args[framesFlag + 1], "genshin-01-*.png").Where(p => !p.EndsWith("-mask.png")).Order())
    {
        using var source = Cv2.ImRead(path);
        var info = new VideoInfo(source.Width, source.Height, 1);
        var region = new ArrowRegion(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d);
        using var roi = new Mat(source, VideoAnalysis.PixelRegion(region, info));
        using var scaled = new Mat(); Cv2.Resize(roi, scaled, new(160, 160));
        var detection = ArrowDetector.Detect(scaled);
        Console.WriteLine(Path.GetFileName(path) + " " + JsonSerializer.Serialize(detection));
        var expected = Path.GetFileName(path) switch { "genshin-01-60.png" => 50d, "genshin-01-300.png" => 320d, "genshin-01-900.png" => 108d, _ => (double?)null };
        if (expected != null) Check(detection != null && Error(detection.Angle, expected.Value) < 15, "development guide frame " + Path.GetFileName(path));
        if (args.Contains("--diagnostics"))
        {
            using var hsv = new Mat(); using var mask = new Mat();
            Cv2.CvtColor(scaled, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(80, 155, 220), new Scalar(105, 255, 255), mask);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours.Where(c => Cv2.ContourArea(c) > 40))
            {
                var polygon = Cv2.ApproxPolyDP(contour, Cv2.ArcLength(contour, true) * 0.016, true);
                Console.WriteLine(JsonSerializer.Serialize(new { area = Cv2.ContourArea(contour), bounds = Cv2.BoundingRect(contour).ToString(),
                    polygon = polygon.Select(p => new[] { p.X, p.Y }), defects = Cv2.ConvexityDefects(polygon, Cv2.ConvexHullIndices(polygon)).Select(d => new[] { d.Item0, d.Item1, d.Item2, d.Item3 }) }));
            }
            Cv2.ImWrite(Path.Combine(args[framesFlag + 1], Path.GetFileNameWithoutExtension(path) + "-mask.png"), mask);
        }
    }
}
var videoFlag = Array.IndexOf(args, "--video");
var holdoutFlag = Array.IndexOf(args, "--holdout");
if (holdoutFlag >= 0)
{
    // Approximate manual nose/rear-center landmarks, independent of detector output.
    foreach (var (time, expected) in new[] { (120, 263d), (480, 270d), (720, 274d), (1080, 333d) })
    {
        var path = Path.Combine(args[holdoutFlag + 1], $"genshin-holdout-{time}.png");
        using var image = Cv2.ImRead(path); using var scaled = new Mat();
        Cv2.Resize(image, scaled, new(160, 160));
        var result = ArrowDetector.Detect(scaled);
        Console.WriteLine($"HOLDOUT {time} " + JsonSerializer.Serialize(result));
        Check(result != null && Error(result.Angle, expected) < 12, "held-out manually annotated guide frame " + time);
    }
}
if (videoFlag >= 0)
{
    var path = args[videoFlag + 1];
    var info = await VideoAnalysis.ProbeAsync(ffmpeg!, path);
    Check(info.Width == 1920 && info.Height == 1080 && info.Duration > 1193 && info.Duration < 1195, "provided AV1 guide metadata");
    var preview = await VideoAnalysis.PreviewAsync(ffmpeg!, path, 60);
    using var decoded = Cv2.ImDecode(preview, ImreadModes.Color);
    Check(decoded.Width == info.Width && decoded.Height == info.Height, "actual guide preview decodes");
    using var cancellation = new CancellationTokenSource();
    var region = new ArrowRegion(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d);
    var partial = VideoAnalysis.AnalyzeAsync(ffmpeg!, path, info, region, true, 0, null, cancellation.Token);
    cancellation.CancelAfter(200);
    var cancelled = false;
    try { await partial; } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancelled decoding does not produce a partial successful track");
    var dataFlag = Array.IndexOf(args, "--data-dir");
    if (dataFlag >= 0)
    {
        var timer = Stopwatch.StartNew();
        var track = await VideoAnalysis.AnalyzeAsync(ffmpeg!, path, info, region, true, 0, null, CancellationToken.None);
        timer.Stop();
        Check(track.IsValid && track.Matches(path) && track.Frames.Count > 2380, "complete guide produces a valid identity-bound direction track");
        VisualDirectionCache.Save(args[dataFlag + 1], track);
        var loaded = VisualDirectionCache.Load(args[dataFlag + 1], path);
        Check(loaded != null && loaded.Frames.Count == track.Frames.Count, "guide direction cache roundtrip");
        var report = new { success = true, time = DateTimeOffset.Now, checks, duration = info.Duration, seconds = timer.Elapsed.TotalSeconds, samples = track.Frames.Count,
            recognized = track.Frames.Count(f => f.Angle != null), at60 = track.HintAt(60), at300 = track.HintAt(300), at900 = track.HintAt(900) };
        Console.WriteLine(JsonSerializer.Serialize(report));
        File.WriteAllText(Path.Combine(args[dataFlag + 1], "vision-result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
Console.WriteLine($"{checks} vision checks passed.");
