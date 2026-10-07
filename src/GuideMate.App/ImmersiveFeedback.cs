using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly TextBlock _hotkeyFeedbackText = new()
    {
        FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Border _hotkeyFeedback = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new(8, 8, 8, 0), Padding = new(10, 6, 10, 6), CornerRadius = new(4),
        Background = new SolidColorBrush(Color.FromArgb(220, 24, 28, 30)),
        IsHitTestVisible = false, Visibility = Visibility.Collapsed, MaxWidth = 250
    };
    private readonly DispatcherTimer _hotkeyFeedbackTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly DispatcherTimer _immersiveHoverTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(100) };
    private int _hotkeyFeedbackVersion, _temporaryFeedbackVersion = -1;

    private bool ImmersiveControlsActive => _immersive && IsVisible && WindowState != WindowState.Minimized && !_closing;

    private void BuildImmersiveFeedback()
    {
        _hotkeyFeedback.Child = _hotkeyFeedbackText;
        System.Windows.Automation.AutomationProperties.SetName(_hotkeyFeedback, "热键操作提示");
        Panel.SetZIndex(_hotkeyFeedback, 30); Panel.SetZIndex(_dragHandle, 20);
        _workspace.Children.Add(_hotkeyFeedback);
        _hotkeyFeedbackTimer.Tick += (_, _) => ClearHotkeyFeedback();
        _immersiveHoverTimer.Tick += (_, _) => SampleImmersiveCursor();
        _workspace.MouseEnter += (_, _) => SampleImmersiveCursor();
        _workspace.MouseLeave += (_, _) => SampleImmersiveCursor();
        _dragHandle.DragStarted += (_, _) => UpdateImmersiveHover(null);
    }

    private void UpdateImmersiveControls()
    {
        if (ImmersiveControlsActive)
        {
            _immersiveHoverTimer.Start();
            SampleImmersiveCursor();
        }
        else
        {
            _immersiveHoverTimer.Stop(); ClearHotkeyFeedback();
            UpdateImmersiveHover(null);
        }
    }

    private void SampleImmersiveCursor()
    {
        // Native coordinates also work over WebView2, click-through and transparent X-ray pixels.
        UpdateImmersiveHover(ImmersiveControlsActive && ReadXRayCursor(out var cursor)
            ? _workspace.PointFromScreen(new(cursor.X, cursor.Y)) : null);
    }

    private void UpdateImmersiveHover(Point? pointer)
    {
        var show = ImmersiveControlsActive && (_dragHandle.IsDragging || pointer is { } point
            && new Rect(0, 0, _workspace.ActualWidth, _workspace.ActualHeight).Contains(point));
        // Hidden keeps the grip's geometry stable for layout and the existing X-ray exclusion.
        _dragHandle.Visibility = !_immersive ? Visibility.Collapsed : show ? Visibility.Visible : Visibility.Hidden;
        _hotkeyFeedback.Margin = new(show ? 50 : 8, 8, 8, 0);
    }

    private void ClearHotkeyFeedback()
    {
        _hotkeyFeedbackVersion++;
        _hotkeyFeedbackTimer.Stop();
        _hotkeyFeedback.Visibility = Visibility.Collapsed;
        _hotkeyFeedbackText.Text = "";
    }

    private void ShowHotkeyFeedback(string text, int version)
    {
        if (!ImmersiveControlsActive || version != _hotkeyFeedbackVersion) return;
        _hotkeyFeedbackText.Text = text;
        _hotkeyFeedback.Visibility = Visibility.Visible;
        _hotkeyFeedbackTimer.Stop(); _hotkeyFeedbackTimer.Start();
    }

    private static string FeedbackNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private async Task VideoHotkeyAsync(string action, double? value, string text, int version)
    {
        var success = await CommandAsync(action, value);
        if (!ImmersiveControlsActive || version != _hotkeyFeedbackVersion) return;
        if (success && action == "toggle")
        {
            var state = await ReadPlaybackFeedbackAsync();
            if (state is not { } playback) success = false;
            else text = playback.Paused ? "暂停" : "播放";
        }
        ShowHotkeyFeedback(success ? text : _notice.Length > 0 ? _notice : "操作失败", version);
    }

    private async Task<(bool Paused, double Rate)?> ReadPlaybackFeedbackAsync()
    {
        var json = await _videoRouter!.ExecuteAsync("window.guideMate?.playbackState() ?? null");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("paused", out var paused)
            || paused.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        return (paused.GetBoolean(), Math.Clamp(Finite(root, "rate", 1), 0.25, 4));
    }

    private async Task RateHotkeyAsync(double rate, int version)
    {
        var success = await SetRateAsync(rate);
        if (!ImmersiveControlsActive || version != _hotkeyFeedbackVersion) return;
        var state = success ? await ReadPlaybackFeedbackAsync() : null;
        ShowHotkeyFeedback(state is { } playback ? "倍速" + FeedbackNumber(playback.Rate) + "x" : "操作失败", version);
    }

    private async Task TemporaryRateHotkeyAsync(int version)
    {
        _temporaryFeedbackVersion = version;
        var success = await StartTemporaryRateAsync();
        if (_temporaryRateActive || !success)
            ShowHotkeyFeedback(success ? "临时倍速" + FeedbackNumber(_settings.TemporaryRate) + "x" : MissingVideoNotice, version);
    }
}
