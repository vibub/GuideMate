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
        return Math.Abs(p.X - current.Width / 2d) < 40 && Math.Abs(p.Y - current.Height / 2d) < 40 ? current : null;
    }
    public void Reset() => _previous = null;
}

/// <summary>小地图像素到统一底图像素的相似变换。</summary>
public sealed record MapLocation(double A, double B, double Tx, double Ty, int Width, int Height, double Confidence)
{
    public MapPoint Position => ToAtlas(new(Width / 2d, Height / 2d));
    public MapPoint ToAtlas(MapPoint point) => new(A * point.X - B * point.Y + Tx, B * point.X + A * point.Y + Ty);
    public MapPoint ToMinimap(MapPoint point)
    {
        var x = point.X - Tx; var y = point.Y - Ty; var scale = A * A + B * B;
        return new((A * x + B * y) / scale, (-B * x + A * y) / scale);
    }
}
