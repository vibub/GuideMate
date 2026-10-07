using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuideMate.Core;

public static class VisualDirectionCache
{
    private static string CachePath(string dataDirectory, string path)
    {
        var file = new FileInfo(path);
        var identity = $"{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return Path.Combine(dataDirectory, "vision", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
    }

    public static VisualDirectionTrack? Load(string dataDirectory, string path)
    {
        var cache = CachePath(dataDirectory, path);
        if (!File.Exists(cache)) return null;
        if (new FileInfo(cache).Length > 32 * 1024 * 1024) throw new FormatException("Visual direction cache is too large.");
        var track = JsonSerializer.Deserialize<VisualDirectionTrack>(File.ReadAllText(cache));
        if (track is not { IsValid: true }) throw new FormatException("Invalid visual direction cache.");
        return track.Matches(path) ? track : null;
    }

    public static void Save(string dataDirectory, VisualDirectionTrack track)
    {
        if (!track.IsValid || !track.Matches(track.SourcePath)) throw new FormatException("Visual track does not match its source video.");
        var path = CachePath(dataDirectory, track.SourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(track));
        File.Move(path + ".tmp", path, true);
    }
}
