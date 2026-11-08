using System.Diagnostics;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private MapDebugLog? _mapDebug;
    private readonly TextBlock _mapDebugStatus = Ui.Text("调试默认关闭；仅本次运行生效。日志不包含截图、网址或账号资料。", 12, Ui.Muted);
    private string? _mapDebugDisplayState;
    private bool? _mapDebugVisible;
    private string MapDebugDirectory => Path.Combine(_dataPath, "Logs", "MapTracking");

    private async void SetMapDebug(bool enabled)
    {
        var previous = _mapDebug; _mapDebug = null; _mapDebugDisplayState = null; _mapDebugVisible = null;
        if (previous != null) previous.Complete();
        if (enabled)
        {
            try
            {
                _mapDebug = new(MapDebugDirectory);
                _mapDebugStatus.Text = "正在记录：" + _mapDebug.FilePath;
                TraceMap("debug_enabled", new { running = _mapRunning, automatic = _settings.AutoDetectMinimaps,
                    guideIntervalMs = _guideMapTimer.Interval.TotalMilliseconds, gameIntervalMs = _mapTimer.Interval.TotalMilliseconds });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _mapDebugStatus.Text = $"无法创建日志（{ex.GetType().Name}），请检查资料目录权限。"; }
        }
        else _mapDebugStatus.Text = previous == null ? "调试记录已关闭。" : "已停止记录，日志保留在：" + previous.FilePath;
        if (previous != null) await previous.Completion;
    }

    private void OpenMapDebugDirectory()
    {
        try
        {
            Directory.CreateDirectory(MapDebugDirectory);
            Process.Start(new ProcessStartInfo(MapDebugDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { _mapDebugStatus.Text = "无法打开日志目录：" + ex.GetType().Name; }
    }

    private void TraceMap(string kind, object? detail = null)
    {
        if (_mapDebug == null) return;
        _mapDebug.Record(kind, detail);
        if (_mapDebug.Problem is { } problem) _mapDebugStatus.Text = problem + " 文件：" + _mapDebug.FilePath;
    }

    private void TraceMapDisplay(string state)
    {
        if (_mapDebug == null || state == _mapDebugDisplayState) return;
        _mapDebugDisplayState = state;
        var visible = _mapMarker?.IsVisible == true;
        var visibilityChanged = _mapDebugVisible.HasValue && _mapDebugVisible.Value != visible;
        _mapDebugVisible = visible;
        var now = Environment.TickCount64;
        TraceMap("display_state", new { state, visible, visibilityChanged, guideAgeMs = _guideMap.Stamp == 0 ? (long?)null : now - _guideMap.Stamp,
            gameAgeMs = _gameMap.Stamp == 0 ? (long?)null : now - _gameMap.Stamp,
            guideBusy = _guideMap.Busy, gameBusy = _gameMap.Busy, guideHeading = _guideMap.Position?.PlayerHeading });
    }

    private void HideMapMarker(string reason)
    {
        _mapMarker?.Hide(); TraceMapDisplay("hidden:" + reason);
    }

    private void TraceMapSample(string lane, long started, long captured, MapFrameResult? result,
        string capture, bool cropped, bool expanded)
    {
        if (_mapDebug == null) return;
        var position = result?.Observation?.Location;
        TraceMap("sample", new { lane, capture, cropped, expanded,
            captureMs = captured - started, recognitionMs = Environment.TickCount64 - captured,
            totalMs = Environment.TickCount64 - started, found = position != null,
            matchState = result?.MatchState, temporal = result?.Temporal, confidence = result?.Confidence,
            widen = result?.WidenSearch, rawHeading = position?.PlayerHeading, headingConfidence = position?.HeadingConfidence,
            guideIntervalMs = _guideMap.GuideInterval });
    }
}
