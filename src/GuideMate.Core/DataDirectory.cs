using System.Text.Json;

namespace GuideMate.Core;

public static class DataDirectory
{
    private sealed record ActiveProfile(string Directory);

    public static string Resolve(string defaultDirectory, string? requestedDirectory, bool smoke)
    {
        if (smoke) return Path.GetFullPath(requestedDirectory ?? Path.Combine(Path.GetTempPath(), "GuideMate-smoke-" + Guid.NewGuid()));
        var selection = Path.Combine(defaultDirectory, "active-profile.json");
        if (requestedDirectory != null)
        {
            var requested = Path.GetFullPath(requestedDirectory);
            Directory.CreateDirectory(requested);
            Directory.CreateDirectory(defaultDirectory);
            File.WriteAllText(selection + ".tmp", JsonSerializer.Serialize(new ActiveProfile(requested)));
            File.Move(selection + ".tmp", selection, true);
            return requested;
        }
        if (!File.Exists(selection)) return Path.GetFullPath(defaultDirectory);
        var profile = JsonSerializer.Deserialize<ActiveProfile>(File.ReadAllText(selection));
        if (profile == null || string.IsNullOrWhiteSpace(profile.Directory) || !Path.IsPathFullyQualified(profile.Directory))
            throw new FormatException("已保存的用户数据目录无效，请检查 " + selection + "。没有重置用户数据。");
        if (!Directory.Exists(profile.Directory))
            throw new DirectoryNotFoundException("原用户数据目录不存在或暂不可访问：" + profile.Directory + "。没有新建空白资料。请恢复该目录后重试。");
        return profile.Directory;
    }
}
