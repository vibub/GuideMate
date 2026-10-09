using System.Text.Json;
using GuideMate.Vision;
using System.Windows.Threading;
using Cv = OpenCvSharp;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly TextBlock _visionSummary = Ui.Text("未加载视觉方向", 12, Ui.Muted);
    private VisualDirectionTrack? _visualTrack;
    private CheckBox _visionToggle = null!;
    private readonly DispatcherTimer _onlineVisionTimer = new() { Interval = TimeSpan.FromSeconds(0.5) };
    private OnlineVisionProfile? _onlineProfile;
    private string _onlineProfileUrl = "";
    private DirectionHint? _onlineHint;
    private double _onlineTime;
    private long _onlineSampleTick;
    private int _onlineGeneration, _videoWidth, _videoHeight;
    private bool _onlineBusy, _videoSeeking;
    private string _onlineMethod = "";

    private FrameworkElement BuildVisionPanel()
    {
        var panel = new StackPanel { Margin = new(16, 0, 16, 12) };
        panel.Children.Add(Ui.Heading("攻略画面方向"));
        panel.Children.Add(Ui.Text("框选玩家箭头后自动识别原神或终末地，也可手动选择。", 12, Ui.Muted));
        panel.Children.Add(_visionSummary);
        panel.Children.Add(Ui.Command("\uE9D9", "校准并分析本地视频", OpenVisionAnalysis));
        panel.Children.Add(Ui.Command("\uE714", "校准在线视频", OpenOnlineCalibration));
        panel.Children.Add(Ui.Text("选区会长期保留，切集、换网址或清晰度均沿用；仅在保存新校准时更改。", 12, Ui.Muted));
        _visionToggle = Ui.Toggle("使用视觉方向", _settings.UseVisualDirection, value =>
        {
            _settings.UseVisualDirection = value;
            if (!value) InvalidateOnlineSample();
            UpdateDirection();
        });
        panel.Children.Add(_visionToggle);
        _onlineVisionTimer.Tick += async (_, _) => await SampleOnlineDirectionAsync();
        _onlineVisionTimer.Start();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async void OpenVisionAnalysis()
    {
        if (_localFile == null) { _status.Text = "未打开本地视频"; return; }
        try
        {
            var ffmpeg = _settings.FfmpegPath;
            if (ffmpeg == null || !File.Exists(ffmpeg)) ffmpeg = VideoAnalysis.FindFfmpeg();
            if (ffmpeg == null)
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择已安装的 ffmpeg.exe", Filter = "FFmpeg|ffmpeg.exe" };
                if (picker.ShowDialog(this) != true) return;
                ffmpeg = picker.FileName;
            }
            _settings.FfmpegPath = ffmpeg;
            var source = _localFile;
            await CommandAsync("pause");
            var previous = _visualTrack is { } track
                ? new OnlineVisionProfile(0, 0, track.Region, track.NorthLocked, track.NorthAngle, track.Game) : null;
            var dialog = new VisionCalibrationWindow(this, ffmpeg, source, _position, onlineProfile: previous);
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            VisualDirectionCache.Save(_dataPath, dialog.Result);
            if (!string.Equals(_localFile, source, StringComparison.OrdinalIgnoreCase)) return;
            _visualTrack = dialog.Result; _visionToggle.IsChecked = true;
            UpdateVisionSummary(); UpdateDirection(); SaveSettings();
        }
        catch (Exception ex) { if (!_closing) _status.Text = "视觉分析失败：" + ex.Message; }
    }

    private void LoadVisualTrack()
    {
        ResetOnlineVision();
        _visualTrack = null;
        try { if (_localFile != null && File.Exists(_localFile)) _visualTrack = VisualDirectionCache.Load(_dataPath, _localFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        { _notice = "视觉缓存读取失败：" + ex.Message; }
        UpdateVisionSummary(); UpdateDirection();
    }

    private static string GameLabel(VisionGame game) => game == VisionGame.Endfield ? "终末地" : "原神";

    private void UpdateVisionSummary() => _visionSummary.Text = _onlineProfile != null
        ? GameLabel(_onlineProfile.Game) + " · 在线视频已校准 · 随播放实时识别\n" + (_onlineProfile.NorthLocked ? "固定北向地图" : "画面角度 · 非绝对方位")
        : _visualTrack == null ? "未加载视觉方向"
        : $"{GameLabel(_visualTrack.Game)} · {Path.GetFileName(_visualTrack.SourcePath)}\n识别帧 {_visualTrack.Frames.Count(f => f.Angle != null)}/{_visualTrack.Frames.Count} · 间隔 {_visualTrack.Interval:0.0} 秒\n"
            + (_visualTrack.NorthLocked ? "固定北向地图" : "画面角度 · 非绝对方位");

    private DirectionHint? CurrentDirection()
    {
        if (_settings.UseVisualDirection && _onlineProfile != null)
            return !_videoSeeking && IsVisible && WindowState != WindowState.Minimized
                && Environment.TickCount64 - _onlineSampleTick < 1600
                && Math.Abs(_position - _onlineTime) < Math.Max(0.85, _settings.Rate * 0.85) ? _onlineHint : null;
        return _settings.UseVisualDirection && _visualTrack != null
            ? (_duration > 0 ? _visualTrack.HintAt(_position) : null) : DirectionAnalyzer.Analyze(_lastCaption);
    }

    private void UpdateDirection()
    {
        var hint = CurrentDirection();
        _direction.Text = hint?.Label ?? "—";
        _directionKind.Text = hint?.Category ?? (_settings.UseVisualDirection && (_visualTrack != null || _onlineProfile != null) ? "攻略画面：未识别" : "等待方向提示");
        _overlay?.Update(_lastCaption, hint);
    }

    private void InvalidateOnlineSample()
    {
        _onlineGeneration++; _onlineHint = null; _onlineSampleTick = 0;
    }

    private void ResetOnlineVision()
    {
        InvalidateOnlineSample(); _onlineProfile = null; _onlineProfileUrl = ""; _onlineMethod = "";
    }

    private void UpdateOnlineVisionState(JsonElement state)
    {
        var width = (int)Finite(state, "width", 0); var height = (int)Finite(state, "height", 0);
        _videoSeeking = state.TryGetProperty("seeking", out var seeking) && seeking.GetBoolean();
        if (_videoSeeking) InvalidateOnlineSample();
        if (_localFile != null) return;
        if (_videoWidth != width || _videoHeight != height || _onlineProfileUrl != _url)
        {
            ResetOnlineVision(); _videoWidth = width; _videoHeight = height; _onlineProfileUrl = _url;
            if (width > 0 && height > 0 && _settings.OnlineVisionCalibration is { } profile)
                _onlineProfile = profile with { Width = width, Height = height };
            UpdateVisionSummary();
        }
    }

    private async void OpenOnlineCalibration()
    {
        if (_localFile != null) { _status.Text = "本地视频请使用“校准并分析本地视频”。"; return; }
        if (_onlineBusy) return;
        _onlineBusy = true;
        var url = _url; var generation = _onlineGeneration;
        try
        {
            await CommandAsync("pause");
            var frame = await CaptureOnlineFrameAsync();
            if (frame == null) { _status.Text = "暂无可校准的视频画面，请等待视频加载或跳转完成。"; return; }
            if (generation != _onlineGeneration || url != _url || frame.MediaKey != _mediaKey) return;
            var dialog = new VisionCalibrationWindow(this, "", "在线视频", frame.Time, frame, _onlineProfile);
            if (dialog.ShowDialog() != true || dialog.OnlineResult == null) return;
            if (generation != _onlineGeneration || url != _url || frame.MediaKey != _mediaKey)
            { _status.Text = "视频已切换，请重新校准当前视频。"; return; }
            ApplyOnlineCalibration(dialog.OnlineResult);
            _status.Text = "在线视频方向已启用，继续播放即可同步识别。";
        }
        catch (Exception ex) { if (!_closing) _status.Text = "在线视频校准失败：" + ex.Message; }
        finally { _onlineBusy = false; }
    }

    private void ApplyOnlineCalibration(OnlineVisionProfile profile)
    {
        InvalidateOnlineSample();
        _settings.OnlineVisionProfiles[_url] = profile;
        _settings.OnlineVisionCalibration = profile;
        _onlineProfile = profile; _onlineProfileUrl = _url;
        _settings.UseVisualDirection = true; _visionToggle.IsChecked = true;
        UpdateVisionSummary(); UpdateDirection(); SaveSettings();
    }

    private async Task SampleOnlineDirectionAsync()
    {
        if (_onlineBusy || _closing || _localFile != null || !_settings.UseVisualDirection || _onlineProfile == null
            || _videoSeeking || !IsVisible || WindowState == WindowState.Minimized) return;
        _onlineBusy = true;
        var generation = _onlineGeneration; var profile = _onlineProfile;
        var key = _mediaKey; var activeFrame = _videoRouter!.ActiveFrame;
        try
        {
            var frame = await CaptureOnlineFrameAsync(profile.Region);
            if (generation != _onlineGeneration || key != _mediaKey || activeFrame != _videoRouter.ActiveFrame || _closing) return;
            if (frame == null) { InvalidateOnlineSample(); UpdateDirection(); return; }
            if (frame.MediaKey != key || frame.Width != profile.Width || frame.Height != profile.Height) return;
            var detection = await Task.Run(() =>
            {
                using var image = Cv.Cv2.ImDecode(frame.Image, Cv.ImreadModes.Color);
                return ArrowDetector.Detect(image, lowResolution: frame.DetailPixels < 60, game: profile.Game);
            });
            if (generation != _onlineGeneration || key != _mediaKey || activeFrame != _videoRouter.ActiveFrame || _closing
                || !IsVisible || WindowState == WindowState.Minimized) return;
            var track = new VisualDirectionTrack { Game = profile.Game, NorthLocked = profile.NorthLocked, NorthAngle = profile.NorthAngle,
                Frames = [new(frame.Time, detection?.Angle, detection?.Score ?? 0)] };
            _onlineHint = track.HintAt(frame.Time) is { } hint ? hint with { Category = "攻略箭头 · 在线视觉" } : null;
            _onlineTime = frame.Time; _onlineSampleTick = Environment.TickCount64; _onlineMethod = frame.Method;
            UpdateVisionSummary(); UpdateDirection();
        }
        catch (Exception ex)
        {
            if (generation == _onlineGeneration && !_closing)
            { InvalidateOnlineSample(); UpdateDirection(); _visionSummary.Text = "在线视频取帧失败：" + ex.Message; }
        }
        finally { _onlineBusy = false; }
    }
}
