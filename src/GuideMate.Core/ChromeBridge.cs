using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuideMate.Core;

public sealed class ChromeCookie
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public bool Session { get; set; }
    public string SameSite { get; set; } = "unspecified";
    public double? ExpirationDate { get; set; }
}

public sealed class ChromeTransfer
{
    public string Type { get; set; } = "";
    public string PairingCode { get; set; } = "";
    public List<ChromeCookie> Cookies { get; set; } = [];
    public int SkippedPartitioned { get; set; }
}

public sealed record BridgeReply(bool Success, string Message, int Imported = 0);

public static class ChromeBridge
{
    public const string HostName = "com.guidemate.bilibili";
    public const string ExtensionId = "emeedledfchopaemhkhjfpbppffbjhid";
    public const int MaxMessageBytes = 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string PipeName => "GuideMate.Chrome." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];

    public static bool IsAllowedDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        var host = domain.StartsWith('.') ? domain[1..] : domain;
        return host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase)
                && host.Split('.').All(label => label.Length > 0 && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    public static ChromeTransfer Parse(byte[] payload)
    {
        var transfer = JsonSerializer.Deserialize<ChromeTransfer>(payload, JsonOptions) ?? throw new FormatException("同步请求为空。");
        if (transfer.Type != "import-bilibili" || transfer.PairingCode is not { Length: 32 }
            || !transfer.PairingCode.All(char.IsAsciiHexDigit) || transfer.Cookies == null || transfer.Cookies.Count > 300)
            throw new FormatException("同步请求格式无效。");
        foreach (var cookie in transfer.Cookies)
        {
            if (cookie == null || !IsAllowedDomain(cookie.Domain) || string.IsNullOrEmpty(cookie.Name) || cookie.Name.Length > 256
                || cookie.Name.Any(c => c < 33 || c >= 127 || "()<>@,;:\\\"/[]?={}".Contains(c))
                || cookie.Value == null || cookie.Value.Length > 8192 || cookie.Value.Any(c => c is '\r' or '\n' or '\0')
                || string.IsNullOrEmpty(cookie.Path) || !cookie.Path.StartsWith('/') || cookie.Path.Length > 2048
                || cookie.SameSite is not ("no_restriction" or "lax" or "strict" or "unspecified")
                || !cookie.Session && (cookie.ExpirationDate == null || !double.IsFinite(cookie.ExpirationDate.Value)
                    || cookie.ExpirationDate < 0 || cookie.ExpirationDate > 253402300799)
                || cookie.SameSite == "no_restriction" && !cookie.Secure)
                throw new FormatException("同步请求包含非 B 站或无效的 Cookie。未导入任何数据。");
        }
        return transfer;
    }

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellation = default)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > MaxMessageBytes) throw new FormatException("同步消息过大或为空。");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellation);
        return payload;
    }

    public static async Task WriteAsync(Stream stream, object message, CancellationToken cancellation = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxMessageBytes) throw new FormatException("同步消息过大。");
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(payload, cancellation);
        await stream.FlushAsync(cancellation);
    }
}
