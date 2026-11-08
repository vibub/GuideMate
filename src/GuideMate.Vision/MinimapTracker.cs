using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

/// <summary>复用已定位区域，以地形块相对绝对定位帧的位移更新坐标，避免逐帧全图搜索和累计漂移。</summary>
public sealed class MinimapTracker : IDisposable
{
    private MinimapObservation? _reference;
    private Mat? _gray;
    private Size _frameSize;
    private MapLocator? _local;
    private MapLocation? _localOrigin;
    private long _globalTime;
    private long _referenceTime, _lastAttempt = -1000;
    public bool UsedTemporalMatch { get; private set; }

    /// <summary>采集从整帧切换为已锁定选区时，保留绝对参考并换算裁切后的像素尺度。</summary>
    public MinimapObservation? LocateCropped(Mat frame, MapLocator locator, long timestamp)
    {
        if (_reference is { } previous)
        {
            var p = previous.Location;
            var sx = (double)frame.Width / p.Width; var sy = (double)frame.Height / p.Height;
            if (Math.Abs(sx / sy - 1) > .02) { Reset(); return null; }
            var anchor = p.PlayerPixel ?? new MapPoint(p.Width / 2d, p.Height / 2d);
            _reference = new(new(0, 0, frame.Width, frame.Height), p with
            {
                A = p.A / sx, B = p.B / sx, Width = frame.Width, Height = frame.Height,
                PlayerPixel = new(anchor.X * sx, anchor.Y * sy)
            });
            _frameSize = frame.Size();
        }
        return Locate(frame, locator, false, false, timestamp);
    }

    public MinimapObservation? Locate(Mat frame, MapLocator locator, bool automatic, bool gameHud, long timestamp)
    {
        UsedTemporalMatch = false;
        if (frame.Size() != _frameSize) Reset();
        _frameSize = frame.Size();
        MinimapObservation? tracked = null;
        if (_reference is { } previous)
        {
            using var crop = new Mat(frame, previous.Region);
            var anchor = previous.Location.PlayerPixel ?? new MapPoint(crop.Width / 2d, crop.Height / 2d);
            if (FindPlayer(crop, anchor) is { } currentAnchor)
            {
                using var gray = TrackingImage(crop);
                if (timestamp - _referenceTime <= 2500 && Translation(_gray!, gray) is { } delta)
                {
                    var dx = delta.X * crop.Width / gray.Width; var dy = delta.Y * crop.Height / gray.Height;
                    var p = previous.Location;
                    tracked = previous with { Location = p with { Tx = p.Tx - p.A * dx + p.B * dy, Ty = p.Ty - p.B * dx - p.A * dy, PlayerPixel = currentAnchor } };
                }
                if ((tracked == null || timestamp - _referenceTime >= 750) && timestamp - _lastAttempt >= 300)
                {
                    _lastAttempt = timestamp;
                    MapLocation? fix = null;
                    if (timestamp - _globalTime >= 5000)
                    {
                        fix = locator.Locate(crop, playerPixel: currentAnchor); _globalTime = timestamp;
                    }
                    fix ??= _local?.Locate(crop, playerPixel: currentAnchor);
                    if (fix == null && tracked == null) fix = locator.Locate(crop, playerPixel: currentAnchor);
                    if (fix != null) return Remember(frame, locator, new(previous.Region, fix), timestamp);
                }
            }
            if (tracked != null) { UsedTemporalMatch = true; return tracked; }
            // 短暂遮挡不删除绝对参考；不输出旧位置，下一帧仍可尝试邻域定位和地形恢复。
            if (timestamp - _referenceTime < 2500) return null;
            _reference = null; _gray?.Dispose(); _gray = null;
        }
        if (timestamp - _lastAttempt < 300) return null;
        _lastAttempt = timestamp;
        MinimapObservation? found;
        if (automatic) found = MinimapDetector.Locate(frame, locator, gameHud);
        else
        {
            var fix = locator.Locate(frame, true);
            found = fix == null ? null : new(new(0, 0, frame.Width, frame.Height), fix);
        }
        if (found == null) return null;
        _globalTime = timestamp;
        return Remember(frame, locator, found, timestamp);
    }

    private MinimapObservation Remember(Mat frame, MapLocator locator, MinimapObservation found, long timestamp)
    {
        _gray?.Dispose();
        using var crop = new Mat(frame, found.Region);
        _gray = TrackingImage(crop); _reference = found; _referenceTime = timestamp;
        var localPoint = _localOrigin?.ToMinimap(found.Location.Position);
        var origin = _localOrigin?.PlayerPixel ?? new MapPoint(found.Location.Width / 2d, found.Location.Height / 2d);
        if (_localOrigin == null || localPoint is { } p && (Math.Abs(p.X - origin.X) > _localOrigin.Width * .5 || Math.Abs(p.Y - origin.Y) > _localOrigin.Height * .5))
        {
            _local?.Dispose(); _local = locator.CreateLocal(found.Location); _localOrigin = found.Location;
        }
        return found;
    }

