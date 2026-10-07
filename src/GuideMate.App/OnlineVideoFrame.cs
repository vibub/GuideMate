using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Cv = OpenCvSharp;

namespace GuideMate.App;

internal sealed record OnlineVideoFrame(byte[] Image, int Width, int Height, double Time, string MediaKey, string Method, int DetailPixels);

public sealed partial class MainWindow
{
    private async Task<OnlineVideoFrame?> CaptureOnlineFrameAsync(ArrowRegion? region = null)
    {
        if (_videoRouter == null || !IsVisible || WindowState == WindowState.Minimized || !_browser.IsVisible) return null;
        var json = await _videoRouter.ExecuteAsync("window.guideMate?.captureFrame(" + JsonSerializer.Serialize(region) + ") ?? null");
        using var document = JsonDocument.Parse(json);
        var info = document.RootElement;
        if (info.ValueKind != JsonValueKind.Object) return null;
        var width = (int)Finite(info, "width", 0); var height = (int)Finite(info, "height", 0);
        if (width <= 0 || height <= 0 || info.GetProperty("seeking").GetBoolean()) return null;
        var time = Finite(info, "time", 0); var key = info.GetProperty("mediaKey").GetString()!;
        var method = info.GetProperty("method").GetString()!;
        if (method == "canvas")
        {
            var data = info.GetProperty("data").GetString()!;
            return new(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]), width, height, time, key, method,
                region == null ? Math.Min(1280, width) : (int)Math.Round(region.Width * width));
        }
        // Browser-rendered pixels remain available when a cross-origin video taints JavaScript canvas.
        using var stream = new MemoryStream();
        await _browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        using var image = Cv.Cv2.ImDecode(stream.ToArray(), Cv.ImreadModes.Color);
        var sx = image.Width / Finite(info, "viewportWidth", 1); var sy = image.Height / Finite(info, "viewportHeight", 1);
        var selection = region ?? new ArrowRegion(0, 0, 1, 1);
        var displayWidth = Finite(info, "displayWidth", 0); var displayHeight = Finite(info, "displayHeight", 0);
        var x = (int)Math.Round((Finite(info, "x", 0) + selection.X * displayWidth) * sx);
        var y = (int)Math.Round((Finite(info, "y", 0) + selection.Y * displayHeight) * sy);
        var cropWidth = (int)Math.Round(selection.Width * displayWidth * sx);
        var cropHeight = (int)Math.Round(selection.Height * displayHeight * sy);
        if (cropWidth < 8 || cropHeight < 8) throw new InvalidOperationException("箭头画面太小，请放大播放器后重试。");
        if (x < 0 || y < 0 || x + cropWidth > image.Width || y + cropHeight > image.Height)
            throw new InvalidOperationException("箭头区域不完整，请把播放器滚动到可见位置。");
        using var crop = new Cv.Mat(image, new Cv.Rect(x, y, cropWidth, cropHeight));
        using var scaled = new Cv.Mat();
        var size = region == null ? new Cv.Size(Math.Min(1280, width), (int)Math.Round(height * Math.Min(1, 1280d / width))) : new Cv.Size(160, 160);
        Cv.Cv2.Resize(crop, scaled, size);
        // A seek or navigation during CapturePreview must not relabel a stale screenshot as a new frame.
        var afterJson = await _videoRouter.ExecuteAsync("window.guideMate?.frameInfo() ?? null");
        using var after = JsonDocument.Parse(afterJson);
        if (after.RootElement.ValueKind != JsonValueKind.Object
            || after.RootElement.GetProperty("mediaKey").GetString() != key
            || after.RootElement.GetProperty("seeking").GetBoolean()
            || Math.Abs(Finite(after.RootElement, "time", 0) - time) > 0.35) return null;
        Cv.Cv2.ImEncode(".png", scaled, out var encoded);
        return new(encoded, width, height, time, key, method, Math.Min(cropWidth, scaled.Width));
    }
}
