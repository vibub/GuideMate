using System.Text.RegularExpressions;

namespace GuideMate.Core;

public static partial class DirectionAnalyzer
{
    private static readonly Dictionary<string, double> Angles = new()
    {
        ["北"] = 0, ["东北"] = 45, ["东"] = 90, ["东南"] = 135,
        ["南"] = 180, ["西南"] = 225, ["西"] = 270, ["西北"] = 315
    };
    [GeneratedRegex(@"(?:(?:朝|往|向|前往|走到)\s*(?<d>东北|东南|西北|西南|北|东|南|西)(?!西|东)|(?<d>东北|东南|西北|西南|北|东|南|西)(?:方向|方|边|侧)|(?<d>左转|右转|向左|向右|往左|往右|往前|向前|往后|向后|上楼|下楼|掉头)|\b(?<e>north[- ]?east|north[- ]?west|south[- ]?east|south[- ]?west|north|south|east|west|turn left|turn right|go forward|go back)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Directions();
    [GeneratedRegex(@"(?:不要|不用|别|不是|不往|不向|不能|do not|don't|not)\s*(?:再|往|向|朝|走|turn|go)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();
    [GeneratedRegex(@"(?:清怪|打怪|打败|击败|进入战斗|开始战斗|fight|combat|defeat)", RegexOptions.IgnoreCase)]
    private static partial Regex Combat();

    public static bool IsCombat(string text) => Combat().Matches(text).Any(m => !IsNegated(text, m.Index));

    private static bool IsNegated(string text, int index) => Negation().IsMatch(text[Math.Max(0, index - 16)..index]);

    public static DirectionHint? Analyze(string text)
    {
        foreach (Match match in Directions().Matches(text))
        {
            if (IsNegated(text, match.Index)) continue;
            var label = match.Groups["d"].Success ? match.Groups["d"].Value : Translate(match.Groups["e"].Value);
            var context = text[Math.Max(0, match.Index - 12)..Math.Min(text.Length, match.Index + match.Length + 8)];
            var camera = context.Contains("镜头") || context.Contains("视角") || context.Contains("camera", StringComparison.OrdinalIgnoreCase);
            return new(label, Angles.TryGetValue(label, out var angle) ? angle : null, camera ? "视角" : Angles.ContainsKey(label) ? "方位" : "相对方向", text);
        }
        return null;
    }

    private static string Translate(string value) => value.ToLowerInvariant().Replace("-", "").Replace(" ", "") switch
    {
        "north" => "北", "northeast" => "东北", "east" => "东", "southeast" => "东南",
        "south" => "南", "southwest" => "西南", "west" => "西", "northwest" => "西北",
        "turnleft" => "左转", "turnright" => "右转", "goforward" => "向前", "goback" => "向后", _ => value
    };
}
