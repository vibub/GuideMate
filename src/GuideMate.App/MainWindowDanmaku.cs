using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private DanmakuWindow? _danmakuOverlay;
    private bool _danmakuSourceEnabled;
    private bool _danmakuAvailable;
    private string _danmakuEpoch = "";

    public void ApplyDanmakuSettings() => UpdateDanmakuOverlay();

    private void UpdateDanmakuOverlay()
    {
        if (!_settings.FullscreenDanmaku || !_immersive || !_danmakuSourceEnabled || !IsVisible
            || WindowState == WindowState.Minimized || _closing)
        {
            _danmakuOverlay?.Hide();
            return;
        }
        _danmakuOverlay ??= new DanmakuWindow();
        if (!_danmakuOverlay.IsVisible) _danmakuOverlay.Show();
        _danmakuOverlay.FitToMonitor(this);
    }

    private void ResetDanmaku()
    {
        _danmakuAvailable = false; _danmakuSourceEnabled = false; _danmakuEpoch = "";
        _danmakuOverlay?.Clear(); _danmakuOverlay?.Hide();
    }

    private void SyncDanmaku(JsonElement state, double rate)
    {
        if (!state.TryGetProperty("danmaku", out var source) || source.ValueKind != JsonValueKind.Object)
        { ResetDanmaku(); return; }
        _danmakuAvailable = source.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True;
        _danmakuSourceEnabled = _danmakuAvailable && source.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
        var epoch = source.TryGetProperty("epoch", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() ?? "" : "";
        if (epoch != _danmakuEpoch) { _danmakuOverlay?.Clear(); _danmakuEpoch = epoch; }
        var seeking = state.TryGetProperty("seeking", out var seek) && seek.ValueKind == JsonValueKind.True;
        if (seeking) _danmakuOverlay?.Clear();
        UpdateDanmakuOverlay();
        if (_danmakuOverlay?.IsVisible == true && source.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            _danmakuOverlay.Update(items, _position, _paused || seeking, rate);
    }
}
