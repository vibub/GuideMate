using System.Diagnostics;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace GuideMate.App;

internal static class GameMapCapture
{
    internal sealed record Frame(byte[] Image, Drawing.Rectangle Bounds, nint Handle);

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
        var handle = ForegroundGame();
        if (handle == 0) return null;
        var bounds = Bounds(handle, region);
        if (bounds.Width < 32 || bounds.Height < 32 || bounds.Width > 7680 || bounds.Height > 4320) return null;
        using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
        if (ForegroundGame() != handle || bounds != Bounds(handle, region)) return null;
        using var stream = new MemoryStream(); bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        return new(stream.ToArray(), bounds, handle);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint id);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref NativeRect point);
}
