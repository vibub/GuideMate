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
        public bool Busy;
        public int Revision, WorkerRevision = -1;
        public ArrowRegion? CaptureRegion;
        public MapLocation? Position;
        public long Stamp, FailureSince;
        public double VideoTime;
        public Drawing.Rectangle MarkerBounds;
        public Cv.Size SourceSize;
    }

    private long _drawnGuideStamp, _drawnGameStamp;

    private static void ResetMapLane(MapLane lane)
    {
        lane.Revision++; lane.CaptureRegion = null; lane.Position = null;
        lane.Stamp = 0; lane.FailureSince = 0; lane.Confirmation.Reset();
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
            _mapMarker?.Hide(); return false;
        }
        if (_mapMediaKey != _mediaKey || _mapVideoGeneration != _onlineGeneration)
        {
            ResetMapLane(_guideMap); _mapSampleTime = 0; _mapMarker?.Hide();
            _mapMediaKey = _mediaKey; _mapVideoGeneration = _onlineGeneration;
        }
        var bounds = GameMapCapture.Bounds(handle);
        if (handle != _mapGameHandle || bounds != _mapBounds)
        {
            ResetMapLane(_gameMap); _mapMarker?.Hide();
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
        { _mapMarker?.Hide(); return; }
        if (_mapMarker?.IsVisible == true && _drawnGuideStamp == _guideMap.Stamp && _drawnGameStamp == _gameMap.Stamp) return;
        _mapMarker?.Update(_gameMap.MarkerBounds, game, guide.Position);
        _drawnGuideStamp = _guideMap.Stamp; _drawnGameStamp = _gameMap.Stamp;
        _mapStatus.Text = $"攻略 ({guide.Position.X:0}, {guide.Position.Y:0}) · 玩家 ({game.Position.X:0}, {game.Position.Y:0}) · 独立跟随";
    }

    private static (MinimapObservation? Observation, Cv.Size Size) LocateMapFrame(MapLane lane, int revision,
        byte[] bytes, MapLocator locator, bool automatic, bool cropped, bool gameHud, long timestamp)
    {
        // 该路一次只允许一个后台任务；UI 只修改 Revision，不直接重置正在使用的 Tracker。
        if (lane.WorkerRevision != revision) { lane.Tracker.Reset(); lane.WorkerRevision = revision; }
        using var image = Cv.Cv2.ImDecode(bytes, Cv.ImreadModes.Color);
        var observation = cropped ? lane.Tracker.LocateCropped(image, locator, timestamp)
            : lane.Tracker.Locate(image, locator, automatic, gameHud, timestamp);
        return (observation, image.Size());
    }

    private MapLocation? AcceptMapFrame(MapLane lane, MinimapObservation? observation, Cv.Size size,
        ArrowRegion? capturedRegion, bool automatic, long timestamp, bool guide)
    {
        if (observation == null)
        {
            if (lane.FailureSince == 0) lane.FailureSince = timestamp;
            // 游戏无新位置时不冻结屏幕圆点；攻略短暂丢帧允许游戏继续投影最近的世界坐标。
            if (!guide || timestamp - lane.Stamp > 400) lane.Position = null;
            if (timestamp - lane.FailureSince >= 900)
            {
                ResetMapLane(lane); _mapMarker?.Hide();
                _mapStatus.Text = guide ? "攻略地图丢失，重新搜索完整画面…" : "游戏地图丢失，重新搜索完整窗口…";
            }
            return null;
        }
        lane.FailureSince = 0;
        var position = lane.Confirmation.Accept(observation.Location);
        lane.Position = position;
        if (position == null) { _mapMarker?.Hide(); return null; }
        lane.Stamp = timestamp;
        if (automatic && capturedRegion == null)
        {
            var r = observation.Region;
            lane.CaptureRegion = new((double)r.X / size.Width, (double)r.Y / size.Height,
                (double)r.Width / size.Width, (double)r.Height / size.Height);
        }
        return position;
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
        try
        {
            var frame = await CaptureOnlineFrameAsync(region, true);
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) return;
            if (key != _mediaKey || videoGeneration != _onlineGeneration || _videoSeeking) { ResetMapLane(lane); return; }
            if (frame == null) { AcceptMapFrame(lane, null, default, region, automatic, now, true); return; }
            var sourceSize = new Cv.Size(frame.Width, frame.Height);
            var jumped = _mapSampleTime != 0 && Math.Abs(frame.Time - _mapVideoTime) > Math.Max(1, (now - _mapSampleTime) / 1000d * _settings.Rate + .5);
            _mapVideoTime = frame.Time; _mapSampleTime = now;
            if (jumped || region != null && lane.SourceSize != default && lane.SourceSize != sourceSize)
            {
                lane.SourceSize = sourceSize; ResetMapLane(lane); _mapMarker?.Hide(); return;
            }
            lane.SourceSize = sourceSize;
            var result = await Task.Run(() => LocateMapFrame(lane, revision, frame.Image, locator, automatic, automatic && region != null, false, now));
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) return;
            if (key != _mediaKey || videoGeneration != _onlineGeneration || _videoSeeking || Environment.TickCount64 - now > 1200)
            { ResetMapLane(lane); _mapMarker?.Hide(); return; }
            if (AcceptMapFrame(lane, result.Observation, result.Size, region, automatic, now, true) != null) lane.VideoTime = frame.Time;
            RenderMapPosition();
        }
        catch (Exception ex)
        {
            if (generation == _mapGeneration) { ResetMapLane(lane); _mapMarker?.Hide(); _mapStatus.Text = "攻略地图识别失败：" + ex.Message; }
        }
        finally { lane.Busy = false; DisposeStoppedMap(); }
    }

    private async Task SampleGameMapAsync()
    {
        var lane = _gameMap;
        if (lane.Busy || _mapBusy || _mapLocator == null || !MapContextReady()) return;
        lane.Busy = true;
        var locator = _mapLocator; var generation = _mapGeneration; var revision = lane.Revision;
        var now = Environment.TickCount64; var automatic = _settings.AutoDetectMinimaps;
        var region = automatic ? lane.CaptureRegion : _settings.MapTracking?.GameRegion;
        var fullBounds = _mapBounds;
        try
        {
            var frame = await Task.Run(() => GameMapCapture.Capture(region));
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) return;
            if (frame == null) { AcceptMapFrame(lane, null, default, region, automatic, now, false); _mapMarker?.Hide(); return; }
            var result = await Task.Run(() => LocateMapFrame(lane, revision, frame.Image, locator, automatic, automatic && region != null, true, now));
            if (!_mapRunning || generation != _mapGeneration || revision != lane.Revision) return;
            if (GameMapCapture.ForegroundGame() != frame.Handle || GameMapCapture.Bounds(frame.Handle) != fullBounds || Environment.TickCount64 - now > 1200)
            { ResetMapLane(lane); _mapMarker?.Hide(); return; }
            if (AcceptMapFrame(lane, result.Observation, result.Size, region, automatic, now, false) != null)
            {
                var r = result.Observation!.Region;
                lane.MarkerBounds = new(frame.Bounds.X + r.X, frame.Bounds.Y + r.Y, r.Width, r.Height);
            }
            RenderMapPosition();
        }
        catch (Exception ex)
        {
            if (generation == _mapGeneration) { ResetMapLane(lane); _mapMarker?.Hide(); _mapStatus.Text = "游戏地图识别失败：" + ex.Message; }
        }
        finally { lane.Busy = false; DisposeStoppedMap(); }
    }
}
