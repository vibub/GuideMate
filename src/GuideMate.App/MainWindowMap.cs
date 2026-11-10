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
    private readonly List<Button> _mapTrackingButtons = new();
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
        panel.Children.Add(BuildMapTrackingButton());
        _mapStatus.Margin = new(2, 6, 2, 10);
        panel.Children.Add(_mapStatus);
        panel.Children.Add(Ui.Text("保持小地图北向固定、无遮挡。", 12, Ui.Muted));

        var setup = new StackPanel();
        setup.Children.Add(Ui.Text("内置地表地图首次下载约 206 MiB，之后可离线使用。", 12, Ui.Muted));
        setup.Children.Add(Ui.Command("\uE707", "使用内置地表地图", UseBuiltInMap));
        setup.Children.Add(Ui.Command("\uE8B7", "导入自定义底图", ImportMapAtlas));
        setup.Children.Add(Ui.Toggle("自动识别小地图", _settings.AutoDetectMinimaps, value =>
        {
            StopMapTracking(); _settings.AutoDetectMinimaps = value; SaveSettings();
            _mapStatus.Text = value ? "自动识别已启用。" : "请校准两路小地图后启动。";
        }));
        var calibration = new StackPanel();
        calibration.Children.Add(Ui.Command("\uE714", "校准攻略小地图", CalibrateGuideMap));
        calibration.Children.Add(Ui.Command("\uE714", "校准游戏小地图", CalibrateGameMap));
        setup.Children.Add(new Expander { Header = "手动校准（备用）", Content = calibration, Margin = new(2, 6, 2, 0) });
        panel.Children.Add(new Expander { Header = "地图设置", Content = setup, Margin = new(2, 12, 2, 0) });

        panel.Children.Add(Ui.Heading("调试日志"));
        _mapDebugButton = Ui.Command("\uE7C8", "开始记录日志", () => SetMapDebug(_mapDebug == null));
        panel.Children.Add(_mapDebugButton);
        panel.Children.Add(Ui.Command("\uE8B7", "打开日志目录", OpenMapDebugDirectory));
        _mapDebugStatus.Margin = new(2, 6, 2, 0);
        panel.Children.Add(_mapDebugStatus);
        _mapTimer.Tick += async (_, _) => await SampleGameMapAsync();
        _guideMapTimer.Tick += async (_, _) => await SampleGuideMapAsync();
        _mapVisibilityTimer.Tick += (_, _) => RenderMapPosition();
        Closed += (_, _) =>
        {
            StopMapTracking(); _mapMarker?.Close(); _mapMarker = null;
            if (_mapDebug is { } log) { CompleteMapDebug(log); log.Completion.Wait(TimeSpan.FromSeconds(2)); } _mapDebug = null;
        };
        return panel;
    }

    private Button BuildMapTrackingButton()
    {
        var button = Ui.Command("\uE768", "启动地图跟随", () =>
        {
            if (_mapRunning || _mapLoadCancellation != null) StopMapTracking();
            else StartMapTracking();
        }, primary: true);
        _mapTrackingButtons.Add(button);
        return button;
    }

    private void UpdateMapTrackingButtons()
    {
        var active = _mapRunning || _mapLoadCancellation is { IsCancellationRequested: false };
        foreach (var button in _mapTrackingButtons)
        {
            SetMapButtonLabel(button, active ? "\uE71A" : "\uE768", active ? "停止地图跟随" : "启动地图跟随");
            button.IsEnabled = active || !_mapBusy;
        }
    }

    private static void SetMapButtonLabel(Button button, string glyph, string label)
    {
        var content = (StackPanel)button.Content;
        ((TextBlock)content.Children[0]).Text = glyph;
        ((TextBlock)content.Children[1]).Text = label;
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
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
        _mapBusy = true; UpdateMapTrackingButtons();
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
        finally { _mapBusy = false; UpdateMapTrackingButtons(); }
    }

    private async void CalibrateGameMap()
    {
        if (!CanConfigureMap()) return;
        _mapBusy = true; UpdateMapTrackingButtons();
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
        finally { _mapBusy = false; UpdateMapTrackingButtons(); }
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
        UpdateMapTrackingButtons();
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
        finally { _mapBusy = false; _mapLoadCancellation.Dispose(); _mapLoadCancellation = null; UpdateMapTrackingButtons(); }
    }

    private void StopMapTracking()
    {
        _mapRunning = false; _mapGeneration++; _mapTimer.Stop(); _guideMapTimer.Stop(); _mapVisibilityTimer.Stop();
        _mapLoadCancellation?.Cancel();
        UpdateMapTrackingButtons();
        ResetMapLane(_guideMap); ResetMapLane(_gameMap); HideMapMarker("tracking_stopped");
        TraceMap("tracking_stop");
        DisposeStoppedMap();
        _mapStatus.Text = "地图跟随已停止。";
    }
}
