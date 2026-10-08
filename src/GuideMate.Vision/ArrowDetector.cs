using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

public record ArrowDetection(double Angle, double Score);

public static class ArrowDetector
{
    public static ArrowDetection? Detect(Mat bgr, bool lowResolution = false, VisionGame game = VisionGame.Genshin)
    {
        if (game == VisionGame.Endfield) return EndfieldArrowDetector.Detect(bgr);
        using var hsv = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(80, 155, 220), new Scalar(105, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        ArrowDetection? best = null;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            var fraction = area / (bgr.Width * bgr.Height);
            if (fraction < 0.025 || fraction > 0.55) continue;
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.X <= 1 || bounds.Y <= 1 || bounds.Right >= bgr.Width - 1 || bounds.Bottom >= bgr.Height - 1) continue;
            var moments = Cv2.Moments(contour);
            var cx = moments.M10 / moments.M00; var cy = moments.M01 / moments.M00;
            var distance = Math.Sqrt(Math.Pow(cx - bgr.Width / 2d, 2) + Math.Pow(cy - bgr.Height / 2d, 2)) / Math.Min(bgr.Width, bgr.Height);
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
            var angle = VisualDirectionTrack.Normalize(Math.Atan2(nose.X - notch.X, notch.Y - nose.Y) * 180 / Math.PI);
            var score = Math.Clamp(depthRatio / 0.12, 0, 1) * (1 - distance) * Math.Min(1, fraction / 0.08);
            if (score >= 0.45 && (best == null || score > best.Score)) best = new(angle, score);
        }
        return best ?? ArrowShapeMatcher.Detect(hsv);
    }
}
