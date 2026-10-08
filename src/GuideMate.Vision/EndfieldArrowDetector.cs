using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

// Endfield uses a white concave marker with a yellow halo. White minimap
// landmarks must pass both the arrow geometry and local halo checks.
internal static class EndfieldArrowDetector
{
    public static ArrowDetection? Detect(Mat bgr)
    {
        using var hsv = new Mat();
        using var white = new Mat();
        using var yellow = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 0, 200), new Scalar(180, 65, 255), white);
        Cv2.InRange(hsv, new Scalar(18, 65, 140), new Scalar(40, 255, 255), yellow);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(white, white, MorphTypes.Close, kernel);
        Cv2.FindContours(white, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        ArrowDetection? best = null;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            var fraction = area / (bgr.Width * bgr.Height);
            if (fraction < 0.025 || fraction > 0.35) continue;
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.X <= 1 || bounds.Y <= 1 || bounds.Right >= bgr.Width - 1 || bounds.Bottom >= bgr.Height - 1) continue;
            var moments = Cv2.Moments(contour);
            var cx = moments.M10 / moments.M00; var cy = moments.M01 / moments.M00;
            var distance = Math.Sqrt(Math.Pow(cx - bgr.Width / 2d, 2) + Math.Pow(cy - bgr.Height / 2d, 2)) / Math.Min(bgr.Width, bgr.Height);
            if (distance > 0.25) continue;
            var polygon = Cv2.ApproxPolyDP(contour, Cv2.ArcLength(contour, true) * 0.025, true);
            if (polygon.Length != 4) continue;
            var hull = Cv2.ConvexHullIndices(polygon);
            if (hull.Length != 3) continue;
            var descents = Enumerable.Range(0, hull.Length).Count(i => hull[i] > hull[(i + 1) % hull.Length]);
            if (descents != 1 && descents != hull.Length - 1) continue;
            var defects = Cv2.ConvexityDefects(polygon, hull);
            if (defects.Length != 1) continue;
            var rear = defects[0];
            var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            var depth = rear.Item3 / 256d / diagonal;
            if (depth < 0.045 || depth > 0.30) continue;
            var notch = polygon[rear.Item2];
            var nose = polygon[Enumerable.Range(0, 4).Single(i => i != rear.Item0 && i != rear.Item1 && i != rear.Item2)];
            var dx = nose.X - notch.X; var dy = nose.Y - notch.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < diagonal * 0.4) continue;
            var ax = dx / length; var ay = dy / length;
            if (((notch.X - cx) * ax + (notch.Y - cy) * ay) / diagonal > -0.03
                || Math.Abs((notch.X - cx) * ay - (notch.Y - cy) * ax) / diagonal > 0.15) continue;
            double ShoulderDistance(Point p) => Math.Sqrt(Math.Pow(p.X - nose.X, 2) + Math.Pow(p.Y - nose.Y, 2));
            var a = ShoulderDistance(polygon[rear.Item0]); var b = ShoulderDistance(polygon[rear.Item1]);
            if (Math.Min(a, b) / Math.Max(a, b) < 0.65) continue;
            using var halo = new Mat(yellow, bounds);
            if (Cv2.CountNonZero(halo) / area < 0.08) continue;
            var score = Math.Min(1, depth / 0.12) * (1 - distance) * Math.Min(a, b) / Math.Max(a, b);
            var angle = VisualDirectionTrack.Normalize(Math.Atan2(dx, -dy) * 180 / Math.PI);
            if (score >= 0.45 && (best == null || score > best.Score)) best = new(angle, score);
        }
        return best;
    }
}
