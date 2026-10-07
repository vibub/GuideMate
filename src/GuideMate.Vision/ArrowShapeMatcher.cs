using System.Numerics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace GuideMate.Vision;

// Recover compressed arrow silhouettes after the precise tail-notch detector
// rejects a frame. Templates describe geometry, not any user's calibration.
internal static class ArrowShapeMatcher
{
    private sealed record Template(int Angle, ulong[] Pixels, int Area);
    private static readonly Lazy<Template[]> Templates = new(CreateTemplates);
    private const int CanvasSize = 48, ShapeSize = 40;

    public static ArrowDetection? Detect(Mat hsv)
    {
        using var mask = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        ArrowDetection? best = null;
        foreach (var (value, hue) in new[] { (180, 94), (180, 97), (200, 94), (160, 97) })
        {
            // Keep cyan saturation while admitting dimmed edges. Narrow hue
            // bands avoid merging the marker with the blue map background.
            Cv2.InRange(hsv, new Scalar(80, 130, value), new Scalar(hue, 255, 255), mask);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                var fraction = area / (hsv.Width * hsv.Height);
                if (fraction < 0.025 || fraction > 0.55) continue;
                var bounds = Cv2.BoundingRect(contour);
                if (bounds.X <= 1 || bounds.Y <= 1 || bounds.Right >= hsv.Width - 1 || bounds.Bottom >= hsv.Height - 1) continue;
                var moments = Cv2.Moments(contour);
                var distance = Math.Sqrt(Math.Pow(moments.M10 / moments.M00 - hsv.Width / 2d, 2)
                    + Math.Pow(moments.M01 / moments.M00 - hsv.Height / 2d, 2)) / Math.Min(hsv.Width, hsv.Height);
                if (distance > 0.25) continue;

                var polygon = Cv2.ApproxPolyDP(contour, Cv2.ArcLength(contour, true) * 0.012, true);
                if (polygon.Length is < 4 or > 14) continue;
                var hull = Cv2.ConvexHullIndices(polygon);
                var descents = Enumerable.Range(0, hull.Length).Count(i => hull[i] > hull[(i + 1) % hull.Length]);
                if (descents != 1 && descents != hull.Length - 1) continue;
                var defects = Cv2.ConvexityDefects(polygon, hull);
                var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
                // A real rear indentation is still required; a triangle or
                // round map icon must not become a direction just by fitting.
                if (defects.Length == 0 || defects.Max(d => d.Item3) / 256d / diagonal < 0.015) continue;
                using var shape = new Mat(hsv.Size(), MatType.CV_8UC1, Scalar.Black);
                Cv2.FillPoly(shape, [contour], Scalar.White);
                var result = Match(Normalize(shape, bounds));
                if (result == null) continue;
                var radians = result.Angle * Math.PI / 180;
                var ax = Math.Sin(radians); var ay = -Math.Cos(radians);
                var cx = moments.M10 / moments.M00; var cy = moments.M01 / moments.M00;
                // A wing can resemble the nose when a map icon overlaps the
                // marker. The indentation must sit behind the fitted heading
                // and near its axis, not beside another triangular corner.
                var rearFits = defects.Any(d =>
                {
                    var point = polygon[d.Item2]; var dx = point.X - cx; var dy = point.Y - cy;
                    return d.Item3 / 256d / diagonal >= 0.015 && (dx * ax + dy * ay) / diagonal < -0.06
                        && Math.Abs(dx * ay - dy * ax) / diagonal < 0.18;
                });
                if (rearFits && (best == null || result.Score > best.Score)) best = result;
            }
        }
        return best;
    }

    private static ArrowDetection? Match(ulong[] pixels)
    {
        var area = pixels.Sum(word => BitOperations.PopCount(word));
        var templates = Templates.Value;
        var scores = new double[templates.Length];
        var bestIndex = 0;
        for (var i = 0; i < templates.Length; i++)
        {
            var template = templates[i];
            var intersection = 0;
            for (var word = 0; word < pixels.Length; word++)
                intersection += BitOperations.PopCount(pixels[word] & template.Pixels[word]);
            scores[i] = intersection / (double)(area + template.Area - intersection);
            if (scores[i] > scores[bestIndex]) bestIndex = i;
        }
        var best = templates[bestIndex];
        var competing = 0d;
        for (var i = 0; i < templates.Length; i++)
        {
            var difference = Math.Abs((templates[i].Angle - best.Angle + 540) % 360 - 180);
            if (difference >= 40) competing = Math.Max(competing, scores[i]);
        }
        // Reject ambiguous fits instead of retaining an earlier direction.
        return scores[bestIndex] >= 0.78 && scores[bestIndex] - competing >= 0.02
            ? new(best.Angle, scores[bestIndex]) : null;
    }

    private static ulong[] Normalize(Mat shape, Rect bounds)
    {
        var scale = ShapeSize / (double)Math.Max(bounds.Width, bounds.Height);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        using var source = new Mat(shape, bounds);
        using var normalized = new Mat(CanvasSize, CanvasSize, MatType.CV_8UC1, Scalar.Black);
        using var destination = new Mat(normalized, new Rect((CanvasSize - width) / 2, (CanvasSize - height) / 2, width, height));
        Cv2.Resize(source, destination, new(width, height), interpolation: InterpolationFlags.Area);
        var bytes = new byte[CanvasSize * CanvasSize];
        Marshal.Copy(normalized.Data, bytes, 0, bytes.Length);
        var words = new ulong[bytes.Length / 64];
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] >= 128) words[i / 64] |= 1UL << (i % 64);
        return words;
    }

    private static Template[] CreateTemplates()
    {
        var templates = new List<Template>();
        foreach (var wing in new[] { 0.65, 0.80, 0.95 })
        foreach (var (notch, tail) in new[] { (0.2, false), (0.2, true), (0.4, true), (0.5, true) })
        {
            using var shape = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
            Point P(double x, double y) => new((int)Math.Round(x * 40 + 60), (int)Math.Round(y * 40 + 60));
            Cv2.FillPoly(shape, [new Point[] { P(0, -1), P(wing, 0.6), P(0, notch), P(-wing, 0.6) }], Scalar.White);
            if (tail)
                Cv2.FillPoly(shape, [new Point[] { P(0, notch), P(0.27, 0.48), P(0, 0.76), P(-0.27, 0.48) }], Scalar.White);
            for (var angle = 0; angle < 360; angle += 5)
            {
                using var rotation = Cv2.GetRotationMatrix2D(new Point2f(60, 60), -angle, 1);
                using var rotated = new Mat();
                Cv2.WarpAffine(shape, rotated, rotation, shape.Size());
                Cv2.FindContours(rotated, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                var pixels = Normalize(rotated, Cv2.BoundingRect(contours.SelectMany(c => c).ToArray()));
                templates.Add(new(angle, pixels, pixels.Sum(word => BitOperations.PopCount(word))));
            }
        }
        return templates.ToArray();
    }
}
