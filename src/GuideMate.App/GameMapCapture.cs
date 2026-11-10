using System.Diagnostics;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Cv = OpenCvSharp;

namespace GuideMate.App;

internal static class GameMapCapture
{
    internal sealed record Frame(byte[] Image, Drawing.Rectangle Bounds, nint Handle);
    internal sealed record PixelFrame(Cv.Mat Image, Drawing.Rectangle Bounds, nint Handle, long CaptureStarted) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    public static nint ForegroundGame()
    {
        var handle = GetForegroundWindow();
        GetWindowThreadProcessId(handle, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName is "YuanShen" or "GenshinImpact" ? handle : 0;
        }
        catch (ArgumentException) { return 0; }
    }

    public static Drawing.Rectangle Bounds(nint handle, ArrowRegion? region = null)
    {
        if (!GetClientRect(handle, out var rect) || !ClientToScreen(handle, ref rect)) return Drawing.Rectangle.Empty;
        // ClientToScreen converts the first POINT (left/top); right/bottom still hold client size.
        var r = region ?? new ArrowRegion(0, 0, 1, 1);
        return new(rect.Left + (int)Math.Round(rect.Right * r.X), rect.Top + (int)Math.Round(rect.Bottom * r.Y),
            (int)Math.Round(rect.Right * r.Width), (int)Math.Round(rect.Bottom * r.Height));
    }

    public static Frame? Capture(ArrowRegion? region = null)
    {
        using var frame = CapturePixels(region);
        if (frame == null) return null;
        // 仅手动校准窗口需要编码图片；实时识别直接传递独立拥有的像素矩阵。
        Cv.Cv2.ImEncode(".bmp", frame.Image, out var bytes);
        return new(bytes, frame.Bounds, frame.Handle);
    }

    public static PixelFrame? CapturePixels(ArrowRegion? region = null)
    {
        var handle = ForegroundGame();
        if (handle == 0) return null;
        var bounds = Bounds(handle, region);
        if (bounds.Width < 32 || bounds.Height < 32 || bounds.Width > 7680 || bounds.Height > 4320) return null;
        using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format24bppRgb);
        long captureStarted;
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            // 图像有效期从实际截图开始计，排除后台任务排队及截图准备时间。
            captureStarted = Environment.TickCount64;
            graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
        }
        if (ForegroundGame() != handle || bounds != Bounds(handle, region)) return null;
        var pixels = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), Drawing.Imaging.ImageLockMode.ReadOnly,
            Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var start = pixels.Stride < 0 ? IntPtr.Add(pixels.Scan0, (bitmap.Height - 1) * pixels.Stride) : pixels.Scan0;
            using var view = Cv.Mat.FromPixelData(bitmap.Height, bitmap.Width, Cv.MatType.CV_8UC3, start, Math.Abs(pixels.Stride));
            var owned = view.Clone();
            if (pixels.Stride < 0) Cv.Cv2.Flip(owned, owned, Cv.FlipMode.X);
            return new(owned, bounds, handle, captureStarted);
        }
        finally { bitmap.UnlockBits(pixels); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint id);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref NativeRect point);
}
