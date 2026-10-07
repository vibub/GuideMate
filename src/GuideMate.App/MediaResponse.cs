using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

internal static class MediaResponse
{
    public static CoreWebView2WebResourceResponse Create(CoreWebView2Environment environment, CoreWebView2WebResourceRequest request, string path)
    {
        var length = new FileInfo(path).Length;
        const string commonHeaders = "Accept-Ranges: bytes\r\nAccess-Control-Allow-Origin: https://guidemate.local\r\nCache-Control: no-store";
        CoreWebView2WebResourceResponse InvalidRange() => environment.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"{commonHeaders}\r\nContent-Length: 0\r\nContent-Range: bytes */{length}");
        long start = 0, end = length - 1;
        var ranged = request.Method == "GET" && request.Headers.Contains("Range");
        if (ranged)
        {
            var match = Regex.Match(request.Headers.GetHeader("Range"), @"^bytes=(\d*)-(\d*)$");
            if (!match.Success || (match.Groups[1].Value == "" && match.Groups[2].Value == ""))
                return InvalidRange();
            if (match.Groups[1].Value == "")
            {
                if (!long.TryParse(match.Groups[2].Value, out var suffix) || suffix <= 0) return InvalidRange();
                start = Math.Max(0, length - suffix);
            }
            else
            {
                if (!long.TryParse(match.Groups[1].Value, out start)) return InvalidRange();
                if (match.Groups[2].Value != "" && !long.TryParse(match.Groups[2].Value, out end)) return InvalidRange();
                end = Math.Min(end, length - 1);
            }
        }
        if (start < 0 || start > end || start >= length) return InvalidRange();
        var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".webm" => "video/webm", ".mp4" or ".m4v" => "video/mp4", ".mov" => "video/quicktime", ".mkv" => "video/x-matroska", _ => "application/octet-stream" };
        var headers = $"Content-Type: {mime}\r\nContent-Length: {end - start + 1}\r\n{commonHeaders}";
        if (ranged) headers += $"\r\nContent-Range: bytes {start}-{end}/{length}";
        var stream = request.Method == "HEAD" ? null : new RangeStream(path, start, end - start + 1);
        return environment.CreateWebResourceResponse(stream, ranged ? 206 : 200, ranged ? "Partial Content" : "OK", headers);
    }

    private sealed class RangeStream(string path, long start, long length) : Stream
    {
        private readonly FileStream _file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            _file.Position = start + _position;
            var read = _file.Read(buffer, offset, (int)Math.Min(count, length - _position));
            _position += read; return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (position < 0 || position > length) throw new IOException("Invalid media stream position");
            return _position = position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _file.Dispose(); base.Dispose(disposing); }
    }
}
