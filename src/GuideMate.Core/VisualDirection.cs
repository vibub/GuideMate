namespace GuideMate.Core;

public record ArrowRegion(double X, double Y, double Width, double Height)
{
    public bool IsValid => new[] { X, Y, Width, Height }.All(double.IsFinite)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}

public record VisualDirectionFrame(double Time, double? Angle, double Score);

public enum VisionGame { Genshin, Endfield }

public record OnlineVisionProfile(int Width, int Height, ArrowRegion Region, bool NorthLocked, double NorthAngle,
    VisionGame Game = VisionGame.Genshin);

public sealed class VisualDirectionTrack
{
    public int Version { get; set; } = 1;
    public string SourcePath { get; set; } = "";
    public long SourceLength { get; set; }
    public long SourceModifiedUtcTicks { get; set; }
    public double Interval { get; set; } = 0.5;
    public VisionGame Game { get; set; }
    public bool NorthLocked { get; set; }
    public double NorthAngle { get; set; }
    public ArrowRegion Region { get; set; } = new(0, 0, 1, 1);
    public List<VisualDirectionFrame> Frames { get; set; } = [];

    public VisualDirectionFrame? At(double time)
    {
        if (!double.IsFinite(time) || time < 0) return null;
        var low = 0; var high = Frames.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (Frames[middle].Time <= time) low = middle + 1;
            else high = middle - 1;
        }
        if (high < 0) return null;
        var frame = Frames[high];
        return frame.Angle != null && time - frame.Time < Interval * 1.5 ? frame : null;
    }

    public DirectionHint? HintAt(double time)
    {
        var frame = At(time);
        if (frame?.Angle is not { } angle) return null;
        angle = Normalize(angle - (NorthLocked ? NorthAngle : 0));
        var label = NorthLocked ? new[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" }[(int)Math.Floor((angle + 22.5) / 45) % 8] : "画面角度";
        return new($"{label} {angle:0}°", NorthLocked ? angle : null, "攻略箭头 · 视觉", $"几何得分 {frame.Score:0.00}");
    }

    public static double Normalize(double angle) => (angle % 360 + 360) % 360;

    public bool Matches(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && string.Equals(file.FullName, SourcePath, StringComparison.OrdinalIgnoreCase)
            && file.Length == SourceLength && file.LastWriteTimeUtc.Ticks == SourceModifiedUtcTicks;
    }

    public bool IsValid => Enum.IsDefined(Game) && Version == 1 && !string.IsNullOrWhiteSpace(SourcePath) && SourceLength >= 0
        && SourceModifiedUtcTicks >= 0 && double.IsFinite(Interval) && Interval >= 0.1 && Interval <= 10
        && double.IsFinite(NorthAngle) && Region is { IsValid: true } && Frames != null
        && Frames.Count <= 200000 && Frames.All(f => f != null && double.IsFinite(f.Time) && f.Time >= 0
            && (f.Angle == null || double.IsFinite(f.Angle.Value) && f.Angle >= 0 && f.Angle < 360)
            && double.IsFinite(f.Score) && f.Score >= 0 && f.Score <= 1)
        && Frames.Zip(Frames.Skip(1)).All(p => p.First.Time < p.Second.Time);
}
