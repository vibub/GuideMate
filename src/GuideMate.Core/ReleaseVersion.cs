using System.Globalization;
using System.Text.RegularExpressions;

namespace GuideMate.Core;

public sealed partial record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex Pattern();

    public static ReleaseVersion Parse(string value)
    {
        if (value == null) throw new FormatException("Release 缺少版本号。");
        var match = Pattern().Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch))
            throw new FormatException("版本号应为 v主版本.次版本.修订号，可附带预发布标记。");
        return new(major, minor, patch, match.Groups[4].Value);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0 || Prerelease == other.Prerelease) return result;
        if (Prerelease.Length == 0) return 1;
        if (other.Prerelease.Length == 0) return -1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var a = decimal.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var b = decimal.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            result = a && b ? an.CompareTo(bn) : a != b ? (a ? -1 : 1) : string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
}
