using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class GameDetectionChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new Exception("FAILED: " + name);
            Console.WriteLine("PASS " + name); checks++;
        }
        foreach (var game in new[] { VisionGame.Genshin, VisionGame.Endfield })
        {
            using var marker = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
            if (game == VisionGame.Endfield) Cv2.Circle(marker, new(80, 80), 46, new Scalar(30, 230, 240), -1);
            Cv2.FillPoly(marker, [new Point[] { new(80, 35), new(110, 110), new(80, 93), new(50, 110) }],
                game == VisionGame.Genshin ? new Scalar(255, 220, 20) : Scalar.White);
            using var before = marker.Clone();
            foreach (var angle in new[] { 0, 50, 90, 180, 270, 320 })
            {
                using var rotation = Cv2.GetRotationMatrix2D(new Point2f(80, 80), -angle, 1);
                using var rotated = new Mat(); Cv2.WarpAffine(marker, rotated, rotation, marker.Size());
                var detection = VisionGameDetector.Detect(rotated);
                Check(detection?.Game == game && Math.Abs((detection.Arrow.Angle - angle + 540) % 360 - 180) < 6,
                    $"automatic game and heading {game} at {angle} degrees");
            }
            using var tiny = new Mat(); using var online = new Mat();
            Cv2.Resize(marker, tiny, new(47, 47), interpolation: InterpolationFlags.Area);
            Cv2.Resize(tiny, online, marker.Size());
            Check(VisionGameDetector.Detect(online, true)?.Game == game, $"low-resolution automatic game {game}");
            Check(Cv2.Norm(marker, before) == 0, $"automatic game detection preserves source pixels {game}");
        }
        using var negative = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Check(VisionGameDetector.Detect(negative) == null, "automatic game remains unknown on a blank frame");
        Cv2.Circle(negative, new(80, 80), 28, new Scalar(255, 220, 20), -1);
        Check(VisionGameDetector.Detect(negative) == null, "automatic game does not infer Genshin from a cyan landmark");
        negative.SetTo(Scalar.Black);
        Cv2.Circle(negative, new(80, 80), 46, new Scalar(30, 230, 240), -1);
        Cv2.Circle(negative, new(80, 80), 28, Scalar.White, -1);
        Check(VisionGameDetector.Detect(negative) == null, "automatic game does not infer Endfield from a white landmark and halo");
        using var ambiguous = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(ambiguous, new(109, 80), 45, new Scalar(30, 230, 240), -1);
        Cv2.FillPoly(ambiguous, [new Point[] { new(109, 32), new(136, 118), new(109, 96), new(82, 118) }], Scalar.White);
        Cv2.FillPoly(ambiguous, [new Point[] { new(50, 32), new(75, 118), new(50, 96), new(25, 118) }], new Scalar(255, 220, 20));
        Check(ArrowDetector.Detect(ambiguous) != null && ArrowDetector.Detect(ambiguous, game: VisionGame.Endfield) != null,
            "ambiguous fixture contains two independently valid game markers");
        Check(VisionGameDetector.Detect(ambiguous) == null, "automatic game rejects competing matches instead of comparing geometric scores");
        return checks;
    }

    public static async Task<int> CheckVideosAsync(string[] args, string ffmpeg)
    {
        var flag = Array.IndexOf(args, "--game-videos");
        if (flag < 0) return 0;
        var checks = 0;
        foreach (var (path, game) in new[] { (args[flag + 1], VisionGame.Genshin), (args[flag + 2], VisionGame.Endfield) })
        {
            foreach (var time in new[] { 60, 120, 300 })
            {
                var bytes = await VideoAnalysis.PreviewAsync(ffmpeg, path, time);
                using var frame = Cv2.ImDecode(bytes, ImreadModes.Color);
                using var crop = new Mat(frame, VideoAnalysis.PixelRegion(new(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d),
                    new(frame.Width, frame.Height, 1)));
                using var scaled = new Mat(); Cv2.Resize(crop, scaled, new(160, 160));
                if (VisionGameDetector.Detect(scaled)?.Game != game) throw new Exception($"FAILED: actual video automatic game {game} at {time}");
                Console.WriteLine($"PASS actual video automatic game {game} at {time}"); checks++;
                using var tiny = new Mat(); Cv2.Resize(crop, tiny, new(47, 47), interpolation: InterpolationFlags.Area);
                Cv2.Resize(tiny, scaled, new(160, 160));
                if (VisionGameDetector.Detect(scaled, true)?.Game != game) throw new Exception($"FAILED: scaled actual video automatic game {game} at {time}");
                Console.WriteLine($"PASS scaled actual video automatic game {game} at {time}"); checks++;
            }
        }
        return checks;
    }
}
