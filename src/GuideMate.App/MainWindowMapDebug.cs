using System.Diagnostics;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private MapDebugLog? _mapDebug;
    private Button? _mapDebugButton;
    private readonly TextBlock _mapDebugStatus = Ui.Text("未记录，仅本次运行生效。", 12, Ui.Muted);
    private string? _mapDebugDisplayState;
    private bool? _mapDebugVisible;
    private long? _mapDebugHiddenSince, _mapDebugUnexpectedHiddenSince;
    private int _mapDebugHiddenCount, _mapDebugExpiredCount, _mapDebugRecognitionCount;
    private long _mapDebugLongestHiddenMs, _mapDebugLongestUnexpectedHiddenMs;
    private string MapDebugDirectory => Path.Combine(_dataPath, "Logs", "MapTracking");

    private async void SetMapDebug(bool enabled)
    {
        var previous = _mapDebug;
        if (previous != null) CompleteMapDebug(previous);
        _mapDebug = null; _mapDebugDisplayState = null; _mapDebugVisible = null;
        _mapDebugHiddenSince = null; _mapDebugUnexpectedHiddenSince = null;
        _mapDebugHiddenCount = _mapDebugExpiredCount = _mapDebugRecognitionCount = 0;
        _mapDebugLongestHiddenMs = _mapDebugLongestUnexpectedHiddenMs = 0;
        if (enabled)
        {
            try
            {
                _mapDebug = new(MapDebugDirectory);
                _mapDebugVisible = _mapMarker?.IsVisible == true;
                _mapDebugStatus.Text = "正在记录，不含截图或账号资料。";
                TraceMap("debug_enabled", new { running = _mapRunning, automatic = _settings.AutoDetectMinimaps,
                    guideIntervalMs = _guideMapTimer.Interval.TotalMilliseconds, gameIntervalMs = _mapTimer.Interval.TotalMilliseconds,
                    guideFreshnessMs = GuideMapFreshnessMs, gameFreshnessMs = GameMapFreshnessMs });
                TraceMapDisplay(_mapDebugVisible == true ? (_guideMap.Position?.PlayerHeading == null ? "dot" : "arrow") : "hidden:debug_started");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _mapDebugStatus.Text = $"无法创建日志（{ex.GetType().Name}），请检查资料目录权限。"; }
        }
        else _mapDebugStatus.Text = previous == null ? "调试记录已关闭。" : "已停止，日志已保留。";
        UpdateMapDebugButton();
        if (_mapDebug is { } current) ObserveMapDebugCompletion(current);
        if (previous != null) await previous.Completion;
    }

    private void UpdateMapDebugButton()
    {
        if (_mapDebugButton != null)
            SetMapButtonLabel(_mapDebugButton, _mapDebug == null ? "\uE7C8" : "\uE71A",
                _mapDebug == null ? "开始记录日志" : "停止记录日志");
    }

    private async void ObserveMapDebugCompletion(MapDebugLog log)
    {
        await log.Completion;
        if (!ReferenceEquals(_mapDebug, log)) return;
        _mapDebug = null;
        _mapDebugStatus.Text = log.Problem ?? "已停止，日志已保留。";
        UpdateMapDebugButton();
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
        if (_mapDebug.Problem is { } problem) _mapDebugStatus.Text = problem;
    }

    private void TraceMapDisplay(string state)
    {
        if (_mapDebug == null || state == _mapDebugDisplayState) return;
        _mapDebugDisplayState = state;
        var visible = _mapMarker?.IsVisible == true;
        var visibilityChanged = _mapDebugVisible.HasValue && _mapDebugVisible.Value != visible;
        _mapDebugVisible = visible;
        var now = Environment.TickCount64;
        var category = state switch
        {
            "hidden:guide_expired" or "hidden:game_expired" or "hidden:guide_result_too_old" or "hidden:game_result_too_old" => "expired",
            "hidden:guide_position_unavailable" or "hidden:game_position_unavailable" or "hidden:game_capture_unavailable"
                or "hidden:guide_confirmation_pending" or "hidden:game_confirmation_pending"
                or "hidden:guide_full_search" or "hidden:game_full_search" or "hidden:guide_error" or "hidden:game_error" => "recognition",
            _ => "context"
        };
        if (visibilityChanged && !visible)
        {
            _mapDebugHiddenCount++; _mapDebugHiddenSince = now;
            if (category == "expired") _mapDebugExpiredCount++;
            if (category == "recognition") _mapDebugRecognitionCount++;
            _mapDebugUnexpectedHiddenSince = category != "context" ? now : null;
        }
        long? hiddenDurationMs = null;
        if (visible && _mapDebugHiddenSince is { } hiddenSince)
        {
            hiddenDurationMs = now - hiddenSince;
            _mapDebugLongestHiddenMs = Math.Max(_mapDebugLongestHiddenMs, hiddenDurationMs.Value);
            _mapDebugHiddenSince = null;
        }
        // 主动切换前台、跳转等会结束非主动隐藏计时，避免把离开游戏的时间算作闪烁。
        if ((visible || category == "context") && _mapDebugUnexpectedHiddenSince is { } unexpectedSince)
        {
            _mapDebugLongestUnexpectedHiddenMs = Math.Max(_mapDebugLongestUnexpectedHiddenMs, now - unexpectedSince);
            _mapDebugUnexpectedHiddenSince = null;
        }
        TraceMap("display_state", new { state, visible, visibilityChanged, hideCategory = visible ? null : category,
            hiddenDurationMs, guideAgeMs = _guideMap.Stamp == 0 ? (long?)null : now - _guideMap.Stamp,
            gameAgeMs = _gameMap.Stamp == 0 ? (long?)null : now - _gameMap.Stamp,
            guideBusy = _guideMap.Busy, gameBusy = _gameMap.Busy, guideHeading = _guideMap.Position?.PlayerHeading });
    }

    private void CompleteMapDebug(MapDebugLog log)
    {
        var now = Environment.TickCount64;
        log.Record("display_summary", new { visibleToHiddenCount = _mapDebugHiddenCount,
            expiredHideCount = _mapDebugExpiredCount, recognitionHideCount = _mapDebugRecognitionCount,
            nonContextHideCount = _mapDebugExpiredCount + _mapDebugRecognitionCount,
            longestHiddenMs = Math.Max(_mapDebugLongestHiddenMs, _mapDebugHiddenSince is { } start ? now - start : 0),
            longestNonContextHiddenMs = Math.Max(_mapDebugLongestUnexpectedHiddenMs,
                _mapDebugUnexpectedHiddenSince is { } unexpected ? now - unexpected : 0) });
        log.Complete();
    }

    private void HideMapMarker(string reason)
    {
        _mapMarker?.Hide(); TraceMapDisplay("hidden:" + reason);
    }

    private void TraceMapSample(string lane, long started, long captured, MapFrameResult? result,
        string capture, bool cropped, bool expanded, long? captureStarted = null)
    {
        if (_mapDebug == null) return;
        var position = result?.Observation?.Location;
        var finished = Environment.TickCount64;
        TraceMap("sample", new { lane, capture, cropped, expanded,
            captureMs = captured - started, captureQueueMs = captureStarted - started,
            imageAgeMs = captureStarted is { } imageTime ? finished - imageTime : (long?)null,
            recognitionMs = finished - captured, timings = result?.Diagnostics,
            totalMs = finished - started, found = position != null,
            matchState = result?.MatchState, temporal = result?.Temporal, confidence = result?.Confidence,
            widen = result?.WidenSearch, rawHeading = position?.PlayerHeading, headingConfidence = position?.HeadingConfidence,
            guideIntervalMs = _guideMap.GuideInterval });
    }
}
