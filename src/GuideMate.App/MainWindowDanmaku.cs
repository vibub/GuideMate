using System.Text.Json;
using System.Windows.Interop;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private DanmakuOverlay? _danmakuOverlay;
    private bool _danmakuSourceEnabled;
    private string _danmakuEpoch = "";
    private int _danmakuGeneration;
    private long _danmakuSnapshot;
    private static readonly JsonElement EmptyDanmakuItems = JsonSerializer.SerializeToElement(Array.Empty<object>());
    private JsonElement _danmakuItems = EmptyDanmakuItems;
    private double _danmakuRate = 1;
    private bool _danmakuSeeking;

    public void ApplyDanmakuSettings() => UpdateDanmakuOverlay();

    private void UpdateDanmakuOverlay()
    {
        var visible = _settings.FullscreenDanmaku && _immersive && _danmakuSourceEnabled && IsVisible
            && WindowState != WindowState.Minimized && !_closing;
        if (!visible && _danmakuOverlay == null) return;
        if (_danmakuOverlay == null)
        {
            _danmakuOverlay = new DanmakuOverlay(Environment.ProcessPath!);
            _danmakuOverlay.Failed += message => Dispatcher.BeginInvoke(() => _status.Text = message);
        }
        var screen = visible ? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds : default;
        _danmakuOverlay.SetState(new(visible, screen, _settings.DanmakuDisplayArea, _settings.DanmakuOpacity,
            _settings.DanmakuFontScale, _settings.DanmakuSpeed, _danmakuItems, _position,
            _paused || _danmakuSeeking, _danmakuRate, _danmakuGeneration, _danmakuSnapshot));
    }

    private void ResetDanmaku()
    {
        _danmakuSourceEnabled = false; _danmakuEpoch = ""; _danmakuItems = EmptyDanmakuItems;
        _danmakuGeneration++;
        UpdateDanmakuOverlay();
    }

    private void SyncDanmaku(JsonElement state, double rate)
    {
        if (!state.TryGetProperty("danmaku", out var source) || source.ValueKind != JsonValueKind.Object)
        { ResetDanmaku(); return; }
        _danmakuSourceEnabled = source.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True
            && source.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
        var epoch = source.TryGetProperty("epoch", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() ?? "" : "";
        _danmakuSeeking = state.TryGetProperty("seeking", out var seek) && seek.ValueKind == JsonValueKind.True;
        if (epoch != _danmakuEpoch || _danmakuSeeking) { _danmakuGeneration++; _danmakuEpoch = epoch; }
        // Detach the snapshot before OnWebMessage disposes its JsonDocument.
        _danmakuItems = source.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.Clone() : EmptyDanmakuItems;
        _danmakuRate = rate; _danmakuSnapshot++;
        UpdateDanmakuOverlay();
    }
}
