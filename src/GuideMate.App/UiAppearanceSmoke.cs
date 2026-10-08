using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    // WebView2 owns a native surface, so compose its capture with the WPF chrome for layout review.
    private async Task SaveAppearancePreviewAsync()
    {
        await Task.Delay(150);
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_root.ActualWidth), (int)Math.Ceiling(_root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        using var stream = new MemoryStream();
        await _browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        var browserImage = new BitmapImage();
        browserImage.BeginInit(); browserImage.CacheOption = BitmapCacheOption.OnLoad; browserImage.StreamSource = stream; browserImage.EndInit();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(browserImage, new Rect(_browser.TranslatePoint(new Point(), _root), new Size(_browser.ActualWidth, _browser.ActualHeight)));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(_dataPath, "main-fluent-preview.png"));
        encoder.Save(output);
    }
}
