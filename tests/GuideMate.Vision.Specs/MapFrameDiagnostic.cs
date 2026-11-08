using GuideMate.Vision;
using OpenCvSharp;

internal static class MapFrameDiagnostic
{
    public static async Task RunClipAsync(string video, string resources, double center)
    {
        using var locator = new MapLocator(resources);
        using var tracker = new MinimapTracker();
        var found = 0;
        for (var i = 0; i < 9; i++)
        {
            var time = Math.Max(0, center - 2) + i * .5;
            using var frame = Cv2.ImDecode(await VideoAnalysis.PreviewAsync(VideoAnalysis.FindFfmpeg()!, video, time), ImreadModes.Color);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = tracker.Locate(frame, locator, true, false, i * 500);
            var elapsed = watch.ElapsedMilliseconds;
            if (result != null) found++;
            Console.WriteLine($"clip time={time:F1}s found={result != null} state={tracker.MatchState} recognitionMs={elapsed} position={result?.Location.Position}");
        }
        Console.WriteLine($"clip located={found}/9; no ground-truth position; decoding/capture excluded; not live FPS");
        if (found != 9) throw new Exception("指定短片段仍有定位丢失");
    }

    public static void Run(string imagePath, string resources)
    {
        using var frame = Cv2.ImRead(imagePath);
        var candidates = MinimapDetector.FindCandidates(frame);
        Console.WriteLine($"image={frame.Width}x{frame.Height} candidates={candidates.Count}");
        foreach (var c in candidates.Take(8))
        {
            using var crop = new Mat(frame, c.Region);
            var arrow = ArrowDetector.DetectNear(crop, c.Player);
            Console.WriteLine($"candidate player=({c.Region.X + c.Player.X:F1},{c.Region.Y + c.Player.Y:F1}) region={c.Region} direction={arrow?.Angle:F1} score={arrow?.Score:F2}");
        }
        using var locator = new MapLocator(resources);
        using var tracker = new MinimapTracker();
        var observation = tracker.Locate(frame, locator, true, false, 0);
        Console.WriteLine($"located={observation != null} world={observation?.Location.Position} heading={observation?.Location.PlayerHeading:F1} confidence={observation?.Location.HeadingConfidence:F2}");
        if (observation == null) throw new Exception("当前截图未能定位；这项诊断没有世界坐标或方向真值。");
        using var map = new Mat(frame, observation.Region);
        var player = observation.Location.PlayerPixel!.Value;
        var side = (int)(Math.Min(map.Width, map.Height) * .3);
        using var source = new Mat(map, new Rect((int)player.X - side / 2, (int)player.Y - side / 2, side, side));
        using var arrowImage = new Mat(); Cv2.Resize(source, arrowImage, new(160, 160));
        var original = ArrowDetector.Detect(arrowImage, true) ?? throw new Exception("方向未识别");
        var recognized = 0; var maxError = 0d;
        for (var angle = 0; angle < 360; angle += 30)
        {
            using var rotation = Cv2.GetRotationMatrix2D(new Point2f(80, 80), -angle, 1);
            using var rotated = new Mat(); Cv2.WarpAffine(arrowImage, rotated, rotation, new(160, 160));
            Cv2.ImEncode(".jpg", rotated, out var jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 70)]);
            using var decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
            var detected = ArrowDetector.Detect(decoded, true);
            if (detected == null) continue;
            recognized++;
            maxError = Math.Max(maxError, Math.Abs((detected.Angle - original.Angle - angle + 720 + 180) % 360 - 180));
        }
        Console.WriteLine($"screenshot rotation/JPEG diagnostic: {recognized}/12 recognized; max relative angle difference={maxError:F2} degrees; not live-game ground truth");
        if (recognized < 10 || maxError > 12) throw new Exception("截图旋转压缩后的方向一致性不足");
    }
}
