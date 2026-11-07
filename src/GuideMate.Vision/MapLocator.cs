using GuideMate.Core;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace GuideMate.Vision;

/// <summary>地表北向地图定位。仅使用图像；不读取游戏内存。</summary>
public sealed class MapLocator : IDisposable
{
    private readonly SIFT _features;
    private readonly Mat _descriptors;
    private readonly KeyPoint[] _points;
    private readonly Size _size;

    public MapLocator(Mat atlas)
    {
        if (atlas.Empty() || atlas.Width > 8192 || atlas.Height > 8192)
            throw new ArgumentException("底图应为非空且边长不超过 8192 像素的单个地表区域。");
        _features = SIFT.Create(18000); _descriptors = new();
        _size = atlas.Size();
        try
        {
            using var gray = Gray(atlas);
            _features.DetectAndCompute(gray, null, out _points, _descriptors);
            if (_points.Length < 30) throw new ArgumentException("底图缺少可识别的地形纹理。");
        }
        catch { Dispose(); throw; }
    }

    public MapLocation? Locate(Mat minimap, bool requirePlayerArrow = false)
    {
        if (minimap.Empty() || minimap.Width < 96 || minimap.Height < 96) return null;
        if (requirePlayerArrow)
        {
            var side = (int)(Math.Min(minimap.Width, minimap.Height) * .24);
            using var centerCrop = new Mat(minimap, new Rect((minimap.Width - side) / 2, (minimap.Height - side) / 2, side, side));
            using var arrow = new Mat(); Cv2.Resize(centerCrop, arrow, new(160, 160));
            if (ArrowDetector.Detect(arrow, true) == null) return null;
        }
        using var gray = Gray(minimap);
        using var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.Black);
        var center = new Point(gray.Width / 2, gray.Height / 2);
        var radius = Math.Min(gray.Width, gray.Height) / 2;
        Cv2.Circle(mask, center, (int)(radius * .88), Scalar.White, -1);
        Cv2.Circle(mask, center, (int)(radius * .17), Scalar.Black, -1);
        using var query = new Mat();
        _features.DetectAndCompute(gray, mask, out var points, query);
        if (points.Length < 12 || query.Empty()) return null;
        using var matcher = new BFMatcher(NormTypes.L2);
        var matches = matcher.KnnMatch(query, _descriptors, 2)
            .Where(m => m.Length == 2 && m[0].Distance < .7 * m[1].Distance)
            .Select(m => m[0]).OrderBy(m => m.Distance).DistinctBy(m => m.TrainIdx)
            .GroupBy(m => ((int)(points[m.QueryIdx].Pt.X * 4 / gray.Width), (int)(points[m.QueryIdx].Pt.Y * 4 / gray.Height)))
            .SelectMany(group => group.Take(12)).ToArray();
        if (matches.Length < 12) return null;
        var pairs = matches.Select(m => (Q: points[m.QueryIdx].Pt, T: _points[m.TrainIdx].Pt)).ToArray();

        // 两点求相似变换，以小地图像素误差做一致性检查，避免单应变换过度拟合。
        MapLocation? best = null; int[] bestInliers = [];
        var random = new Random(431);
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var i = random.Next(pairs.Length); var j = random.Next(pairs.Length);
            var dx = pairs[j].Q.X - pairs[i].Q.X; var dy = pairs[j].Q.Y - pairs[i].Q.Y;
            var norm = dx * dx + dy * dy;
            if (norm < radius * radius * .15) continue;
            var tx = pairs[j].T.X - pairs[i].T.X; var ty = pairs[j].T.Y - pairs[i].T.Y;
            var a = (dx * tx + dy * ty) / norm; var b = (dx * ty - dy * tx) / norm;
            var scale = Math.Sqrt(a * a + b * b);
            if (scale < .2 || scale > 12 || Math.Abs(Math.Atan2(b, a)) > Math.PI / 15) continue;
            var candidate = new MapLocation(a, b, pairs[i].T.X - a * pairs[i].Q.X + b * pairs[i].Q.Y,
                pairs[i].T.Y - b * pairs[i].Q.X - a * pairs[i].Q.Y, gray.Width, gray.Height, 0);
            var inliers = Enumerable.Range(0, pairs.Length).Where(k =>
            {
                var p = candidate.ToMinimap(new(pairs[k].T.X, pairs[k].T.Y));
                return Math.Pow(p.X - pairs[k].Q.X, 2) + Math.Pow(p.Y - pairs[k].Q.Y, 2) <= 9;
            }).ToArray();
            if (inliers.Length > bestInliers.Length) { best = candidate; bestInliers = inliers; }
        }
        if (best == null || bestInliers.Length < 12 || bestInliers.Length < pairs.Length * .55) return null;
        var accepted = bestInliers.Select(i => pairs[i]).ToArray();
        if (accepted.Max(p => p.Q.X) - accepted.Min(p => p.Q.X) < radius * .7
            || accepted.Max(p => p.Q.Y) - accepted.Min(p => p.Q.Y) < radius * .7) return null;
        // 使用所有内点最小二乘精修，不依赖额外的原生 calib3d 模块。
        var qx = accepted.Average(p => p.Q.X); var qy = accepted.Average(p => p.Q.Y);
        var ax = accepted.Average(p => p.T.X); var ay = accepted.Average(p => p.T.Y);
        double denominator = 0, real = 0, imaginary = 0;
        foreach (var p in accepted)
        {
            var x = p.Q.X - qx; var y = p.Q.Y - qy;
            denominator += x * x + y * y;
            real += x * (p.T.X - ax) + y * (p.T.Y - ay);
            imaginary += x * (p.T.Y - ay) - y * (p.T.X - ax);
        }
        var ra = real / denominator; var rb = imaginary / denominator;
        var result = new MapLocation(ra, rb, ax - ra * qx + rb * qy, ay - rb * qx - ra * qy,
            gray.Width, gray.Height, (double)accepted.Length / pairs.Length);
        var position = result.Position;
        return position.X >= 0 && position.Y >= 0 && position.X < _size.Width && position.Y < _size.Height ? result : null;
    }

    private static Mat Gray(Mat image)
    {
        if (image.Channels() == 1) return image.Clone();
        var gray = new Mat();
        Cv2.CvtColor(image, gray, image.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public void Dispose() { _descriptors.Dispose(); _features.Dispose(); }
}
