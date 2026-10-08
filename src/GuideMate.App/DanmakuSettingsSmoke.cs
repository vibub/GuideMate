using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyDanmakuAppearanceAsync(List<string> checks)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            checks.Add(label);
        }
        IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T match) yield return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
        (double, double, double, double) Values(AppSettings value) =>
            (value.DanmakuDisplayArea, value.DanmakuOpacity, value.DanmakuFontScale, value.DanmakuSpeed);
        var original = Values(_settings);
        var hotkeys = JsonSerializer.Serialize(_settings.Hotkeys);
        var bookmarks = JsonSerializer.Serialize(_settings.Bookmarks);
        var opacity = _settings.GetImmersiveOpacity();
        var url = _url;
        var overlay = _danmakuOverlay!;
        SettingsWindow? dialog = null;
        try
        {
            dialog = new SettingsWindow(this, _settings, _keys!, (bindings, hold) => _keys!.Apply(bindings, hold));
            dialog.Show(); await Task.Delay(100);
            dialog.Width = 400;
            var names = new[] { "显示区域", "不透明度", "弹幕字号", "弹幕速度" };
            var controls = names.Select(name => Descendants<Slider>(dialog).Single(slider => AutomationProperties.GetName(slider) == name)).ToArray();
            var changed = new[] { 0.3, 0.5, 1.5, 1.75 };
            for (var i = 0; i < controls.Length; i++) controls[i].Value = changed[i];
            var scroll = Descendants<ScrollViewer>(dialog).First();
            var heading = Descendants<TextBlock>(dialog).Single(text => text.Text == "全屏弹幕");
            scroll.ScrollToVerticalOffset(heading.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 12);
            await Task.Delay(100);
            var root = (FrameworkElement)dialog.Content;
            foreach (var control in controls)
            {
                var point = control.TranslatePoint(new Point(), root);
                Check(point.X >= 0 && point.X + control.ActualWidth <= root.ActualWidth && control.ActualWidth > 100
                    && point.Y >= 0 && point.Y + control.ActualHeight < root.ActualHeight - 70,
                    "danmaku setting fits minimum dialog width and visible viewport: " + AutomationProperties.GetName(control));
            }
            var screenshot = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var white = new DrawingVisual();
            using (var drawing = white.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            screenshot.Render(white); screenshot.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using (var output = File.Create(Path.Combine(_dataPath, "settings-danmaku.png"))) encoder.Save(output);
            Check(Values(_settings) == original, "editing four sliders is pending until save");
            Descendants<Button>(dialog).Single(button => button.ToolTip?.ToString() == "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = null;
            Check(Values(_settings) == (0.3, 0.5, 1.5, 1.75), "saving applies all four danmaku preferences together");
            Check(Values(_store.Load()) == Values(_settings), "saved danmaku preferences survive settings reload");
            Check(JsonSerializer.Serialize(_settings.Hotkeys) == hotkeys && JsonSerializer.Serialize(_settings.Bookmarks) == bookmarks
                && _settings.GetImmersiveOpacity() == opacity && _url == url,
                "saving danmaku leaves custom bindings, bookmarks, window opacity and current page intact");
            var saved = Values(_settings);
            dialog = new SettingsWindow(this, _settings, _keys!, (_, _) => throw new Exception("Cancel invoked save"));
            dialog.Show(); await Task.Delay(100);
            foreach (var name in names) Descendants<Slider>(dialog).Single(slider => AutomationProperties.GetName(slider) == name).Value = 1;
            dialog.Close(); dialog = null;
            Check(Values(_settings) == saved && Values(_store.Load()) == saved, "closing without save discards all four slider edits");

            // Feed deterministic timestamps synchronously to the actual desktop surface;
            // no physical input or real-site behavior is inferred from these pixel checks.
            (Rect Bounds, byte Alpha) Render(double area = 1, double alpha = 1, double font = 1, double speed = 1, int mode = 5, double offset = 0)
            {
                overlay.Configure(area, alpha, font, speed); overlay.Clear();
                var item = new { id = "settings-probe", text = "弹幕测试", mode, color = 0xffffff, size = 25, offset };
                overlay.Update(JsonSerializer.SerializeToElement(new[] { item }), 10, true, 1);
                var surface = overlay.Surface; surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                var left = bitmap.PixelWidth; var top = bitmap.PixelHeight; var right = -1; var bottom = -1; byte maximum = 0;
                for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
                {
                    var a = pixels[(y * bitmap.PixelWidth + x) * 4 + 3];
                    if (a == 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); maximum = Math.Max(maximum, a);
                }
                return (right < 0 ? Rect.Empty : new Rect(left, top, right - left + 1, bottom - top + 1), maximum);
            }
            var normal = Render();
            var large = Render(font: 2);
            Check(!normal.Bounds.IsEmpty && large.Bounds.Width > normal.Bounds.Width * 1.8
                && large.Bounds.Height > normal.Bounds.Height * 1.7, "font scaling changes rendered glyph width and height");
            var translucent = Render(alpha: 0.5);
            Check(normal.Alpha > 240 && translucent.Alpha is >= 120 and <= 130, "half opacity halves rendered glyph and outline alpha");
            Check(Render(alpha: 0).Bounds.IsEmpty, "zero opacity produces a fully transparent danmaku surface");
            var bottomFull = Render(mode: 4);
            var bottomPartial = Render(area: 0.3, mode: 4);
            Check(!bottomPartial.Bounds.IsEmpty && bottomPartial.Bounds.Bottom <= overlay.Surface.ActualHeight * 0.3 + 1
                && bottomFull.Bounds.Top > overlay.Surface.ActualHeight * 0.8, "bottom fixed comments move inside the selected display region");
            var crowded = Enumerable.Range(0, 120).Select(i => new { id = "area-" + i, text = "滚动弹幕", mode = 1, size = 25, offset = 2 });
            overlay.Configure(0.3, 1, 1, 1); overlay.Clear(); overlay.Update(JsonSerializer.SerializeToElement(crowded), 10, true, 1);
            var areaRows = overlay.CommentCount;
            overlay.Configure(1, 1, 1, 1); overlay.Update(JsonSerializer.SerializeToElement(crowded), 10, true, 1);
            Check(areaRows > 0 && overlay.CommentCount > areaRows * 2, "display region limits scrolling lane capacity");
            Check(!Render(area: 0.1, font: 2).Bounds.IsEmpty, "minimum region still accepts maximum-size comments");
            var slow = Render(speed: 0.5, mode: 1, offset: 2);
            var fast = Render(speed: 2, mode: 1, offset: 2);
            Check(Math.Abs((slow.Bounds.Left - fast.Bounds.Left) - overlay.Surface.ActualWidth * 0.375) < 3,
                "scroll speed changes actual rendered position at the same media time");
            var reverseSlow = Render(speed: 0.5, mode: 6, offset: 2);
            var reverseFast = Render(speed: 2, mode: 6, offset: 2);
            Check(Math.Abs((reverseFast.Bounds.Left - reverseSlow.Bounds.Left) - overlay.Surface.ActualWidth * 0.375) < 3,
                "reverse comments use the configured scroll speed");
            var stationarySlow = Render(speed: 0.5, offset: 3);
            var stationaryFast = Render(speed: 2, offset: 3);
            Check(!stationarySlow.Bounds.IsEmpty && stationaryFast.Bounds.IsEmpty, "speed also adjusts fixed comment lifetime");
        }
        finally
        {
            dialog?.Close();
            (_settings.DanmakuDisplayArea, _settings.DanmakuOpacity, _settings.DanmakuFontScale, _settings.DanmakuSpeed) = original;
            ApplyDanmakuSettings(); overlay.Clear(); SaveSettings();
        }
    }
}
