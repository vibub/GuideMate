using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuideMate.Core;

public static partial class Subtitles
{
    [GeneratedRegex(@"(?<start>(?:\d+:)?\d{2}:\d{2}[.,]\d+)\s*-->\s*(?<end>(?:\d+:)?\d{2}:\d{2}[.,]\d+)")]
    private static partial Regex Timeline();
    [GeneratedRegex(@"<[^>]*>|\{\\[^}]*\}")]
    private static partial Regex Markup();

    public static IReadOnlyList<SubtitleCue> Parse(string text, string extension)
    {
        var cues = extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? ParseJson(text) : ParseText(text);
        var result = cues.Where(c => double.IsFinite(c.Start) && double.IsFinite(c.End) && c.Start >= 0 && c.End > c.Start && c.Text.Length > 0)
            .OrderBy(c => c.Start).ThenBy(c => c.End).ToArray();
        if (result.Length == 0) throw new FormatException("没有读取到有效字幕，请选择 SRT、VTT 或 B 站字幕 JSON。");
        return result;
    }

    private static IEnumerable<SubtitleCue> ParseJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var body = document.RootElement;
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("body", out var array)) body = array;
        if (body.ValueKind != JsonValueKind.Array) throw new FormatException("JSON 字幕需要 body 数组或字幕数组。");
        var cues = new List<SubtitleCue>();
        foreach (var cue in body.EnumerateArray())
        {
            if (cue.ValueKind != JsonValueKind.Object || !cue.TryGetProperty("from", out var start)
                || !cue.TryGetProperty("to", out var end) || !cue.TryGetProperty("content", out var content)) continue;
            if (start.TryGetDouble(out var from) && end.TryGetDouble(out var to) && content.ValueKind == JsonValueKind.String)
                cues.Add(new(from, to, Clean(content.GetString() ?? "")));
        }
        return cues;
    }

    private static IEnumerable<SubtitleCue> ParseText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cues = new List<SubtitleCue>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("NOTE", StringComparison.Ordinal) || lines[i].Trim() is "STYLE" or "REGION")
            {
                while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1])) i++;
                continue;
            }
            var match = Timeline().Match(lines[i]);
            if (!match.Success) continue;
            var content = new List<string>();
            while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]) && !Timeline().IsMatch(lines[i + 1]))
                content.Add(lines[++i]);
            cues.Add(new(ParseTime(match.Groups["start"].Value), ParseTime(match.Groups["end"].Value), Clean(string.Join("\n", content))));
        }
        return cues;
    }

    private static double ParseTime(string value)
    {
        var fields = value.Replace(',', '.').Split(':');
        var values = fields.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return values.Length == 3 ? values[0] * 3600 + values[1] * 60 + values[2] : values[0] * 60 + values[1];
    }

    public static string Clean(string text) => WebUtility.HtmlDecode(Markup().Replace(text, "")).Trim();

    public static string At(IReadOnlyList<SubtitleCue> cues, double videoTime, double offset = 0)
    {
        var time = videoTime - offset;
        return string.Join("\n", cues.Where(c => c.Start <= time && time < c.End).Select(c => c.Text));
    }
}
