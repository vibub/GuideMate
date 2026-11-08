using GuideMate.Vision;
using System.Windows.Threading;
using Cv = OpenCvSharp;
using System.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly TextBlock _mapStatus = Ui.Text("地图跟随未启动", 12, Ui.Muted);
    private readonly DispatcherTimer _mapTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _mapVisibilityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly MapTrackConfirmation _guideConfirmation = new(), _gameConfirmation = new();
    private MapLocator? _mapLocator;
    private MapMarkerWindow? _mapMarker;
    private bool _mapRunning, _mapBusy;
    private int _mapGeneration;
    private int _mapVideoGeneration;
    private long _mapLastUpdate;
    private nint _mapGameHandle;
    private System.Drawing.Rectangle _mapBounds;
    private string _mapMediaKey = "";
    private double _mapVideoTime;
    private long _mapSampleTime;
    private CancellationTokenSource? _mapLoadCancellation;

    private FrameworkElement BuildMapPanel()
    {
        var panel = new StackPanel { Margin = new(0, 18, 0, 0) };
        panel.Children.Add(Ui.Heading("原神地图跟随 · 实验功能"));
        panel.Children.Add(Ui.Text("默认使用提瓦特地表地图，首次启动自动下载约 206 MiB，后续离线使用。北向固定，游戏地图需无遮挡。", 12, Ui.Muted));
        panel.Children.Add(Ui.Command("\uE707", "使用内置提瓦特地表地图", UseBuiltInMap));
        panel.Children.Add(Ui.Command("\uE8B7", "导入自定义底图（可选）", ImportMapAtlas));
        panel.Children.Add(Ui.Command("\uE714", "1. 框选攻略完整小地图", CalibrateGuideMap));
        panel.Children.Add(Ui.Command("\uE714", "2. 框选游戏完整小地图", CalibrateGameMap));
        panel.Children.Add(Ui.Command("\uE768", "启动地图跟随", StartMapTracking));
        panel.Children.Add(Ui.Command("\uE71A", "停止地图跟随", StopMapTracking));
        panel.Children.Add(_mapStatus);
        panel.Children.Add(Ui.Text("青色点为攻略位置；超出范围显示方向。隐藏随引、切出游戏或识别失败时暂停标记。地图配置独立保存，不改变箭头选区。", 12, Ui.Muted));
        _mapTimer.Tick += async (_, _) => await SampleMapAsync();
        _mapVisibilityTimer.Tick += (_, _) =>
        {
            if (!_mapRunning || !IsVisible || WindowState == WindowState.Minimized || _videoSeeking
                || _mapMediaKey != _mediaKey || _mapVideoGeneration != _onlineGeneration || GameMapCapture.ForegroundGame() != _mapGameHandle
                || Environment.TickCount64 - _mapLastUpdate > 1200
                || (_settings.MapTracking?.GameRegion is { } region && GameMapCapture.Bounds(_mapGameHandle, region) != _mapBounds))
                _mapMarker?.Hide();
        };
        Closed += (_, _) => { StopMapTracking(); _mapMarker?.Close(); _mapMarker = null; };
        return panel;
    }

    private bool CanConfigureMap()
    {
        if (_mapBusy) { _mapStatus.Text = "正在处理地图画面，请稍后重试。"; return false; }
        StopMapTracking(); return true;
    }

    private void ImportMapAtlas()
    {
        if (!CanConfigureMap()) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择同一地表区域的无标注地图（最大 8192×8192）", Filter = "地图图片|*.png;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) != true) return;
        var previous = _settings.MapTracking;
        _settings.MapTracking = new(picker.FileName, previous?.GuideRegion, previous?.GameRegion);
        SaveSettings(); _mapStatus.Text = "底图：" + Path.GetFileName(picker.FileName) + "；启动时检查纹理。";
    }

    private void UseBuiltInMap()
    {
        if (!CanConfigureMap()) return;
        var previous = _settings.MapTracking;
        _settings.MapTracking = new("", previous?.GuideRegion, previous?.GameRegion);
        SaveSettings(); _mapStatus.Text = "已选择内置提瓦特地表地图。首次启动时自动下载资源；地下和特殊地图暂不支持。";
    }

    private async void CalibrateGuideMap()
    {
        if (!CanConfigureMap()) return;
        _mapBusy = true;
        try
        {
            var key = _mediaKey;
            var frame = await CaptureOnlineFrameAsync(mapDetail: true);
            if (frame == null || key != _mediaKey) { _mapStatus.Text = "请先加载并显示攻略视频。"; return; }
            var old = _settings.MapTracking;
            var dialog = new MapRegionWindow(this, frame.Image, "随引 · 攻略小地图选区", old?.GuideRegion);
            if (dialog.ShowDialog() != true || dialog.Result == null || key != _mediaKey) return;
            _settings.MapTracking = new(old?.AtlasPath ?? "", dialog.Result, old?.GameRegion);
            SaveSettings(); _mapStatus.Text = "攻略地图选区已保存。";
        }
        catch (Exception ex) { _mapStatus.Text = "攻略选区失败：" + ex.Message; }
        finally { _mapBusy = false; }
    }

    private async void CalibrateGameMap()
    {
        if (!CanConfigureMap()) return;
        _mapBusy = true;
        var generation = _mapGeneration;
        try
        {
            _mapStatus.Text = "请在 15 秒内切换到原神，保持小地图完整可见；检测到游戏后自动截图。";
            GameMapCapture.Frame? frame = null;
            for (var i = 0; i < 30 && !_closing && generation == _mapGeneration; i++)
            {
                await Task.Delay(500);
                if (GameMapCapture.ForegroundGame() == 0) continue;
                frame = GameMapCapture.Capture(); if (frame != null) break;
            }
            if (_closing || generation != _mapGeneration) return;
            if (frame == null) { _mapStatus.Text = "未检测到前台原神窗口，请重试。"; return; }
            var old = _settings.MapTracking;
            var dialog = new MapRegionWindow(this, frame.Image, "随引 · 游戏小地图选区", old?.GameRegion);
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            _settings.MapTracking = new(old?.AtlasPath ?? "", old?.GuideRegion, dialog.Result);
            SaveSettings(); _mapStatus.Text = "游戏地图选区已保存。";
        }
        catch (Exception ex) { _mapStatus.Text = "游戏选区失败：" + ex.Message; }
        finally { _mapBusy = false; }
    }

    private async void StartMapTracking()
    {
        if (!CanConfigureMap()) return;
        var profile = _settings.MapTracking;
        if (profile?.GuideRegion is not { IsValid: true } || profile.GameRegion is not { IsValid: true })
        { _mapStatus.Text = "请先分别保存攻略和游戏的小地图选区。"; return; }
        if (!string.IsNullOrEmpty(profile.AtlasPath) && !File.Exists(profile.AtlasPath))
        { _mapStatus.Text = "自定义底图不存在，请重新导入或选择内置提瓦特地图。"; return; }
        _mapBusy = true; var generation = _mapGeneration;
        _mapLoadCancellation = new(); var token = _mapLoadCancellation.Token;
        try
        {
            string? featureDirectory = null;
            if (string.IsNullOrEmpty(profile.AtlasPath))
                featureDirectory = await BuiltInMapAssets.EnsureAsync(_dataPath, new Progress<string>(message =>
                { if (!_closing && generation == _mapGeneration) _mapStatus.Text = message; }), token);
            token.ThrowIfCancellationRequested();
            _mapStatus.Text = "正在建立底图特征…";
            var locator = await Task.Run(() =>
            {
                if (featureDirectory != null) return new MapLocator(featureDirectory);
                using var atlas = Cv.Cv2.ImRead(profile.AtlasPath);
                return new MapLocator(atlas);
            });
            if (_closing || generation != _mapGeneration) { locator.Dispose(); return; }
            _mapLocator = locator;
            _mapMarker ??= new MapMarkerWindow();
            _mapRunning = true; _mapTimer.Start(); _mapVisibilityTimer.Start();
            _mapStatus.Text = "已启动。保持攻略小窗可见，并切换到原神开始定位。";
        }
        catch (OperationCanceledException) { _mapStatus.Text = "地图资源加载已取消。"; }
        catch (Exception ex) { _mapLocator?.Dispose(); _mapLocator = null; _mapStatus.Text = "地图启动失败：" + ex.Message; }
        finally { _mapBusy = false; _mapLoadCancellation.Dispose(); _mapLoadCancellation = null; }
    }

    private void StopMapTracking()
    {
        _mapRunning = false; _mapGeneration++; _mapTimer.Stop(); _mapVisibilityTimer.Stop();
        _mapLoadCancellation?.Cancel();
        _mapMarker?.Hide(); _guideConfirmation.Reset(); _gameConfirmation.Reset();
        if (!_mapBusy) { _mapLocator?.Dispose(); _mapLocator = null; }
        _mapStatus.Text = "地图跟随已停止。";
    }

    private void RejectMapSample(string message)
    {
        _mapMarker?.Hide(); _guideConfirmation.Reset(); _gameConfirmation.Reset(); _mapStatus.Text = message;
    }

    private async Task SampleMapAsync()
    {
        if (_mapBusy || !_mapRunning || _mapLocator == null) return;
        if (_closing || !IsVisible || WindowState == WindowState.Minimized || _videoSeeking || GameMapCapture.ForegroundGame() == 0)
        { RejectMapSample("等待前台游戏和可见攻略画面…"); return; }
        _mapBusy = true; var generation = _mapGeneration; var key = _mediaKey; var videoGeneration = _onlineGeneration;
        var locator = _mapLocator; var profile = _settings.MapTracking!;
        try
        {
            var guide = await CaptureOnlineFrameAsync(profile.GuideRegion, true);
            var game = GameMapCapture.Capture(profile.GameRegion);
            if (guide == null || game == null) { RejectMapSample("暂无完整地图画面。"); return; }
            var now = Environment.TickCount64;
            if (_mapMediaKey != key || _mapVideoGeneration != videoGeneration || Math.Abs(guide.Time - _mapVideoTime) > Math.Max(1, (now - _mapSampleTime) / 1000d * _settings.Rate + .5))
            { _guideConfirmation.Reset(); _gameConfirmation.Reset(); _mapMarker?.Hide(); }
            _mapMediaKey = key; _mapVideoGeneration = videoGeneration; _mapVideoTime = guide.Time; _mapSampleTime = now;
            var result = await Task.Run(() =>
            {
                using var guideImage = Cv.Cv2.ImDecode(guide.Image, Cv.ImreadModes.Color);
                using var gameImage = Cv.Cv2.ImDecode(game.Image, Cv.ImreadModes.Color);
                return (Guide: locator.Locate(guideImage, true), Game: locator.Locate(gameImage, true));
            });
            if (_closing || !_mapRunning || generation != _mapGeneration) return;
            if (key != _mediaKey || videoGeneration != _onlineGeneration || _videoSeeking || !IsVisible || WindowState == WindowState.Minimized
                || GameMapCapture.ForegroundGame() != game.Handle || GameMapCapture.Bounds(game.Handle, profile.GameRegion) != game.Bounds
                || Environment.TickCount64 - now > 1200 || Math.Abs(_position - guide.Time) > Math.Max(1.2, _settings.Rate * 1.2))
            { RejectMapSample("画面已变化，正在重新定位…"); return; }
            var guidePosition = _guideConfirmation.Accept(result.Guide);
            var gamePosition = _gameConfirmation.Accept(result.Game);
            if (guidePosition == null || gamePosition == null)
            { _mapMarker?.Hide(); _mapStatus.Text = $"定位中 · 攻略{(result.Guide == null ? "未识别" : "待确认")} · 游戏{(result.Game == null ? "未识别" : "待确认")}"; return; }
            _mapBounds = game.Bounds; _mapGameHandle = game.Handle; _mapLastUpdate = now;
            _mapMarker!.Update(game.Bounds, gamePosition, guidePosition.Position);
            _mapStatus.Text = $"攻略 ({guidePosition.Position.X:0}, {guidePosition.Position.Y:0}) · 玩家 ({gamePosition.Position.X:0}, {gamePosition.Position.Y:0}) · 底图像素坐标";
        }
        catch (Exception ex) { RejectMapSample("地图识别失败：" + ex.Message); }
        finally
        {
            _mapBusy = false;
            if (!_mapRunning) { _mapLocator?.Dispose(); _mapLocator = null; }
        }
    }
}
