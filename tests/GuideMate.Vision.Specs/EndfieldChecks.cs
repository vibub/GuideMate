using System.Text.Json;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class EndfieldChecks
{
    public static int Run(string[] args)
    {
        var checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAILED: " + label);
            Console.WriteLine("PASS " + label); checks++;
        }
        double Error(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);
        using var arrow = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(arrow, new(80, 80), 46, new Scalar(30, 230, 240), -1);
        Cv2.FillPoly(arrow, [new Point[] { new(80, 35), new(110, 110), new(80, 93), new(50, 110) }], Scalar.White);
        for (var angle = 0; angle < 360; angle += 10)
        {
            using var rotation = Cv2.GetRotationMatrix2D(new Point2f(80, 80), -angle, 1);
            using var rotated = new Mat(); Cv2.WarpAffine(arrow, rotated, rotation, arrow.Size());
            var result = ArrowDetector.Detect(rotated, game: VisionGame.Endfield);
            Check(result != null && Error(result.Angle, angle) < 6, "Endfield synthetic rotation " + angle);
        }
        using var negative = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Check(ArrowDetector.Detect(negative, game: VisionGame.Endfield) == null, "Endfield blank frame rejected");
        Cv2.Circle(negative, new(80, 80), 46, new Scalar(30, 230, 240), -1);
        Cv2.Circle(negative, new(80, 80), 28, Scalar.White, -1);
        Check(ArrowDetector.Detect(negative, game: VisionGame.Endfield) == null, "Endfield white circular landmark rejected");
        negative.SetTo(Scalar.Black);
        Cv2.Circle(negative, new(80, 80), 46, new Scalar(30, 230, 240), -1);
        Cv2.FillPoly(negative, [new Point[] { new(80, 35), new(110, 110), new(50, 110) }], Scalar.White);
        Check(ArrowDetector.Detect(negative, game: VisionGame.Endfield) == null, "Endfield white triangle without rear notch rejected");
        negative.SetTo(Scalar.Black);
        Cv2.FillPoly(negative, [new Point[] { new(80, 35), new(110, 110), new(80, 93), new(50, 110) }], Scalar.White);
        Check(ArrowDetector.Detect(negative, game: VisionGame.Endfield) == null, "Endfield white arrow without yellow halo rejected");
        using var clipped = new Mat(arrow, new Rect(55, 20, 60, 100));
        Check(ArrowDetector.Detect(clipped, game: VisionGame.Endfield) == null, "Endfield clipped marker rejected");
        Check(ArrowDetector.Detect(arrow) == null, "Endfield marker does not alter Genshin recognition");
        var flag = Array.IndexOf(args, "--endfield-frames");
        if (flag < 0) return checks;
        foreach (var time in new[] { 5, 30, 60, 120, 180, 240, 300, 360, 400 })
        {
            using var source = Cv2.ImRead(Path.Combine(args[flag + 1], $"endfield-{time}.png"));
            using var crop = new Mat(source, VideoAnalysis.PixelRegion(new(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d), new(source.Width, source.Height, 1)));
            using var scaled = new Mat(); Cv2.Resize(crop, scaled, new(160, 160));
            var result = ArrowDetector.Detect(scaled, game: VisionGame.Endfield);
            Console.WriteLine($"ENDFIELD {time} " + JsonSerializer.Serialize(result));
            if (time == 5) Check(result == null, "Endfield actual map screen has no player direction");
            else Check(result != null, "Endfield actual marker detected at " + time);
            // Held-out approximate tip/notch landmarks, not detector-generated labels.
            double? expected = time switch { 120 => 270, 240 => 270, 300 => 0, 360 => 250, 400 => 95, _ => null };
            if (expected != null) Check(result != null && Error(result.Angle, expected.Value) < 18, "Endfield held-out heading " + time);
            using var tiny = new Mat(); using var online = new Mat();
            Cv2.Resize(crop, tiny, new(47, 47), interpolation: InterpolationFlags.Area);
            Cv2.Resize(tiny, online, new(160, 160));
            var small = ArrowDetector.Detect(online, true, VisionGame.Endfield);
            if (expected != null) Check(small != null && Error(small.Angle, expected.Value) < 20, "Endfield online-size heading " + time);
        }
        return checks;
    }
    public static async Task<int> AnalyzeVideoAsync(string[] args, string ffmpeg)
    {
        var flag = Array.IndexOf(args, "--endfield-video");
        if (flag < 0) return 0;
        var checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAILED: " + label);
            Console.WriteLine("PASS " + label); checks++;
        }
        var path = args[flag + 1];
        var info = await VideoAnalysis.ProbeAsync(ffmpeg, path);
        Check(info.Width == 1920 && info.Height == 1080 && info.Duration is > 416 and < 418, "provided Endfield HEVC guide metadata");
        var preview = await VideoAnalysis.PreviewAsync(ffmpeg, path, 60);
        using var image = Cv2.ImDecode(preview, ImreadModes.Color);
        Check(image.Width == info.Width && image.Height == info.Height, "Endfield guide preview decodes");
        var region = new ArrowRegion(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var track = await VideoAnalysis.AnalyzeAsync(ffmpeg, path, info, region, true, 0, null, CancellationToken.None, VisionGame.Endfield);
        timer.Stop();
        Check(track.IsValid && track.Matches(path) && track.Frames.Count is >= 832 and <= 835 && track.Game == VisionGame.Endfield,
            "complete Endfield guide produces a valid game-bound direction track");
        Check(track.HintAt(5) == null && track.HintAt(60)?.Label.StartsWith("西 ") == true && track.HintAt(300)?.Label.StartsWith("北 ") == true,
            "Endfield complete track contains no stale map direction and correct west/north samples");
        var dataFlag = Array.IndexOf(args, "--data-dir");
        if (dataFlag >= 0)
        {
            var directory = args[dataFlag + 1];
            VisualDirectionCache.Save(directory, track);
            var loaded = VisualDirectionCache.Load(directory, path);
            Check(loaded?.Game == VisionGame.Endfield && loaded.Frames.SequenceEqual(track.Frames) && loaded.Region == region,
                "Endfield direction cache roundtrip retains game, samples and selected region");
            var report = new { success = true, checks, duration = info.Duration, seconds = timer.Elapsed.TotalSeconds,
                samples = track.Frames.Count, recognized = track.Frames.Count(f => f.Angle != null), at5 = track.HintAt(5), at60 = track.HintAt(60), at300 = track.HintAt(300) };
            File.WriteAllText(Path.Combine(directory, "endfield-result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report));
        }
        return checks;
    }

}