    private static MapPoint? FindPlayer(Mat crop, MapPoint anchor)
    {
        var side = (int)(Math.Min(crop.Width, crop.Height) * .3);
        var rect = Rect.Intersect(new((int)anchor.X - side / 2, (int)anchor.Y - side / 2, side, side), new(0, 0, crop.Width, crop.Height));
        using var center = new Mat(crop, rect); using var scaled = new Mat();
        Cv2.Resize(center, scaled, new(160, 160));
        if (ArrowDetector.Detect(scaled, true) == null) return null;
        using var hsv = new Mat(); using var mask = new Mat();
        Cv2.CvtColor(center, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(80, 145, 200), new Scalar(106, 255, 255), mask);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var bounds = contours.Where(c => Cv2.ContourArea(c) >= 15).Select(Cv2.BoundingRect)
            .OrderBy(b => Math.Pow(rect.X + b.X + b.Width / 2d - anchor.X, 2) + Math.Pow(rect.Y + b.Y + b.Height / 2d - anchor.Y, 2)).FirstOrDefault();
        return bounds.Width < 4 ? null : new(rect.X + bounds.X + bounds.Width / 2d, rect.Y + bounds.Y + bounds.Height / 2d);
    }

    private static Mat TrackingImage(Mat crop)
    {
        using var gray = new Mat(); Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        var result = new Mat(); Cv2.Resize(gray, result, new(256, 256)); return result;
    }

    // 多块地形必须一致平移；中心箭头、圆周边框以及少数固定图标不参与投票。
    private static MapPoint? Translation(Mat reference, Mat current)
    {
        var matches = new List<(double X, double Y, int Px, int Py)>();
        var patches = new List<(int X, int Y, double Texture)>();
        // 海面中的有效纹理不一定落在固定八个点上，先在环带挑选最有纹理的地形块。
        for (var y = 64; y <= 192; y += 32)
        for (var x = 64; x <= 192; x += 32)
        {
            var distance = Math.Sqrt(Math.Pow(x - 128, 2) + Math.Pow(y - 128, 2));
            if (distance < 48 || distance > 92) continue;
            using var patch = new Mat(reference, new Rect(x - 16, y - 16, 32, 32));
            Cv2.MeanStdDev(patch, out _, out var deviation);
            if (deviation.Val0 >= 9) patches.Add((x, y, deviation.Val0));
        }
        foreach (var (px, py, _) in patches.OrderByDescending(p => p.Texture).Take(12))
        {
            using var patch = new Mat(reference, new Rect(px - 16, py - 16, 32, 32));
            using var search = new Mat(current, new Rect(px - 48, py - 48, 96, 96));
            using var scores = new Mat(); Cv2.MatchTemplate(search, patch, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out var peak, out _, out var point);
            if (peak < .82 || point.X == 0 || point.Y == 0 || point.X == 64 || point.Y == 64) continue;
            double Refine(float left, float mid, float right)
            {
                var d = left - 2 * mid + right;
                return Math.Abs(d) < 1e-6 ? 0 : Math.Clamp(.5 * (left - right) / d, -.5, .5);
            }
            var dx = point.X - 32 + Refine(scores.At<float>(point.Y, point.X - 1), scores.At<float>(point.Y, point.X), scores.At<float>(point.Y, point.X + 1));
            var dy = point.Y - 32 + Refine(scores.At<float>(point.Y - 1, point.X), scores.At<float>(point.Y, point.X), scores.At<float>(point.Y + 1, point.X));
            matches.Add((dx, dy, px, py));
        }
        var best = matches.Select(m => matches.Where(n => Math.Abs(m.X - n.X) < 1.5 && Math.Abs(m.Y - n.Y) < 1.5).ToArray())
            .OrderByDescending(group => group.Length).FirstOrDefault();
        if (best == null || best.Length < 4 || best.Max(m => m.Px) - best.Min(m => m.Px) < 80 || best.Max(m => m.Py) - best.Min(m => m.Py) < 80) return null;
        return new(best.Average(m => m.X), best.Average(m => m.Y));
    }

    public void Reset()
    {
        _reference = null; _gray?.Dispose(); _gray = null; _lastAttempt = -1000;
        _local?.Dispose(); _local = null; _localOrigin = null;
    }
    public void Dispose() => Reset();
}
