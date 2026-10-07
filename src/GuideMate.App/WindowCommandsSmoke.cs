using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyWindowCommandsAsync(List<string> checks)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void CheckRestored(WindowState expected, string description) => Check(IsVisible && WindowState == expected
            && IsWindowVisible(new WindowInteropHelper(this).Handle), description);
        void CaptureTitle(string name)
        {
            UpdateLayout();
            var title = (Grid)_root.Children[0];
            var min = _minimizeButton.TranslatePoint(new Point(), title);
            var tray = _hideToTrayButton.TranslatePoint(new Point(), title);
            Check(tray.X >= 0 && tray.X + _hideToTrayButton.ActualWidth <= min.X
                && min.X + _minimizeButton.ActualWidth <= title.ActualWidth && _minimizeButton.ActualHeight >= 30,
                "separate tray and minimize buttons fit the title bar: " + name);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(title.ActualWidth), (int)Math.Ceiling(title.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(title), null, new Rect(0, 0, title.ActualWidth, title.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(_dataPath, name)); encoder.Save(output);
        }
        var overlayEnabled = _settings.SubtitleOverlay;
        var bounds = new Size(Width, Height);
        var navigationBefore = _navigationVersion;
        try
        {
            Check(AutomationProperties.GetName(_minimizeButton) == "最小化"
                && AutomationProperties.GetName(_hideToTrayButton) == "隐藏到托盘"
                && _minimizeButton.ToolTip?.ToString() == "最小化" && _hideToTrayButton.ToolTip?.ToString() == "隐藏到托盘",
                "title buttons have distinct accessible names and tooltips");
            _settings.SubtitleOverlay = true; UpdateOverlay();
            Check(_overlay?.IsVisible == true, "subtitle overlay enabled before minimize");
            var normal = new Rect(Left, Top, ActualWidth, ActualHeight);
            Click(_minimizeButton); await Task.Delay(150);
            Check(WindowState == WindowState.Minimized && IsVisible && ShowInTaskbar
                && IsIconic(new WindowInteropHelper(this).Handle), "minimize button iconifies native window without hiding its taskbar entry");
            UpdateOverlay();
            Check(_overlay?.IsVisible == false && _settings.SubtitleOverlay, "minimize hides overlay without disabling its setting");
            SaveSettings(); var saved = _store.Load();
            Check(Math.Abs(saved.Left - normal.Left) < 2 && Math.Abs(saved.Top - normal.Top) < 2
                && Math.Abs(saved.Width - normal.Width) < 2 && Math.Abs(saved.Height - normal.Height) < 2,
                "saving while minimized retains normal window geometry");
            WindowState = WindowState.Normal; await Task.Delay(150);
            CheckRestored(WindowState.Normal, "taskbar-style state restore shows the normal window");
            Check(_overlay?.IsVisible == true, "state restore automatically shows the enabled subtitle overlay");
            Click(_hideToTrayButton); await Task.Delay(100);
            Check(!IsVisible && !IsWindowVisible(new WindowInteropHelper(this).Handle)
                && _overlay?.IsVisible == false && _tray?.Visible == true, "tray button hides native main and overlay windows while tray icon remains");
            Click(_hideToTrayButton);
            Check(!IsVisible, "hide-to-tray command is idempotent rather than toggling visibility");
            ToggleHidden(); await Task.Delay(150);
            CheckRestored(WindowState.Normal, "hidden-window hotkey path restores from tray");
            Check(_overlay?.IsVisible == true, "tray restore shows the enabled subtitle overlay");
            WindowState = WindowState.Maximized; await Task.Delay(100);
            Click(_minimizeButton); await Task.Delay(100);
            ToggleHidden(); await Task.Delay(150);
            CheckRestored(WindowState.Maximized, "hotkey restore remembers maximized state before minimization");
            WindowState = WindowState.Normal; await Task.Delay(100);
            Click(_minimizeButton); HideToTray();
            EmergencyRestore(); await Task.Delay(150);
            CheckRestored(WindowState.Normal, "tray and emergency restore unminimize an already hidden window");
            Check(_overlay?.IsVisible == true && _settings.SubtitleOverlay, "emergency restore preserves overlay preference");
            Check(_navigationVersion == navigationBefore, $"window hide and restore preserve loaded page: navigation {navigationBefore} -> {_navigationVersion}");
            await CommandAsync("play"); await Task.Delay(300);
            var before = JsonSerializer.Deserialize<double>((await _videoRouter!.ExecuteAsync("document.querySelector('video').currentTime"))!);
            Click(_minimizeButton); await Task.Delay(500);
            var minimizedTime = JsonSerializer.Deserialize<double>((await _videoRouter.ExecuteAsync("document.querySelector('video').currentTime"))!);
            Check(minimizedTime > before + 0.2, "real sample video keeps playing while minimized");
            HideToTray(); await Task.Delay(500);
            var hiddenPaused = await _videoRouter.ExecuteAsync("document.querySelector('video').paused");
            Check(hiddenPaused is "true" or "false", "hidden-to-tray retains readable sample media without reloading the page");
            File.WriteAllText(Path.Combine(_dataPath, "hidden-media-state.json"), JsonSerializer.Serialize(new { paused = hiddenPaused == "true" }));
            RestoreMainWindow(); await CommandAsync("pause");
            CaptureTitle("window-buttons-normal.png");
            Width = 860; Height = 500; await Task.Delay(150);
            CaptureTitle("window-buttons-minimum.png");
        }
        finally
        {
            RestoreMainWindow(); WindowState = WindowState.Normal;
            Width = bounds.Width; Height = bounds.Height;
            _settings.SubtitleOverlay = overlayEnabled; UpdateOverlay();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
