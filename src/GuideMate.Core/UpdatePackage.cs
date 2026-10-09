using System.IO.Compression;
using System.Text.Json;

namespace GuideMate.Core;

public sealed record UpdateManifest(string Version, string[] Files);

public static class UpdatePackage
{
    public const string ManifestName = "update-manifest.json";
    private static readonly string[] RequiredFiles = ["GuideMate.exe", "GuideMate.dll", "GuideMate.deps.json", "GuideMate.runtimeconfig.json", "assets/bridge.js"];
    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase)
        { "assets", "chrome-host", "chrome-extension", "licenses", "scripts", "docs", "updater", "runtimes", "zh-Hans", "zh-Hant", "en" };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { "Start-GuideMate.cmd", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "CONTRIBUTING.md", "SECURITY.md", ManifestName };

    public static string ProgramPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.StartsWith('/') || relative.Contains(':'))
            throw new FormatException("安装包包含无效路径：" + relative);
        var parts = relative.Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || part.Any(char.IsControl))
                throw new FormatException("安装包包含不安全路径：" + relative);
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                throw new FormatException("安装包包含设备路径：" + relative);
            if (part.Equals("settings.json", StringComparison.OrdinalIgnoreCase) || part.Equals("active-profile.json", StringComparison.OrdinalIgnoreCase)
                || part.Equals("WebView2", StringComparison.OrdinalIgnoreCase) || part.EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase)
                || part.Equals("vision", StringComparison.OrdinalIgnoreCase) || part.Equals("genshin-preview", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("安装包不能包含用户资料：" + relative);
        }
        var extension = Path.GetExtension(relative);
        var allowed = parts.Length > 1 ? Folders.Contains(parts[0]) : Documents.Contains(relative)
            || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("GuideMate.exe", StringComparison.OrdinalIgnoreCase)
            || (relative.StartsWith("GuideMate", StringComparison.OrdinalIgnoreCase) && extension.Equals(".json", StringComparison.OrdinalIgnoreCase));
        if (!allowed) throw new FormatException("安装包包含未识别的程序文件：" + relative);
        return relative;
    }

    public static UpdateManifest Extract(string archive, string stage, string version)
    {
        ReleaseVersion.Parse(version);
        using var zip = ZipFile.OpenRead(archive);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            ProgramPath(entry.FullName);
            if (!files.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new FormatException("安装包包含重复路径或符号链接。");
            size += entry.Length;
            if (size > 2L * 1024 * 1024 * 1024 || files.Count > 10000) throw new FormatException("解压后的安装包过大。");
        }
        if (RequiredFiles.Any(required => !files.Contains(required))) throw new FormatException("ZIP 缺少完整的随引程序文件，请下载 Release 安装包。");
        Directory.CreateDirectory(stage);
        foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            var destination = Path.Combine(stage, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, false);
        }
        var manifestFile = Path.Combine(stage, ManifestName);
        var actual = files.Where(file => !file.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        UpdateManifest manifest;
        if (File.Exists(manifestFile))
        {
            manifest = ReadManifest(manifestFile);
            if (ReleaseVersion.Parse(manifest.Version).CompareTo(ReleaseVersion.Parse(version)) != 0
                || !actual.SetEquals(manifest.Files)) throw new FormatException("安装包的版本或文件清单与 Release 不一致。");
        }
        else
        {
            // Older published ZIPs predate manifests; only known program paths are accepted.
            manifest = new(version, actual.Order(StringComparer.OrdinalIgnoreCase).ToArray());
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest));
        }
        return manifest;
    }

    private static UpdateManifest ReadManifest(string path)
    {
        var result = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result?.Files == null || result.Files.Length > 10000) throw new FormatException("程序文件清单无效。");
        ReleaseVersion.Parse(result.Version);
        foreach (var file in result.Files) ProgramPath(file);
        if (result.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Files.Length)
            throw new FormatException("程序文件清单包含重复项。");
        return result;
    }

    private static string TargetPath(string install, string relative, string? dataDirectory)
    {
        var target = Path.GetFullPath(Path.Combine(install, relative));
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(install) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("安装路径超出程序目录。");
        if (dataDirectory != null && !Path.TrimEndingDirectorySeparator(dataDirectory).Equals(Path.TrimEndingDirectorySeparator(install), StringComparison.OrdinalIgnoreCase)
            && (target.Equals(dataDirectory, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(Path.TrimEndingDirectorySeparator(dataDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("程序文件与用户资料目录重叠，更新已停止：" + relative);
        for (var current = target; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新目标不能经过符号链接或目录联接：" + current);
        }
        return target;
    }

    public static void Install(string stage, string installDirectory, string backupDirectory, string? dataDirectory)
    {
        var install = Path.GetFullPath(installDirectory);
        if (!File.Exists(Path.Combine(install, "GuideMate.exe"))) throw new IOException("安装目录内没有 GuideMate.exe，请选择已安装的随引目录。");
        if (dataDirectory != null) dataDirectory = Path.GetFullPath(dataDirectory);
        var manifest = ReadManifest(Path.Combine(stage, ManifestName));
        var next = manifest.Files.Append(ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousFile = Path.Combine(install, ManifestName);
        var previous = File.Exists(previousFile) ? ReadManifest(previousFile).Files : [];
        var all = next.Concat(previous).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targets = all.ToDictionary(file => file, file => TargetPath(install, file, dataDirectory), StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(backupDirectory);
        var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in all)
        {
            if (!File.Exists(targets[file])) continue;
            var backup = Path.Combine(backupDirectory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(targets[file], backup, false);
            existed.Add(file);
        }
        var touched = new List<string>();
        try
        {
            foreach (var file in all)
            {
                if (next.Contains(file))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targets[file])!);
                    var temporary = targets[file] + ".guidemate-update-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Copy(Path.Combine(stage, file), temporary, false);
                        File.Move(temporary, targets[file], true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                else File.Delete(targets[file]);
                touched.Add(file);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var failures = new List<Exception>();
            foreach (var file in touched.AsEnumerable().Reverse())
            {
                try
                {
                    if (existed.Contains(file)) File.Copy(Path.Combine(backupDirectory, file), targets[file], true);
                    else File.Delete(targets[file]);
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { failures.Add(restoreError); }
            }
            if (failures.Count != 0)
                throw new UpdateRecoveryException("安装失败，部分文件未能恢复。原程序备份保留在：" + backupDirectory, new AggregateException(failures.Prepend(error)));
            throw new IOException("安装失败，已恢复原程序：" + error.Message, error);
        }
    }
}

public sealed class UpdateRecoveryException(string message, Exception inner) : IOException(message, inner);
