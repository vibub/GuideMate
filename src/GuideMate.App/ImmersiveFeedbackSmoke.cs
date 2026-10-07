using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyImmersiveFeedbackAsync(List<string> checks)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            checks.Add(label);
        }
        async Task Wait(Func<bool> condition, string label)
        {
            var end = DateTime.UtcNow.AddSeconds(8);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception(label + "; feedback=" + _hotkeyFeedbackText.Text); await Task.Delay(40); }
            checks.Add(label);
        }
        bool Says(string text) => _hotkeyFeedback.Visibility == Visibility.Visible && _hotkeyFeedbackText.Text == text;
        var seek = _settings.SeekSeconds;
        var rate = _settings.Rate;
        var temporaryRate = _settings.TemporaryRate;
        try
        {
            ShowHotkeyFeedback("不应显示", _hotkeyFeedbackVersion);
            Check(_hotkeyFeedback.Visibility == Visibility.Collapsed && !_immersiveHoverTimer.IsEnabled,
                "normal mode does not show immersive feedback or poll hover");
            ToggleImmersive(); await Task.Delay(300);
            _immersiveHoverTimer.Stop();
            UpdateImmersiveHover(new Point(-1, -1)); UpdateLayout();
            Check(_dragHandle.Visibility == Visibility.Hidden && _dragHandle.ActualWidth == 34,
                "outside pointer hides the grip while preserving its layout size");
            UpdateImmersiveHover(new Point(100, 100));
            Check(_dragHandle.IsVisible, "pointer anywhere inside the small window reveals drag grip");
            Check(!_hotkeyFeedback.IsHitTestVisible, "hotkey badge never intercepts video or drag input");
            _dragHandle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Check(_dragHandle.IsDragging && _dragHandle.IsMouseCaptured, "Thumb begins its actual WPF drag-capture lifecycle");
            UpdateImmersiveHover(new Point(-100, -100));
            Check(_dragHandle.IsVisible && _dragHandle.IsDragging, "drag capture keeps grip visible outside window");
            _dragHandle.CancelDrag(); UpdateImmersiveHover(new Point(-100, -100));
            Check(_dragHandle.Visibility == Visibility.Hidden && !_dragHandle.IsMouseCaptured,
                "drag release hides grip again outside window without losing capture cleanup");

            await CommandAsync("pause"); await CommandAsync("position", 10);
            await Wait(() => _paused && Math.Abs(_position - 10) < 0.3, "feedback fixture paused at known time");
            _settings.SeekSeconds = 5;
            OnHotkey("SeekForward"); await Wait(() => Says("快进5s") && Math.Abs(_position - 15) < 0.3,
                "seek-forward hotkey shows its saved 5-second step after real video execution");
            Check(_hotkeyFeedback.Margin.Left == 8, "badge uses the top-left corner while grip is hidden");
            UpdateImmersiveHover(new Point(100, 100)); UpdateLayout();
            Check(_hotkeyFeedback.Margin.Left >= _dragHandle.Margin.Left + _dragHandle.ActualWidth + 8,
                "badge moves right to avoid visible drag grip");
            OnHotkey("SeekBack"); await Wait(() => Says("后退5s") && Math.Abs(_position - 10) < 0.3,
                "seek-back hotkey shows actual configured step");
            _settings.SeekSeconds = 7.5;
            OnHotkey("SeekForward"); await Wait(() => Says("快进7.5s") && Math.Abs(_position - 17.5) < 0.3,
                "fractional custom seek setting is displayed without resetting it");
            OnHotkey("PlayPause"); await Wait(() => Says("播放") && !_paused, "play hotkey reads resulting player state");
            OnHotkey("PlayPause"); await Wait(() => Says("暂停") && _paused, "pause hotkey reads resulting player state");
            OnHotkey("PlayPause"); OnHotkey("PlayPause");
            await Wait(() => Says("暂停") && _paused, "rapid toggles retain only the latest confirmed feedback");
            await SetRateAsync(1.5); await Task.Delay(300);
            OnHotkey("RateUp"); await Wait(() => Says("倍速1.75x"), "rate-up feedback reports applied speed");
            OnHotkey("RateDown"); await Wait(() => Says("倍速1.5x"), "rate-down feedback reports applied speed");
            _settings.TemporaryRate = 2;
            OnHotkey("TemporaryRate"); await Wait(() => Says("临时倍速2x") && _temporaryRateActive,
                "hold-dispatched temporary rate has dedicated feedback");
            await EndTemporaryRateAsync(true); await Wait(() => Says("恢复倍速1.5x") && !_temporaryRateActive,
                "hold release reports restored original speed");
            OnHotkey("TemporaryRate"); await Wait(() => Says("临时倍速2x"), "second temporary hold begins");
            OnHotkey("SeekBack"); await Wait(() => Says("后退7.5s"), "new action replaces temporary-rate feedback");
            await EndTemporaryRateAsync(true);
            Check(Says("后退7.5s"), "late hold restore does not overwrite a newer action message");
            if (_keys?.EmergencyAvailable == true)
            {
                OnHotkey("ClickThrough"); Check(Says("开启鼠标穿透") && _through, "click-through hotkey confirms enabled state");
                OnHotkey("ClickThrough"); Check(Says("关闭鼠标穿透") && !_through, "click-through hotkey confirms disabled state");
            }

            OnHotkey("NextEpisode"); await Wait(() => _hotkeyFeedback.Visibility == Visibility.Visible && _hotkeyFeedbackText.Text.Contains("未找到可用"),
                "unavailable episode action shows failure instead of a success message");
            await _browser.CoreWebView2.ExecuteScriptAsync("window.feedbackSavedVideo=document.querySelector('video'); feedbackSavedVideo.remove()");
            OnHotkey("PlayPause"); await Wait(() => Says(MissingVideoNotice), "missing video does not show a false play/pause result");
            await _browser.CoreWebView2.ExecuteScriptAsync("document.body.append(feedbackSavedVideo)");
            await Wait(() => _duration > 20, "same video is restored after missing-media feedback check");

            var version = ++_hotkeyFeedbackVersion;
            ShowHotkeyFeedback("第一次", version); await Task.Delay(700);
            version = ++_hotkeyFeedbackVersion;
            ShowHotkeyFeedback("第二次", version); await Task.Delay(800);
            Check(Says("第二次"), "repeated feedback restarts its full display duration");
            await Wait(() => _hotkeyFeedback.Visibility == Visibility.Collapsed && !_hotkeyFeedbackTimer.IsEnabled,
                "badge automatically clears after its transient duration");
            UpdateImmersiveHover(new Point(100, 100));
            ShowHotkeyFeedback("快进5s", _hotkeyFeedbackVersion); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_root.ActualWidth), (int)Math.Ceiling(_root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(_dataPath, "immersive-hotkey-feedback.png"))) encoder.Save(output);
            OnHotkey("Hide");
            Check(!IsVisible && _hotkeyFeedback.Visibility == Visibility.Collapsed && !_immersiveHoverTimer.IsEnabled && !_hotkeyFeedbackTimer.IsEnabled,
                "hide stops hover and clears feedback");
            OnHotkey("Hide"); await Task.Delay(150);
            Check(IsVisible && Says("已恢复") && _immersiveHoverTimer.IsEnabled, "restore hotkey resumes hover and shows restored notice");
            WindowState = WindowState.Minimized;
            Check(_hotkeyFeedback.Visibility == Visibility.Collapsed && !_immersiveHoverTimer.IsEnabled,
                "minimize clears feedback and stops hover");
            RestoreMainWindow();
            Check(_immersiveHoverTimer.IsEnabled, "unminimize restarts hover without changing immersion");
            ToggleImmersive();
            Check(_dragHandle.Visibility == Visibility.Collapsed && _hotkeyFeedback.Visibility == Visibility.Collapsed
                && !_immersiveHoverTimer.IsEnabled && !_hotkeyFeedbackTimer.IsEnabled,
                "leaving immersion clears controls and all feedback timers");
        }
        finally
        {
            await EndTemporaryRateAsync();
            if (_through) SetClickThrough(false);
            _dragHandle.CancelDrag();
            if (_immersive) ToggleImmersive();
            _settings.SeekSeconds = seek; _settings.TemporaryRate = temporaryRate;
            await SetRateAsync(rate);
            ClearHotkeyFeedback();
        }
    }
}
