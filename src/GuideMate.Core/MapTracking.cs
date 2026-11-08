namespace GuideMate.Core;

/// <summary>独立的地图校准，不改动既有箭头方向选区。</summary>
public sealed record MapTrackingProfile(string AtlasPath, ArrowRegion? GuideRegion, ArrowRegion? GameRegion);

public readonly record struct MapPoint(double X, double Y);

/// <summary>每次跳变须由连续两帧确认；失效后不能继续使用旧点。</summary>
public sealed class MapTrackConfirmation
{
    private MapLocation? _previous;
    public MapLocation? Accept(MapLocation? current)
    {
        var previous = _previous; _previous = current;
        if (previous == null || current == null) return null;
        var p = current.ToMinimap(previous.Position);
        var anchor = current.PlayerPixel ?? new MapPoint(current.Width / 2d, current.Height / 2d);
        return Math.Abs(p.X - anchor.X) < 40 && Math.Abs(p.Y - anchor.Y) < 40 ? current : null;
    }
    public void Reset() => _previous = null;
}

/// <summary>小地图像素到统一底图像素的相似变换。</summary>
public sealed record MapLocation(double A, double B, double Tx, double Ty, int Width, int Height, double Confidence)
{
    public MapPoint? PlayerPixel { get; init; }
    /// <summary>统一底图中的玩家朝向：北为 0，顺时针递增。</summary>
    public double? PlayerHeading { get; init; }
    public double HeadingConfidence { get; init; }
    public MapPoint Position => ToAtlas(PlayerPixel ?? new(Width / 2d, Height / 2d));
    public MapPoint ToAtlas(MapPoint point) => new(A * point.X - B * point.Y + Tx, B * point.X + A * point.Y + Ty);
    public MapPoint ToMinimap(MapPoint point)
    {
        var x = point.X - Tx; var y = point.Y - Ty; var scale = A * A + B * B;
        return new((A * x + B * y) / scale, (-B * x + A * y) / scale);
    }
}

public sealed class MapHeadingConfirmation
{
    private double? _heading, _pending;
    private long _stamp, _pendingStamp;
    public double? Accept(double? angle, double confidence, long timestamp)
    {
        if (angle == null || confidence < .65) { Reset(); return null; }
        double Difference(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);
        if (_heading is { } previous && timestamp - _stamp < 500 && Difference(angle.Value, previous) > 75)
        {
            if (_pending == null || timestamp - _pendingStamp > 500 || Difference(angle.Value, _pending.Value) > 20)
            { _pending = angle; _pendingStamp = timestamp; return timestamp - _stamp < 250 ? previous : null; }
        }
        _heading = angle; _stamp = timestamp; _pending = null; return angle;
    }
    public void Reset() { _heading = null; _pending = null; _stamp = 0; }
}
