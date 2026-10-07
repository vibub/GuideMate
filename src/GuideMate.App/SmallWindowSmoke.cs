using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task RunSmallWindowProbeAsync()
    {
        var checks = new List<string>();
        try
        {
            var end = DateTime.UtcNow.AddSeconds(25);
            while (_duration < 20 || _navigationVersion == 0) { if (DateTime.UtcNow > end) throw new Exception("Sample video failed to load"); await Task.Delay(100); }
            await VerifySmallWindowAsync(checks);
            await VerifyImmersiveFeedbackAsync(checks);
            WriteSmokeResult(true, "", checks);
        }
        catch (Exception ex) { WriteSmokeResult(false, ex.ToString(), checks); }
        finally { Close(); }
    }

    private async Task VerifySmallWindowAsync(List<string> checks)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        async Task WaitFor(Func<bool> condition, string description)
        {
            var end = DateTime.UtcNow.AddSeconds(10);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception("Timeout: " + description); await Task.Delay(50); }
            checks.Add(description);
        }
        IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T match) yield return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
        RenderTargetBitmap Capture(string name)
        {
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_root.ActualWidth), (int)Math.Ceiling(_root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(_dataPath, name)); encoder.Save(output);
            return bitmap;
        }
        byte[] Pixel(BitmapSource bitmap, int x, int y)
        {
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
        }
        var opacity = _settings.Opacity;
        var immersiveOpacity = _settings.ImmersiveOpacity;
        var xray = _settings.XRayEnabled; var radius = _settings.XRayRadius;
        var hotkeys = JsonSerializer.Serialize(_settings.Hotkeys);
        var url = _url; var navigation = _navigationVersion;
        SettingsWindow? dialog = null;
        try
        {
            _settings.Opacity = 0.8; _settings.ImmersiveOpacity = 0.55;
            ApplySmallWindowSettings();
            Check(Opacity == 0.8, "small-window opacity does not alter normal window opacity");
            dialog = new SettingsWindow(this, _settings, _keys!, (bindings, hold) =>
            {
                Check(JsonSerializer.Serialize(bindings) == hotkeys, "saving appearance leaves all hotkey recorder values unchanged");
                return _keys!.Apply(bindings, hold);
            });
            dialog.Show(); await Task.Delay(100);
            var sliders = Descendants<Slider>(dialog).ToArray();
            var opacityControl = sliders.Single(s => AutomationProperties.GetName(s) == "小窗透明度");
            var radiusControl = sliders.Single(s => AutomationProperties.GetName(s) == "X 光半径");
            var xrayControl = Descendants<CheckBox>(dialog).Single(c => c.Content?.ToString() == "鼠标 X 光");
            opacityControl.Value = 0.35; radiusControl.Value = 80; xrayControl.IsChecked = true;
            dialog.Width = 400;
            Descendants<ScrollViewer>(dialog).First().ScrollToVerticalOffset(470);
            await Task.Delay(100);
            var settingsRoot = (FrameworkElement)dialog.Content;
            foreach (var control in new FrameworkElement[] { opacityControl, radiusControl, xrayControl })
            {
                var point = control.TranslatePoint(new Point(), settingsRoot);
                Check(point.X >= 0 && point.X + control.ActualWidth <= settingsRoot.ActualWidth && control.ActualWidth > 100,
                    "small-window setting control fits minimum dialog width: " + AutomationProperties.GetName(control));
            }
            var settingsImage = new RenderTargetBitmap((int)Math.Ceiling(settingsRoot.ActualWidth), (int)Math.Ceiling(settingsRoot.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var settingsVisual = new DrawingVisual();
            using (var context = settingsVisual.RenderOpen())
            {
                var bounds = new Rect(0, 0, settingsRoot.ActualWidth, settingsRoot.ActualHeight);
                context.DrawRectangle(Brushes.White, null, bounds);
            }
            settingsImage.Render(settingsVisual);
            settingsImage.Render(settingsRoot);
            var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsImage));
            using (var output = File.Create(Path.Combine(_dataPath, "settings-small-window.png"))) settingsEncoder.Save(output);
            Check(_settings.ImmersiveOpacity == 0.55 && _settings.XRayEnabled == xray && _settings.XRayRadius == radius,
                "small-window setting edits remain pending until save");
            var save = Descendants<Button>(dialog).Single(b => b.ToolTip?.ToString() == "保存设置");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dialog = null;
            Check(Math.Abs(_settings.GetImmersiveOpacity() - 0.65) < 0.001 && _settings.XRayEnabled && _settings.XRayRadius == 80,
                "settings save applies opacity, X-ray toggle and radius together");
            var persisted = _store.Load();
            Check(persisted.ImmersiveOpacity == _settings.ImmersiveOpacity && persisted.XRayEnabled && persisted.XRayRadius == 80
                && JsonSerializer.Serialize(persisted.Hotkeys) == hotkeys, "new appearance settings persist without resetting hotkeys");
            dialog = new SettingsWindow(this, _settings, _keys!, (_, _) => throw new Exception("Cancel invoked save"));
            dialog.Show(); await Task.Delay(100);
            Descendants<Slider>(dialog).Single(s => AutomationProperties.GetName(s) == "小窗透明度").Value = 0.7;
            Descendants<CheckBox>(dialog).Single(c => c.Content?.ToString() == "鼠标 X 光").IsChecked = false;
            dialog.Close(); dialog = null;
            Check(Math.Abs(_settings.GetImmersiveOpacity() - 0.65) < 0.001 && _settings.XRayEnabled,
                "closing settings without save discards opacity and X-ray edits");
            ToggleImmersive(); await Task.Delay(250);
            Check(Math.Abs(Opacity - 0.65) < 0.001 && _xrayTimer.IsEnabled && _root.Background == Brushes.Transparent,
                "immersive uses separate opacity and starts bounded cursor tracking on a transparent root");
            _faded = true; ApplyWindowOpacity();
            Check(Opacity == 0.25, "combat fade composes with immersive opacity");
            _faded = false; ApplyWindowOpacity();
            Check(Math.Abs(Opacity - 0.65) < 0.001, "ending fade restores immersive opacity rather than normal opacity");
            _xrayTimer.Stop();
            var center = new Point(_browser.ActualWidth / 2, _browser.ActualHeight / 2);
            UpdateXRayPointer(center);
            Check(_videoSurface.OpacityMask == _xrayMask && _xrayMask.RadiusX == 80 && _xrayMask.RadiusY == 80
                && _xrayMask.GradientStops[0].Color.A == 0 && _xrayMask.GradientStops[^1].Color.A == 255,
                "X-ray uses a radius-80 circular opacity mask with transparent core and opaque exterior");
            var oldMask = _videoSurface.OpacityMask;
            UpdateXRayPointer(center + new Vector(20, 5));
            Check(ReferenceEquals(oldMask, _videoSurface.OpacityMask) && _xrayMask.Center == center + new Vector(20, 5)
                && _xrayMask.GradientOrigin == _xrayMask.Center,
                "moving X-ray reuses its gradient mask instead of rebuilding one per frame");
            var screen = _browser.PointToScreen(center);
            var roundtrip = _browser.PointFromScreen(screen);
            Check((roundtrip - center).Length < 0.01, "physical screen coordinates roundtrip through the current WPF DPI transform");
            Check(ReadXRayCursor(out _), "native cursor sampling is available without injecting mouse input");
            SampleXRayCursor();
            UpdateXRayPointer(new Point(-1, 100));
            Check(_videoSurface.OpacityMask == null, "X-ray disappears only when pointer leaves the actual small-window area");
            UpdateXRayPointer(_dragHandle.TranslatePoint(new Point(10, 10), _browser));
            Check(_videoSurface.OpacityMask == null, "X-ray keeps drag handle accessible");
            UpdateXRayPointer(new Point(center.X, _browser.ActualHeight - 7));
            Check(_videoSurface.OpacityMask == null, "X-ray keeps bottom seek target and resize edges accessible");
            UpdateXRayPointer(new Point(6, 100));
            Check(_videoSurface.OpacityMask != null && _xrayMask.Center == new Point(6, 100), "X-ray mask follows pointer near the window edge");
            UpdateXRayPointer(center);
            await Task.Delay(100);
            var image = Capture("small-window-xray.png");
            Check(Pixel(image, (int)center.X, (int)center.Y)[3] == 0,
                "WPF rendered X-ray center has zero alpha, revealing the underlying desktop");
            var featherAlpha = new[] { 56, 62, 68, 74, 81 }.Select(distance =>
                Pixel(image, (int)center.X + distance, (int)center.Y)[3]).ToArray();
            Check(featherAlpha[0] <= 8 && featherAlpha[1] > 10 && featherAlpha[2] > featherAlpha[1]
                && featherAlpha[3] > featherAlpha[2] && featherAlpha[3] < 250 && featherAlpha[4] == 255,
                "real video pixels feather gradually from transparent core to opaque outside the saved radius");
            Check(Pixel(image, (int)center.X, (int)center.Y + 68)[3] is > 50 and < 220
                && Pixel(image, (int)center.X + 48, (int)center.Y + 48)[3] is > 50 and < 220,
                "feather opacity is circular on vertical and diagonal samples, not stretched to window aspect ratio");
            var featherPixel = Pixel(image, (int)center.X + 68, (int)center.Y);
            Check(featherPixel[0] > 20, "semi-transparent feather retains video color rather than adding a black rim");
            Check(Pixel(image, (int)center.X + 100, (int)center.Y)[3] > 0,
                "real composition browser outside the X-ray remains rendered");
            var videoPixel = Pixel(image, (int)center.X + 100, (int)center.Y);
            Check(videoPixel.Take(3).Sum(v => (int)v) > 30, "X-ray rendering preserves nonblank real sample video pixels");
            Check(ProgressFraction(5, 10) == 0.5 && ProgressFraction(-1, 10) == 0 && ProgressFraction(11, 10) == 1
                && ProgressFraction(3, 0) == 0 && ProgressFraction(double.NaN, 1) == 0 && ProgressFraction(1, double.PositiveInfinity) == 0,
                "edge progress normalizes, clamps and ignores invalid or unknown duration");
            await CommandAsync("pause"); await CommandAsync("position", _duration / 4);
            await WaitFor(() => _paused && Math.Abs(_position - _duration / 4) < 0.2, "real video reports a quarter-length position");
            UpdateLayout(); UpdateEdgeProgress();
            Check(_edgeProgress.IsVisible && _edgeProgress.ActualHeight == 14 && _edgePlayed.Height == 3
                && Math.Abs(_edgePlayed.Width - _edgeProgress.ActualWidth / 4) < 3,
                "constant thin bottom edge highlights played prefix using actual video state");
            ShowEdgeTime(_edgeProgress.ActualWidth / 2);
            Check(_edgeTime.IsVisible && _edgeTime.Text == FormatTime(_duration / 2) + " / " + FormatTime(_duration),
                "edge hover time uses pointer position without changing the video");
            _edgeSeeking = true; PreviewEdgeSeek(_edgeProgress.ActualWidth / 2);
            Check(Math.Abs(_edgePlayed.Width - _edgeProgress.ActualWidth / 2) < 1,
                "drag seek previews target in the bottom edge without page command spam");
            var seekPosition = _edgeSeekPosition; CancelEdgeSeek(); await CommandAsync("position", seekPosition);
            await WaitFor(() => Math.Abs(_position - _duration / 2) < 0.2, "bottom-edge seek command changes the real HTML5 video");
            Check(!_edgeTime.IsVisible, "seek completion hides hover timestamp without hiding thin edge progress");
            _videoSurface.OpacityMask = null;
            var progressImage = Capture("small-window-progress.png");
            var playedPixel = Pixel(progressImage, progressImage.PixelWidth / 4, progressImage.PixelHeight - 2);
            var remainingPixel = Pixel(progressImage, progressImage.PixelWidth * 3 / 4, progressImage.PixelHeight - 2);
            Check(playedPixel[1] > 180 && playedPixel[2] < 100 && remainingPixel[1] < 50,
                "bottom-edge rendered pixels visibly distinguish played highlight from remaining track");
            var size = new Size(Width, Height);
            Width = 320; Height = 180; await Task.Delay(100);
            Check(_edgeProgress.ActualWidth >= 319 && _edgeProgress.ActualHeight == 14
                && Math.Abs(_edgePlayed.Width - _edgeProgress.ActualWidth / 2) < 2,
                "minimum small-window dimensions keep proportional progress and stable hit target");
            Capture("small-window-minimum.png");
            Width = size.Width; Height = size.Height; await Task.Delay(100);
            OnHotkey("Hide");
            Check(!IsVisible && !_xrayTimer.IsEnabled && _videoSurface.OpacityMask == null, "configured Hide action hides immersive window and stops X-ray polling");
            OnHotkey("Hide"); await Task.Delay(150);
            Check(IsVisible && _immersive && _xrayTimer.IsEnabled && Math.Abs(Opacity - 0.65) < 0.001
                && _edgeProgress.IsVisible && _settings.XRayEnabled && _url == url && _navigationVersion == navigation,
                "Hide action restores immersive mode, appearance, progress and same loaded page: " + JsonSerializer.Serialize(new
                { IsVisible, _immersive, xrayTimer = _xrayTimer.IsEnabled, Opacity, progress = _edgeProgress.IsVisible,
                    xray = _settings.XRayEnabled, expectedUrl = url, _url, expectedNavigation = navigation, _navigationVersion }));
            _edgeSeeking = true; WindowState = WindowState.Minimized;
            Check(!_xrayTimer.IsEnabled && !_edgeSeeking, "minimize stops polling and cancels an unfinished edge seek");
            WindowState = WindowState.Normal; await Task.Delay(100);
            Check(_xrayTimer.IsEnabled, "restoring minimized immersive window resumes X-ray tracking");
            _settings.XRayEnabled = false; ApplySmallWindowSettings();
            Check(!_xrayTimer.IsEnabled && _videoSurface.OpacityMask == null, "disabling X-ray removes hole and cursor timer immediately");
            _settings.XRayEnabled = true; ApplySmallWindowSettings();
            ToggleImmersive(); await Task.Delay(150);
            Check(Opacity == 0.8 && !_xrayTimer.IsEnabled && _videoSurface.OpacityMask == null && !_edgeProgress.IsVisible,
                "leaving immersive restores normal opacity and removes all small-window effects");
            Check(JsonSerializer.Serialize(_settings.Hotkeys) == hotkeys, "all saved bindings remain unchanged after small-window commands");
            OnWebMessage("{\"type\":\"no-video\"}");
            Check(_edgePlayed.Width == 0 && !_edgeProgress.IsEnabled, "no-video state clears played prefix and disables seeking");
            await WaitFor(() => _duration > 20, "real bridge state restores progress after synthetic no-video check");
        }
        finally
        {
            dialog?.Close();
            RestoreMainWindow(); WindowState = WindowState.Normal;
            if (_immersive) ToggleImmersive();
            _settings.Opacity = opacity; _settings.ImmersiveOpacity = immersiveOpacity;
            _settings.XRayEnabled = xray; _settings.XRayRadius = radius;
            ApplySmallWindowSettings(); SaveSettings();
        }
    }
}
