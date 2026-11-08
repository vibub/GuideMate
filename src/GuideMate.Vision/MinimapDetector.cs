using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

public sealed record MinimapCandidate(Rect Region, MapPoint Player, double Score);
public sealed record MinimapObservation(Rect Region, MapLocation Location);

/// <summary>在整帧寻找玩家箭头；圆形边界提供尺度，地形匹配最终确认候选。</summary>
public static class MinimapDetector
{
    public static IReadOnlyList<MinimapCandidate> FindCandidates(Mat frame)
    {
        if (frame.Empty()) return [];
        var scale = Math.Min(1, 1280d / frame.Width);
        using var small = new Mat(); Cv2.Resize(frame, small, new(), scale, scale);
        using var hsv = new Mat(); using var mask = new Mat();
        Cv2.CvtColor(small, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(80, 145, 200), new Scalar(106, 255, 255), mask);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var players = new List<(Point2d Point, double Side, double Score)>();
        var width = small.Width; var height = small.Height;
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour); var side = Math.Max(bounds.Width, bounds.Height);
            if (side < 7 || side > Math.Min(width, height) * .16 || Cv2.ContourArea(contour) < 15) continue;
            var areaPixels = Cv2.ContourArea(contour); var perimeter = Cv2.ArcLength(contour, true);
            if (areaPixels / (bounds.Width * bounds.Height) > .72 || 4 * Math.PI * areaPixels / (perimeter * perimeter) > .70) continue;
            var cx = bounds.X + bounds.Width / 2d; var cy = bounds.Y + bounds.Height / 2d;
            var extent = (int)Math.Ceiling(side * .85);
            var rect = new Rect((int)cx - extent, (int)cy - extent, extent * 2, extent * 2);
            if (rect.X < 0 || rect.Y < 0 || rect.Right > width || rect.Bottom > height) continue;
            using var crop = new Mat(small, rect); using var arrow = new Mat(); Cv2.Resize(crop, arrow, new(160, 160));
            var detection = ArrowDetector.Detect(arrow, true);
            if (detection != null) players.Add((new(cx, cy), side, detection.Score));
        }
        if (players.Count == 0) return [];
        var candidates = new List<MinimapCandidate>();
        foreach (var player in players.OrderByDescending(p => p.Side * p.Score).Take(6))
        {
            // 在箭头附近搜索圆环，避免全屏霍夫变换；允许攻略将圆环裁切到画面边缘。
            var extent = (int)Math.Min(Math.Min(width, height) * .48, player.Side * 8);
            var area = Rect.Intersect(new Rect((int)player.Point.X - extent, (int)player.Point.Y - extent, extent * 2, extent * 2), new(0, 0, width, height));
            using var local = new Mat(small, area); using var gray = new Mat();
            Cv2.CvtColor(local, gray, ColorConversionCodes.BGR2GRAY); Cv2.GaussianBlur(gray, gray, new(5, 5), 1.2);
            var circles = Cv2.HoughCircles(gray, HoughModes.Gradient, 1.5, 30, 100, 45,
                Math.Max(28, (int)(player.Side * 2.2)), Math.Max(30, (int)(player.Side * 7)));
            var radii = circles.Where(c => Math.Sqrt(Math.Pow(c.Center.X + area.X - player.Point.X, 2)
                + Math.Pow(c.Center.Y + area.Y - player.Point.Y, 2)) < c.Radius * .18)
                .Select(c => (double)c.Radius).Take(2).Concat(new[] { player.Side * 4, player.Side * 5.5 });
            foreach (var radius in radii)
            {
                var centerX = player.Point.X / scale; var centerY = player.Point.Y / scale; var r = radius / scale;
                var rect = Rect.Intersect(new Rect((int)(centerX - r), (int)(centerY - r), (int)(r * 2), (int)(r * 2)), new(0, 0, frame.Width, frame.Height));
                if (rect.Width < 96 || rect.Height < 96 || candidates.Any(c => Math.Abs(c.Region.X - rect.X) < 8 && Math.Abs(c.Region.Y - rect.Y) < 8 && Math.Abs(c.Region.Width - rect.Width) < 12 && Math.Abs(c.Region.Height - rect.Height) < 12)) continue;
                candidates.Add(new(rect, new(centerX - rect.X, centerY - rect.Y), player.Score));
            }
        }
        return candidates;
    }

    public static bool IsGameHudCandidate(MinimapCandidate candidate, int width, int height)
    {
        var x = candidate.Region.X + candidate.Player.X;
        var y = candidate.Region.Y + candidate.Player.Y;
        return x < width * .3 && y < height * .4
            && candidate.Region.Width < width * .4 && candidate.Region.Height < height * .6;
    }

    public static MinimapObservation? Locate(Mat frame, MapLocator locator, bool gameHud = false)
    {
        foreach (var candidate in FindCandidates(frame))
        {
            // 游戏只接受左上角 HUD；攻略允许剪辑放大或大地图中的玩家位置。
            if (gameHud && !IsGameHudCandidate(candidate, frame.Width, frame.Height)) continue;
            using var crop = new Mat(frame, candidate.Region);
            var location = locator.Locate(crop, playerPixel: candidate.Player);
            if (location != null) return new(candidate.Region, location);
        }
        return null;
    }
}
