using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuideMate.Core;

public sealed record ReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Notes,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] ReleaseAsset[] Assets)
{
    public ReleaseAsset Package => (Assets ?? []).SingleOrDefault(asset => asset.Name == $"GuideMate-{Tag}-win-x64.zip")
        ?? throw new FormatException("此 Release 没有 GuideMate Windows x64 ZIP 安装包。");
}

public sealed class GitHubReleaseClient(HttpClient http)
{
    public const string RepositoryUrl = "https://github.com/vibub/GuideMate";
    private const string Api = "https://api.github.com/repos/vibub/GuideMate/releases/";

    public async Task<GitHubRelease?> GetAsync(string? tag, CancellationToken cancellationToken = default)
    {
        if (tag != null) ReleaseVersion.Parse(tag);
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + (tag == null ? "latest" : "tags/" + Uri.EscapeDataString(tag)));
        request.Headers.UserAgent.ParseAdd("GuideMate-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (tag == null) return null;
            throw new IOException("没有找到指定的公开 Release：" + tag);
        }
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub 暂时限制了请求，请稍后重试。");
        response.EnsureSuccessStatusCode();
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
            ?? throw new FormatException("GitHub 返回了空的 Release 信息。");
        ReleaseVersion.Parse(release.Tag);
        if (tag != null && release.Tag != tag) throw new FormatException("返回的 Release 与指定版本不一致。");
        if (release.Draft || (tag == null && release.Prerelease)) throw new FormatException("自动检查只接受已发布的正式版本。");
        _ = release.Package;
        return release;
    }

    private static Uri AssetUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith("/vibub/GuideMate/releases/download/", StringComparison.Ordinal))
            throw new FormatException("安装包下载地址不属于随引仓库的 Release。");
        return uri;
    }

    public async Task DownloadAsync(GitHubRelease release, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var asset = release.Package;
        if (asset.Size is <= 0 or > 1024L * 1024 * 1024) throw new FormatException("安装包大小无效。");
        var digest = asset.Digest;
        if (digest == null)
        {
            var checksum = release.Assets.SingleOrDefault(item => item.Name == asset.Name + ".sha256")
                ?? throw new FormatException("Release 缺少 SHA256 校验，不能覆盖安装。");
            using var checksumResponse = await http.GetAsync(AssetUri(checksum.DownloadUrl), cancellationToken);
            checksumResponse.EnsureSuccessStatusCode();
            var content = (await checksumResponse.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[1].TrimStart('*') != asset.Name) throw new FormatException("SHA256 校验文件与安装包不匹配。");
            digest = "sha256:" + parts[0];
        }
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71
            || !digest[7..].All(Uri.IsHexDigit)) throw new FormatException("Release 的 SHA256 校验格式无效。");
        using var request = new HttpRequestMessage(HttpMethod.Get, AssetUri(asset.DownloadUrl));
        request.Headers.UserAgent.ParseAdd("GuideMate-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > asset.Size) throw new IOException("下载内容超过 Release 标记的大小。");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report((double)total / asset.Size);
        }
        if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装包不完整或 SHA256 校验失败；原程序没有改动。");
    }
}
