using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

public record ArrowDetection(double Angle, double Score)
{
    public MapPoint? PixelCenter { get; init; }
}

public static class ArrowDetector
{
    public static ArrowDetection? Detect(Mat bgr, bool lowResolution = false, VisionGame game = VisionGame.Genshin)
    {
        if (game == VisionGame.Endfield) return EndfieldArrowDetector.Detect(bgr);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var primary = DetectGeometry(hsv, lowResolution, new(80, 155, 220), new(105, 255, 255));
        var narrow = DetectGeometry(hsv, lowResolution, new(80, 145, 190), new(97, 255, 255));
        var shape = ArrowShapeMatcher.Detect(hsv);
        var candidates = new[] { primary, narrow, shape }.OfType<ArrowDetection>().ToArray();
        if (candidates.Length == 0) return null;
        bool Agrees(ArrowDetection a, ArrowDetection b) => Difference(a.Angle, b.Angle) < 25
            && a.PixelCenter is { } ac && b.PixelCenter is { } bc
            && Math.Sqrt(Math.Pow(ac.X - bc.X, 2) + Math.Pow(ac.Y - bc.Y, 2)) < Math.Min(hsv.Width, hsv.Height) * .12;
        var best = candidates.OrderByDescending(c => candidates.Count(other => Agrees(c, other)))
            .ThenByDescending(c => c.Score).First();
        var agreeing = candidates.Where(c => Agrees(c, best)).ToArray();
        if (candidates.Length > 1 && agreeing.Length < 2) return null;
        var x = agreeing.Sum(c => Math.Sin(c.Angle * Math.PI / 180) * c.Score);
        var y = agreeing.Sum(c => Math.Cos(c.Angle * Math.PI / 180) * c.Score);
        return best with { Angle = VisualDirectionTrack.Normalize(Math.Atan2(x, y) * 180 / Math.PI) };
    }

    private static double Difference(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);

    public static ArrowDetection? DetectNear(Mat frame, MapPoint anchor)
    {
        var side = (int)(Math.Min(frame.Width, frame.Height) * .3);
        var rect = Rect.Intersect(new((int)anchor.X - side / 2, (int)anchor.Y - side / 2, side, side), new(0, 0, frame.Width, frame.Height));
        if (rect.Width < 8 || rect.Height < 8) return null;
        using var crop = new Mat(frame, rect); using var scaled = new Mat(); Cv2.Resize(crop, scaled, new(160, 160));
        var result = Detect(scaled, true);
        if (result?.PixelCenter is not { } center) return null;
        var radians = result.Angle * Math.PI / 180;
        return result with
        {
            PixelCenter = new(rect.X + center.X * rect.Width / 160, rect.Y + center.Y * rect.Height / 160),
            Angle = VisualDirectionTrack.Normalize(Math.Atan2(Math.Sin(radians) * rect.Width, Math.Cos(radians) * rect.Height) * 180 / Math.PI)
        };
    }

    private static ArrowDetection? DetectGeometry(Mat hsv, bool lowResolution, Scalar lower, Scalar upper)
    {
        using var mask = new Mat();
        Cv2.InRange(hsv, lower, upper, mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        ArrowDetection? best = null;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            var fraction = area / (hsv.Width * hsv.Height);
            if (fraction < 0.025 || fraction > 0.55) continue;
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.X <= 1 || bounds.Y <= 1 || bounds.Right >= hsv.Width - 1 || bounds.Bottom >= hsv.Height - 1) continue;
            var moments = Cv2.Moments(contour);
            var cx = moments.M10 / moments.M00; var cy = moments.M01 / moments.M00;
            var distance = Math.Sqrt(Math.Pow(cx - hsv.Width / 2d, 2) + Math.Pow(cy - hsv.Height / 2d, 2)) / Math.Min(hsv.Width, hsv.Height);
            if (distance > 0.25) continue;
            var polygon = Cv2.ApproxPolyDP(contour, Cv2.ArcLength(contour, true) * 0.016, true);
            if (polygon.Length < 4 || polygon.Length > 9) continue;
            var hull = Cv2.ConvexHullIndices(polygon);
            var descents = Enumerable.Range(0, hull.Length).Count(i => hull[i] > hull[(i + 1) % hull.Length]);
            // Noisy, self-intersecting masks violate OpenCV's cyclic index-order requirement.
            if (descents != 1 && descents != hull.Length - 1) continue;
            var defects = Cv2.ConvexityDefects(polygon, hull);
            if (defects.Length == 0) continue;
            var ordered = defects.OrderByDescending(d => d.Item3).ToArray();
            var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            var rear = ordered.Where(d => d.Item3 / 256d / diagonal >= 0.045).Take(3).ToArray();
            if (rear.Length is < 1 or > 2) continue;
            var depthRatio = rear.Sum(d => d.Item3 / 256d / diagonal);
            // Online low-resolution encoding can flatten the tail notch; keep the same score and shape gates.
            if (depthRatio < (lowResolution ? 0.05 : 0.08) || depthRatio > 0.4) continue;
            var notch = new Point((int)rear.Average(d => polygon[d.Item2].X), (int)rear.Average(d => polygon[d.Item2].Y));
            if (rear.Length == 2 && Math.Sqrt(Math.Pow(polygon[rear[0].Item2].X - polygon[rear[1].Item2].X, 2)
                + Math.Pow(polygon[rear[0].Item2].Y - polygon[rear[1].Item2].Y, 2)) > diagonal * 0.5) continue;
            // Genshin's tail can produce two adjacent notches; both point away from the nose.
            var nose = polygon.OrderByDescending(p => Math.Pow(p.X - notch.X, 2) + Math.Pow(p.Y - notch.Y, 2)).First();
            var left = polygon[rear[0].Item0]; var right = polygon[rear[0].Item1];
            // 两翼中点比单个缺口像素更稳定，尾部光晕或缺口粘连时尤其如此。
            var wideRear = Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2)) > diagonal * .45;
            var baseX = wideRear ? (left.X + right.X) / 2d : notch.X;
            var baseY = wideRear ? (left.Y + right.Y) / 2d : notch.Y;
            var angle = VisualDirectionTrack.Normalize(Math.Atan2(nose.X - baseX, baseY - nose.Y) * 180 / Math.PI);
            var score = Math.Clamp(depthRatio / 0.12, 0, 1) * (1 - distance) * Math.Min(1, fraction / 0.08);
            if (score >= 0.45 && (best == null || score > best.Score))
                best = new(angle, score) { PixelCenter = new(bounds.X + bounds.Width / 2d, bounds.Y + bounds.Height / 2d) };
        }
        return best;
    }
}
