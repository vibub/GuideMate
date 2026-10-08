using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyWindowSwitcherAsync(List<string> checks)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        void CheckSwitcher(Window window, bool hidden, string description)
        {
            var style = GetWindowLongPtrW(new WindowInteropHelper(window).Handle, -20).ToInt64();
            Check(window.ShowInTaskbar == !hidden && ((style & 0x80) != 0) == hidden
                && ((style & 0x40000) != 0) == !hidden, description);
        }
        IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T match) yield return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
        SettingsWindow OpenSettings(bool saveAllowed)
        {
            var settings = new SettingsWindow(this, _settings, _keys!, (bindings, hold) =>
                saveAllowed ? _keys!.Apply(bindings, hold) : throw new Exception("Cancel invoked save"));
            settings.Show();
            return settings;
        }
        CheckBox Toggle(SettingsWindow dialog) => Descendants<CheckBox>(dialog)
            .Single(control => AutomationProperties.GetName(control) == "在 Alt+Tab 中隐藏沉浸小窗");
        void Save(SettingsWindow dialog) => Descendants<Button>(dialog)
            .Single(button => button.ToolTip?.ToString() == "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var original = _settings.HideImmersiveFromAltTab;
        var overlayEnabled = _settings.SubtitleOverlay;
        var hotkeys = JsonSerializer.Serialize(_settings.Hotkeys);
        var bookmarks = JsonSerializer.Serialize(_settings.Bookmarks);
        var opacity = _settings.GetImmersiveOpacity();
        var xray = (_settings.XRayEnabled, _settings.XRayRadius);
        var url = _url; var navigation = _navigationVersion;
        var handle = new WindowInteropHelper(this).Handle;
        var browser = _browser.CoreWebView2;
        SettingsWindow? dialog = null;
        try
        {
            _settings.HideImmersiveFromAltTab = true; ApplySmallWindowSettings(); SaveSettings();
            CheckSwitcher(this, false, "normal main window retains native Alt+Tab and taskbar styles");
            var foreground = NativeHotkeys.CurrentForegroundWindow;
            _settings.SubtitleOverlay = true; UpdateOverlay();
            Check(_overlay?.Owner == null && _overlay?.IsVisible == true, "subtitle is tested as a visible unowned desktop window");
            CheckSwitcher(_overlay!, true, "unowned subtitle overlay is a tool window without an app-window entry");
            Check(NativeHotkeys.CurrentForegroundWindow == foreground, "showing subtitle overlay does not take foreground focus");

            ToggleImmersive(); await Task.Delay(200);
            CheckSwitcher(this, true, "saved exclusion hides immersive window from native task switching");
            var otherStyles = GetWindowLongPtrW(handle, -20).ToInt64() & ~0x40080L;
            dialog = OpenSettings(true); await Task.Delay(100);
            Check(Toggle(dialog).IsChecked == true, "settings toggle shows saved Alt+Tab exclusion");
            dialog.Width = 400;
            Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "转到小窗设置")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100); dialog.UpdateLayout();
            var root = (FrameworkElement)dialog.Content;
            var control = Toggle(dialog);
            var point = control.TranslatePoint(new Point(), root);
            Check(point.X >= 0 && point.X + control.ActualWidth <= root.ActualWidth
                && point.Y >= 0 && point.Y + control.ActualHeight < root.ActualHeight - 70,
                "Alt+Tab toggle fits minimum settings width and small-window viewport");
            var screenshot = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
                (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var white = new DrawingVisual();
            using (var drawing = white.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            screenshot.Render(white); screenshot.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using (var output = File.Create(Path.Combine(_dataPath, "settings-window-switcher.png"))) encoder.Save(output);
            control.IsChecked = false;
            Check(_settings.HideImmersiveFromAltTab && _store.Load().HideImmersiveFromAltTab,
                "editing Alt+Tab toggle remains pending until save");
            CheckSwitcher(this, true, "pending settings do not change native immersive styles");
            Save(dialog); dialog = null; await Task.Delay(150);
            Check(!_settings.HideImmersiveFromAltTab && !_store.Load().HideImmersiveFromAltTab,
                "saving opt-out applies immediately and persists");
            CheckSwitcher(this, false, "saving opt-out restores immersive Alt+Tab and taskbar entry");
            Check((GetWindowLongPtrW(handle, -20).ToInt64() & ~0x40080L) == otherStyles,
                "switcher preference preserves layered, topmost and other extended window styles");
            CheckSwitcher(_overlay!, true, "subtitle remains excluded when immersive opt-out is saved");

            dialog = OpenSettings(false); await Task.Delay(100);
            Toggle(dialog).IsChecked = true;
            dialog.Close(); dialog = null;
            Check(!_settings.HideImmersiveFromAltTab && !_store.Load().HideImmersiveFromAltTab,
                "cancel discards Alt+Tab draft without changing saved preference");
            CheckSwitcher(this, false, "cancel leaves immersive window in native task switching");

            dialog = OpenSettings(true); await Task.Delay(100);
            Toggle(dialog).IsChecked = true; Save(dialog); dialog = null; await Task.Delay(150);
            CheckSwitcher(this, true, "saving exclusion while immersive removes the native switcher entry");
            Check(_store.Load().HideImmersiveFromAltTab, "re-enabled exclusion persists across settings reload");
            if (_keys!.EmergencyAvailable)
            {
                SetClickThrough(true);
                CheckSwitcher(this, true, "click-through keeps immersive Alt+Tab exclusion");
                CheckSwitcher(_overlay!, true, "click-through keeps subtitle Alt+Tab exclusion");
                Check(NativeHotkeys.IsClickThrough(this) && NativeHotkeys.IsClickThrough(_overlay!), "both windows retain click-through flags");
                _settings.HideImmersiveFromAltTab = false; ApplySmallWindowSettings();
                CheckSwitcher(this, false, "changing switcher style while click-through restores app-window entry");
                Check(NativeHotkeys.IsClickThrough(this), "switcher changes preserve existing click-through behavior");
                _settings.HideImmersiveFromAltTab = true; ApplySmallWindowSettings();
                SetClickThrough(false);
                CheckSwitcher(this, true, "disabling click-through keeps immersive Alt+Tab exclusion");
                CheckSwitcher(_overlay!, true, "disabling click-through keeps subtitle Alt+Tab exclusion");
            }
            ToggleHidden(); await Task.Delay(100);
            Check(!IsVisible && _overlay?.IsVisible == false, "hide hotkey path still hides both excluded windows");
            ToggleHidden(); await Task.Delay(150);
            Check(IsVisible && _immersive && _overlay?.IsVisible == true, "hide hotkey path restores immersive and subtitle windows");
            CheckSwitcher(this, true, "hide and restore retain immersive switcher exclusion");
            CheckSwitcher(_overlay!, true, "hide and restore retain subtitle switcher exclusion");
            WindowState = WindowState.Minimized; await Task.Delay(100);
            ToggleHidden(); await Task.Delay(150);
            Check(WindowState == WindowState.Normal && _immersive, "hotkey restore unminimizes excluded immersive window");
            CheckSwitcher(this, true, "minimize and restore retain immersive switcher exclusion");
            EmergencyRestore(); await Task.Delay(150);
            CheckSwitcher(this, false, "emergency restore returns normal main window to Alt+Tab and taskbar");
            CheckSwitcher(_overlay!, true, "emergency restore leaves subtitle excluded");
            Check(new WindowInteropHelper(this).Handle == handle && ReferenceEquals(_browser.CoreWebView2, browser)
                && _url == url && _navigationVersion == navigation, "all switcher transitions preserve window handle, browser and loaded page");
            Check(JsonSerializer.Serialize(_settings.Hotkeys) == hotkeys && JsonSerializer.Serialize(_settings.Bookmarks) == bookmarks
                && _settings.GetImmersiveOpacity() == opacity && (_settings.XRayEnabled, _settings.XRayRadius) == xray,
                "saving switcher preference preserves custom hotkeys, bookmarks, opacity and X-ray settings");
        }
        finally
        {
            dialog?.Close();
            SetClickThrough(false); RestoreMainWindow(); WindowState = WindowState.Normal;
            if (_immersive) ToggleImmersive();
            _settings.HideImmersiveFromAltTab = original; _settings.SubtitleOverlay = overlayEnabled;
            ApplySmallWindowSettings(); UpdateOverlay(); SaveSettings();
        }
    }
}
