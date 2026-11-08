using System.Diagnostics;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class AutoMapVideoChecks
{
    public static async Task RunAsync(string video, string output)
    {
        Directory.CreateDirectory(output);
        var resources = await BuiltInMapAssets.EnsureAsync(output, null, default);
        using var locator = new MapLocator(resources);
        var detected = 0; var located = 0;
        foreach (var second in new[] { 5, 30, 60, 120, 180, 240, 300, 360, 420, 480, 530 })
        {
            using var frame = Cv2.ImDecode(await VideoAnalysis.PreviewAsync(VideoAnalysis.FindFfmpeg()!, video, second), ImreadModes.Color);
            var timer = Stopwatch.StartNew(); var candidates = MinimapDetector.FindCandidates(frame);
            var noMap = second is 300 or 420 or 530;
            if (noMap && candidates.Count != 0) throw new Exception($"{second}s 无地图画面误检");
            if (!noMap && candidates.Count == 0) throw new Exception($"{second}s 未找到玩家候选");
            if (candidates.Count > 0) detected++;
            MinimapObservation? observation = null;
            foreach (var candidate in candidates)
            {
                using var crop = new Mat(frame, candidate.Region);
                var position = locator.Locate(crop, playerPixel: candidate.Player);
                if (position != null) { observation = new(candidate.Region, position); break; }
            }
            if (observation != null) located++;
            if (observation != null)
            {
                var expected = second == 5 ? new MapPoint(962, 545) : new MapPoint(288, 270);
                var anchor = observation.Location.PlayerPixel!.Value;
                var actual = new MapPoint(observation.Region.X + anchor.X, observation.Region.Y + anchor.Y);
                if (Math.Abs(actual.X - expected.X) > 25 || Math.Abs(actual.Y - expected.Y) > 25)
                    throw new Exception($"{second}s 将图标误认作玩家：{actual}");
                if (second == 5 && MinimapDetector.IsGameHudCandidate(new(observation.Region, anchor, 1), frame.Width, frame.Height))
                    throw new Exception("游戏大地图被误认为左上角 HUD");
            }
            Console.WriteLine($"video {second}s candidates={candidates.Count} located={observation != null} position={observation?.Location.Position} elapsed={timer.ElapsedMilliseconds}ms anchor={observation?.Location.PlayerPixel} region=({observation?.Region.X},{observation?.Region.Y},{observation?.Region.Width},{observation?.Region.Height})");
            if (observation != null) Cv2.Rectangle(frame, observation.Region, Scalar.LimeGreen, 3);
            else foreach (var candidate in candidates.Take(1)) Cv2.Rectangle(frame, candidate.Region, Scalar.Red, 3);
            Cv2.ImWrite(Path.Combine(output, $"auto-{second}.jpg"), frame);
        }
        Console.WriteLine($"REAL VIDEO SAMPLES: detected={detected}/11 located={located}/11; not live game validation");
        if (detected != 8 || located < 7) throw new Exception("参考视频的自动地图定位回归");
    }
}
