using GuideMate.Vision;
using System.Windows.Threading;
using Cv = OpenCvSharp;
using System.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly TextBlock _mapStatus = Ui.Text("地图跟随未启动", 12, Ui.Muted);
    private readonly DispatcherTimer _mapTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _guideMapTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer _mapVisibilityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private MapLocator? _mapLocator;
    private readonly MapLane _guideMap = new(), _gameMap = new();
    private MapMarkerWindow? _mapMarker;
    private bool _mapRunning, _mapBusy;
    private int _mapGeneration;
    private int _mapVideoGeneration;
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
        panel.Children.Add(Ui.Toggle("自动寻找攻略和游戏小地图", _settings.AutoDetectMinimaps, value =>
        {
            StopMapTracking(); _settings.AutoDetectMinimaps = value; SaveSettings();
            _mapStatus.Text = value ? "自动识别已启用，点击启动即可；手动选区仍保留。" : "已切换手动选区，请确认两路选区后启动。";
        }));
        panel.Children.Add(Ui.Command("\uE714", "手动校准攻略小地图（备用）", CalibrateGuideMap));
        panel.Children.Add(Ui.Command("\uE714", "手动校准游戏小地图（备用）", CalibrateGameMap));
        panel.Children.Add(Ui.Command("\uE768", "启动地图跟随", StartMapTracking));
        panel.Children.Add(Ui.Command("\uE71A", "停止地图跟随", StopMapTracking));
        panel.Children.Add(Ui.Toggle("记录地图调试日志（本次运行）", false, SetMapDebug));
        panel.Children.Add(Ui.Command("\uE8B7", "打开调试日志目录", OpenMapDebugDirectory));
        panel.Children.Add(_mapDebugStatus);
        panel.Children.Add(_mapStatus);
        panel.Children.Add(Ui.Text("青色小箭头表示攻略位置与角色朝向，方向不确定时显示圆点；超出范围停在地图边缘。隐藏随引、切出游戏或跟踪失效时暂停标记。", 12, Ui.Muted));
        _mapTimer.Tick += async (_, _) => await SampleGameMapAsync();
        _guideMapTimer.Tick += async (_, _) => await SampleGuideMapAsync();
        _mapVisibilityTimer.Tick += (_, _) => RenderMapPosition();
        Closed += (_, _) =>
        {
            StopMapTracking(); _mapMarker?.Close(); _mapMarker = null;
            _mapDebug?.Complete(); _mapDebug?.Completion.Wait(TimeSpan.FromSeconds(2)); _mapDebug = null;
        };
        return panel;
    }

    private bool CanConfigureMap()
    {
        if (_mapBusy || _guideMap.Busy || _gameMap.Busy) { _mapStatus.Text = "正在处理地图画面，请稍后重试。"; return false; }
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
        var profile = _settings.MapTracking ?? new MapTrackingProfile("", null, null);
        if (!_settings.AutoDetectMinimaps && (profile.GuideRegion is not { IsValid: true } || profile.GameRegion is not { IsValid: true }))
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
            _mapMediaKey = _mediaKey; _mapVideoGeneration = _onlineGeneration;
            _guideMapTimer.Interval = TimeSpan.FromMilliseconds(160);
            _mapRunning = true; _mapTimer.Start(); _guideMapTimer.Start(); _mapVisibilityTimer.Start();
            TraceMap("tracking_start", new { automatic = _settings.AutoDetectMinimaps });
            _mapStatus.Text = "已启动。保持攻略小窗可见，并切换到原神开始定位。";
        }
        catch (OperationCanceledException) { _mapStatus.Text = "地图资源加载已取消。"; }
        catch (Exception ex) { _mapLocator?.Dispose(); _mapLocator = null; _mapStatus.Text = "地图启动失败：" + ex.Message; }
        finally { _mapBusy = false; _mapLoadCancellation.Dispose(); _mapLoadCancellation = null; }
    }

    private void StopMapTracking()
    {
        _mapRunning = false; _mapGeneration++; _mapTimer.Stop(); _guideMapTimer.Stop(); _mapVisibilityTimer.Stop();
        _mapLoadCancellation?.Cancel();
        ResetMapLane(_guideMap); ResetMapLane(_gameMap); HideMapMarker("tracking_stopped");
        TraceMap("tracking_stop");
        DisposeStoppedMap();
        _mapStatus.Text = "地图跟随已停止。";
    }
}
