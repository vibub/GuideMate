using GuideMate.Vision;
using Cv = OpenCvSharp;
using Drawing = System.Drawing;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private sealed class MapLane
    {
        public readonly MinimapTracker Tracker = new();
        public readonly MapTrackConfirmation Confirmation = new();
        public readonly MapHeadingConfirmation Heading = new();
        public bool Busy;
        public bool FixedPlayerCenter;
        public int Revision, WorkerRevision = -1;
        public ArrowRegion? CaptureRegion;
        public MapLocation? Position;
        public long Stamp, FailureSince;
        public double VideoTime;
        public Drawing.Rectangle MarkerBounds;
        public Cv.Size SourceSize;
        public string? LastFrameId;
        public bool LastFrameLocated;
        public bool SearchingRegion;
        public long LastExpansion;
        public double MotionSpeed;
        public int GuideInterval = 160;
    }

    private sealed record MapFrameResult(MinimapObservation? Observation, Cv.Size Size, double Confidence, bool WidenSearch,
        bool Temporal, string MatchState);

    private long _drawnGuideStamp, _drawnGameStamp;

    private static void ResetMapLane(MapLane lane)
    {
        lane.Revision++; lane.CaptureRegion = null; lane.Position = null;
        lane.Stamp = 0; lane.FailureSince = 0; lane.Confirmation.Reset();
        lane.Heading.Reset();
        lane.LastFrameId = null; lane.LastFrameLocated = false; lane.SearchingRegion = false; lane.MotionSpeed = 0; lane.GuideInterval = 160;
    }

    private void DisposeStoppedMap()
    {
        // 只在两路后台调用均退出后释放共享索引和各自的原生图像资源。
        if (_mapRunning || _mapBusy || _guideMap.Busy || _gameMap.Busy) return;
        _mapLocator?.Dispose(); _mapLocator = null;
        _guideMap.Tracker.Reset(); _gameMap.Tracker.Reset();
    }

    private bool MapContextReady()
    {
        if (!_mapRunning) return false;
        var handle = GameMapCapture.ForegroundGame();
        if (_closing || !IsVisible || WindowState == WindowState.Minimized || _videoSeeking || handle == 0)
        {
            ResetMapLane(_guideMap); ResetMapLane(_gameMap); _mapSampleTime = 0;
            HideMapMarker(_closing ? "closing" : !IsVisible ? "app_hidden" : WindowState == WindowState.Minimized ? "app_minimized"
                : _videoSeeking ? "video_seeking" : "game_not_foreground"); return false;
        }
        if (_mapMediaKey != _mediaKey || _mapVideoGeneration != _onlineGeneration)
        {
            ResetMapLane(_guideMap); _mapSampleTime = 0; HideMapMarker("media_changed");
            _mapMediaKey = _mediaKey; _mapVideoGeneration = _onlineGeneration;
        }
        var bounds = GameMapCapture.Bounds(handle);
        if (handle != _mapGameHandle || bounds != _mapBounds)
        {
            ResetMapLane(_gameMap); HideMapMarker("game_window_changed");
            _mapGameHandle = handle; _mapBounds = bounds;
        }
        return true;
    }

    private void RenderMapPosition()
    {
        if (!MapContextReady()) return;
        var now = Environment.TickCount64;
        if (_guideMap.Position is not { } guide || _gameMap.Position is not { } game
            || now - _guideMap.Stamp > 800 || now - _gameMap.Stamp > 350
            || Math.Abs(_position - _guideMap.VideoTime) > Math.Max(1, _settings.Rate * .8))
        {
            HideMapMarker(_guideMap.Position == null ? "guide_position_unavailable" : _gameMap.Position == null ? "game_position_unavailable"
                : now - _guideMap.Stamp > 800 ? "guide_expired" : now - _gameMap.Stamp > 350 ? "game_expired" : "video_time_mismatch");
            return;
        }
        if (_mapMarker?.IsVisible == true && _drawnGuideStamp == _guideMap.Stamp && _drawnGameStamp == _gameMap.Stamp) return;
        _mapMarker?.SetHeading(guide.PlayerHeading, game);
        _mapMarker?.Update(_gameMap.MarkerBounds, game, guide.Position);
        TraceMapDisplay(guide.PlayerHeading == null ? "dot" : "arrow");
        _drawnGuideStamp = _guideMap.Stamp; _drawnGameStamp = _gameMap.Stamp;
        _mapStatus.Text = $"攻略 ({guide.Position.X:0}, {guide.Position.Y:0}) · 玩家 ({game.Position.X:0}, {game.Position.Y:0}) · 独立跟随";
    }

    private static MapFrameResult LocateMapFrame(MapLane lane, int revision,
        byte[] bytes, MapLocator locator, bool automatic, bool cropped, bool gameHud, long timestamp)
    {
        using var image = Cv.Cv2.ImDecode(bytes, Cv.ImreadModes.Color);
        return LocateMapFrame(lane, revision, image, locator, automatic, cropped, gameHud, timestamp);
    }

    private static MapFrameResult LocateMapFrame(MapLane lane, int revision,
        Cv.Mat image, MapLocator locator, bool automatic, bool cropped, bool gameHud, long timestamp)
    {
        // 该路一次只允许一个后台任务；UI 只修改 Revision，不直接重置正在使用的 Tracker。
        if (lane.WorkerRevision != revision) { lane.Tracker.Reset(); lane.WorkerRevision = revision; }
        var observation = cropped ? lane.Tracker.LocateCropped(image, locator, timestamp, lane.FixedPlayerCenter)
            : lane.Tracker.Locate(image, locator, automatic, gameHud, timestamp);
        return new(observation, image.Size(), lane.Tracker.TrackingConfidence, lane.Tracker.NeedsWiderSearch,
            lane.Tracker.UsedTemporalMatch, lane.Tracker.MatchState);
    }

    private bool ExpandMapSearch(MapLane lane, ArrowRegion? region, bool automatic, bool requested, long timestamp)
    {
        if (!automatic || !requested || region == null || lane.SearchingRegion || timestamp - lane.LastExpansion < 2500) return false;
        var x = Math.Max(0, region.X - region.Width * .35); var y = Math.Max(0, region.Y - region.Height * .35);
        var right = Math.Min(1, region.X + region.Width * 1.35); var bottom = Math.Min(1, region.Y + region.Height * 1.35);
        lane.Revision++; lane.CaptureRegion = new(x, y, right - x, bottom - y);
        lane.SearchingRegion = true; lane.LastExpansion = timestamp; lane.FailureSince = timestamp; lane.LastFrameId = null;
        if (_mapDebug != null) TraceMap("search_expanded", new { lane = ReferenceEquals(lane, _guideMap) ? "guide" : "game", region = lane.CaptureRegion });
        return true;
    }

    private MapLocation? AcceptMapFrame(MapLane lane, MinimapObservation? observation, Cv.Size size,
        ArrowRegion? capturedRegion, bool automatic, long timestamp, bool guide, bool widenSearch = false)
    {
        if (observation == null)
        {
            if (lane.FailureSince == 0) lane.FailureSince = timestamp;
            // 游戏无新位置时不冻结屏幕圆点；攻略短暂丢帧允许游戏继续投影最近的世界坐标。
            if (!guide || timestamp - lane.Stamp > 400) lane.Position = null;
            if (ExpandMapSearch(lane, capturedRegion, automatic, widenSearch, timestamp)) return null;
            if (timestamp - lane.FailureSince >= 900)
            {
                ResetMapLane(lane); HideMapMarker(guide ? "guide_full_search" : "game_full_search");
                _mapStatus.Text = guide ? "攻略地图丢失，重新搜索完整画面…" : "游戏地图丢失，重新搜索完整窗口…";
            }
            return null;
        }
        lane.FailureSince = 0;
        var position = lane.Confirmation.Accept(observation.Location);
        if (position != null) position = position with { PlayerHeading = lane.Heading.Accept(position.PlayerHeading, position.HeadingConfidence, timestamp) };
        lane.Position = position;
        if (_mapDebug != null) TraceMap("confirmation", new { lane = guide ? "guide" : "game", accepted = position != null,
            rawHeading = observation.Location.PlayerHeading, headingConfidence = observation.Location.HeadingConfidence,
            acceptedHeading = position?.PlayerHeading });
        if (position == null) { HideMapMarker(guide ? "guide_confirmation_pending" : "game_confirmation_pending"); return null; }
        lane.Stamp = timestamp;
        if (automatic && (capturedRegion == null || lane.SearchingRegion))
        {
            var r = observation.Region;
            var basis = capturedRegion ?? new ArrowRegion(0, 0, 1, 1);
            lane.CaptureRegion = new(basis.X + (double)r.X / size.Width * basis.Width, basis.Y + (double)r.Y / size.Height * basis.Height,
                (double)r.Width / size.Width * basis.Width, (double)r.Height / size.Height * basis.Height);
            lane.SearchingRegion = false;
            if (_mapDebug != null) TraceMap("region_locked", new { lane = guide ? "guide" : "game", region = lane.CaptureRegion });
        }
        else ExpandMapSearch(lane, capturedRegion, automatic, widenSearch, timestamp);
        return position;
    }

    private static void ScheduleGuide(MapLane lane, MapLocation? next, double confidence, bool paused, long timestamp)
    {
        if (next != null && lane.Position is { } previous && lane.Stamp > 0)
        {
            var p = next.ToMinimap(previous.Position);
            var anchor = next.PlayerPixel ?? new MapPoint(next.Width / 2d, next.Height / 2d);
            var speed = Math.Sqrt(Math.Pow(p.X - anchor.X, 2) + Math.Pow(p.Y - anchor.Y, 2))
                * 240 / next.Width / Math.Max(.08, (timestamp - lane.Stamp) / 1000d);
            lane.MotionSpeed = .5 * lane.MotionSpeed + .5 * speed;
        }
        var turning = next?.PlayerHeading is { } angle && lane.Position?.PlayerHeading is { } oldAngle
            && Math.Abs((angle - oldAngle + 540) % 360 - 180) > 15;
        lane.GuideInterval = next == null || confidence < .7 || turning ? 80 : paused ? 400
            : lane.MotionSpeed > 12 ? 80 : lane.MotionSpeed > 2 ? 160 : 300;
    }

    private async Task SampleGuideMapAsync()
    {
        var lane = _guideMap;
        if (lane.Busy || _mapBusy || _mapLocator == null || !MapContextReady()) return;
        lane.Busy = true;
        var locator = _mapLocator; var generation = _mapGeneration; var revision = lane.Revision;
        var key = _mediaKey; var videoGeneration = _onlineGeneration; var now = Environment.TickCount64;
        var automatic = _settings.AutoDetectMinimaps;
        var region = automatic ? lane.CaptureRegion : _settings.MapTracking?.GuideRegion;
        var cropped = automatic && region != null && !lane.SearchingRegion;
        try
        {
            var frame = await CaptureOnlineFrameAsync(region, true, lane.LastFrameId);
            var captured = Environment.TickCount64;
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) { TraceMap("guide_capture_discarded"); return; }
            if (key != _mediaKey || videoGeneration != _onlineGeneration || _videoSeeking) { TraceMap("guide_source_changed"); ResetMapLane(lane); return; }
            if (frame == null) { TraceMapSample("guide", now, captured, null, "unavailable", cropped, lane.SearchingRegion); lane.GuideInterval = 160; AcceptMapFrame(lane, null, default, region, automatic, now, true); return; }
            var sourceSize = new Cv.Size(frame.Width, frame.Height);
            var jumped = _mapSampleTime != 0 && Math.Abs(frame.Time - _mapVideoTime) > Math.Max(1, (now - _mapSampleTime) / 1000d * _settings.Rate + .5);
            _mapVideoTime = frame.Time; _mapSampleTime = now;
            if (jumped || region != null && lane.SourceSize != default && lane.SourceSize != sourceSize)
            {
                lane.SourceSize = sourceSize; ResetMapLane(lane); HideMapMarker(jumped ? "video_jump" : "video_size_changed"); return;
            }
            lane.SourceSize = sourceSize;
            if (frame.Method == "unchanged")
            {
                TraceMapSample("guide", now, captured, null, "unchanged", cropped, lane.SearchingRegion);
                if (!lane.LastFrameLocated) AcceptMapFrame(lane, null, default, region, automatic, now, true);
                else if (lane.Position != null && Math.Abs(frame.Time - lane.VideoTime) < .15) lane.Stamp = now;
                else lane.LastFrameId = null;
                lane.GuideInterval = frame.Paused ? 400 : Math.Min(160, lane.GuideInterval);
                RenderMapPosition(); return;
            }
            var result = await Task.Run(() => LocateMapFrame(lane, revision, frame.Image, locator, automatic, cropped, false, now));
            TraceMapSample("guide", now, captured, result, frame.Method, cropped, lane.SearchingRegion);
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) { TraceMap("guide_result_discarded"); return; }
            if (key != _mediaKey || videoGeneration != _onlineGeneration || _videoSeeking || Environment.TickCount64 - now > 1200)
            { ResetMapLane(lane); HideMapMarker(Environment.TickCount64 - now > 1200 ? "guide_result_too_old" : "guide_source_changed"); return; }
            ScheduleGuide(lane, result.Observation?.Location, result.Confidence, frame.Paused, now);
            if (AcceptMapFrame(lane, result.Observation, result.Size, region, automatic, now, true, result.WidenSearch) != null)
            {
                lane.VideoTime = frame.Time;
                if (revision == lane.Revision) { lane.LastFrameId = frame.FrameId; lane.LastFrameLocated = true; }
            }
            else if (result.Observation == null && revision == lane.Revision)
            { lane.LastFrameId = frame.FrameId; lane.LastFrameLocated = false; }
            RenderMapPosition();
        }
        catch (Exception ex)
        {
            if (_mapDebug != null) TraceMap("guide_error", new { type = ex.GetType().Name, code = ex.HResult });
            if (generation == _mapGeneration) { ResetMapLane(lane); HideMapMarker("guide_error"); _mapStatus.Text = "攻略地图识别失败：" + ex.Message; }
        }
        finally { lane.Busy = false; _guideMapTimer.Interval = TimeSpan.FromMilliseconds(lane.GuideInterval); DisposeStoppedMap(); }
    }

    private async Task SampleGameMapAsync()
    {
        var lane = _gameMap;
        lane.FixedPlayerCenter = true;
        if (lane.Busy || _mapBusy || _mapLocator == null || !MapContextReady()) return;
        lane.Busy = true;
        var locator = _mapLocator; var generation = _mapGeneration; var revision = lane.Revision;
        var now = Environment.TickCount64; var automatic = _settings.AutoDetectMinimaps;
        var region = automatic ? lane.CaptureRegion : _settings.MapTracking?.GameRegion;
        var cropped = automatic && region != null && !lane.SearchingRegion;
        var fullBounds = _mapBounds;
        try
        {
            using var frame = await Task.Run(() => GameMapCapture.CapturePixels(region));
            var captured = Environment.TickCount64;
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) { TraceMap("game_capture_discarded"); return; }
            if (frame == null) { TraceMapSample("game", now, captured, null, "unavailable", cropped, lane.SearchingRegion); AcceptMapFrame(lane, null, default, region, automatic, now, false); HideMapMarker("game_capture_unavailable"); return; }
            var result = await Task.Run(() => LocateMapFrame(lane, revision, frame.Image, locator, automatic, cropped, region == null, now));
            TraceMapSample("game", now, captured, result, "pixels", cropped, lane.SearchingRegion);
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) { TraceMap("game_result_discarded"); return; }
            if (GameMapCapture.ForegroundGame() != frame.Handle || GameMapCapture.Bounds(frame.Handle) != fullBounds || Environment.TickCount64 - now > 1200)
            { ResetMapLane(lane); HideMapMarker(Environment.TickCount64 - now > 1200 ? "game_result_too_old" : "game_capture_context_changed"); return; }
            if (AcceptMapFrame(lane, result.Observation, result.Size, region, automatic, now, false, result.WidenSearch) != null)
            {
                var r = result.Observation!.Region;
                lane.MarkerBounds = new(frame.Bounds.X + r.X, frame.Bounds.Y + r.Y, r.Width, r.Height);
            }
            RenderMapPosition();
        }
        catch (Exception ex)
        {
            if (_mapDebug != null) TraceMap("game_error", new { type = ex.GetType().Name, code = ex.HResult });
            if (generation == _mapGeneration) { ResetMapLane(lane); HideMapMarker("game_error"); _mapStatus.Text = "游戏地图识别失败：" + ex.Message; }
        }
        finally { lane.Busy = false; DisposeStoppedMap(); }
    }
}
