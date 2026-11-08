using System.IO.Compression;
using System.Security.Cryptography;

namespace GuideMate.Core;

/// <summary>从上游公开资源包安装固定版本的地表特征，程序发布包不再分发第三方地图。</summary>
public static class BuiltInMapAssets
{
    public const string Version = "1.0.25";
    public const string KeypointsFile = "Teyvat_0_2048_SIFT.kp.bin";
    public const string DescriptorsFile = "Teyvat_0_2048_SIFT.mat.png";
    public const string PackageUrl = "https://api.nuget.org/v3-flatcontainer/bettergi.assets.map/1.0.25/bettergi.assets.map.1.0.25.nupkg";
    public const string PackageHash = "278EE3542E8C255BF35C70E7CD896F7AB991E1CEEACDD3A4D6326FFD0CE5AAD0";
    private static readonly (string Name, string Hash)[] Files =
    [
        (KeypointsFile, "53A6448EE99EE19261522351E85CBC94FD3D58E61D3C4CA614D78F10B9F0F0F1"),
        (DescriptorsFile, "097148CEBC0F54B72E36FBC829E904185C105D9E1C78B14412251E571F3D9425")
    ];

    public static string CacheDirectory(string dataPath) => Path.Combine(dataPath, "MapResources", "BetterGI-" + Version);

    public static async Task<bool> IsReadyAsync(string directory, CancellationToken cancellationToken = default)
    {
        foreach (var (name, hash) in Files)
            if (!await MatchesAsync(Path.Combine(directory, name), hash, cancellationToken)) return false;
        return true;
    }

    public static async Task<string> EnsureAsync(string dataPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var directory = CacheDirectory(dataPath);
        if (await IsReadyAsync(directory, cancellationToken)) return directory;
        Directory.CreateDirectory(directory);
        var download = Path.Combine(directory, ".download-" + Guid.NewGuid().ToString("N") + ".nupkg");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var response = await client.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(download))
            {
                var buffer = new byte[131072]; long received = 0; var lastPercent = -1;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken);
                    if (count == 0) break;
                    received += count;
                    if (received > 220_000_000) throw new InvalidDataException("地图资源包大小与固定版本不符。");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    var percent = (int)(received * 100 / 215576403);
                    if (percent != lastPercent)
                    { lastPercent = percent; progress?.Report($"正在下载提瓦特地表地图资源 {percent}% · {received / 1048576d:0.0} / 205.6 MiB"); }
                }
            }
            progress?.Report("正在校验并安装地图资源…");
            await InstallArchiveAsync(download, directory, cancellationToken);
            return directory;
        }
        finally { if (File.Exists(download)) File.Delete(download); }
    }

    public static async Task InstallArchiveAsync(string archivePath, string directory, CancellationToken cancellationToken = default)
    {
        if (!await MatchesAsync(archivePath, PackageHash, cancellationToken))
            throw new InvalidDataException("地图资源包 SHA-256 校验失败，请重新下载。");
        Directory.CreateDirectory(directory);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var (name, hash) in Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry("contentFiles/any/any/Assets/Map/Teyvat/" + name)
                ?? throw new InvalidDataException("地图资源包缺少 " + name);
            var temporary = Path.Combine(directory, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var source = entry.Open())
                await using (var target = File.Create(temporary))
                    await source.CopyToAsync(target, cancellationToken);
                if (!await MatchesAsync(temporary, hash, cancellationToken)) throw new InvalidDataException("地图特征校验失败：" + name);
                File.Move(temporary, Path.Combine(directory, name), true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static async Task<bool> MatchesAsync(string path, string hash, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)) == hash;
    }
}
