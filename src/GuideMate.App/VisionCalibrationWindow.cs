using System.Globalization;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GuideMate.Vision;
using Cv = OpenCvSharp;

namespace GuideMate.App;

internal sealed partial class VisionCalibrationWindow : Window
{
    private readonly string _ffmpeg, _path;
    private readonly double _startTime;
    private readonly Grid _picture = new();
    private readonly Image _preview = new();
    private readonly Canvas _selection = new() { Background = Brushes.Transparent };
    private readonly Rectangle _regionOutline = new() { Stroke = Brushes.OrangeRed, StrokeThickness = 4, Fill = new SolidColorBrush(Color.FromArgb(25, 255, 90, 30)), IsHitTestVisible = false };
    private readonly TextBlock _result = Ui.Text("正在读取视频…", 12, Ui.Muted);
    private readonly TextBlock _coordinates = Ui.Text("", 12, Ui.Muted);
    private readonly Slider _time = new() { Minimum = 0, Maximum = 1 };
    private readonly TextBlock _clock = Ui.Text("", 12, Ui.Muted);
    private readonly CheckBox _northLocked = Ui.Toggle("地图北向固定", true, _ => { });
    private readonly TextBox _northAngle = new() { Text = "0", Width = 64 };
    private readonly ComboBox _game = new() { Width = 140, Margin = new(8, 0, 14, 0) };
    private readonly TextBlock _gameResult = Ui.Text("框选完整的玩家箭头后自动识别游戏，也可手动选择。", 12, Ui.Muted);
    private VisionGame? _detectedGame;
    private VisionGame? Game => _game.SelectedIndex > 0 ? (VisionGame)(_game.SelectedIndex - 1) : _detectedGame;
    private readonly ComboBox _preset = new() { Width = 154 };
    private VisionGame _presetGame;
    private bool _updatingPresets;
    private readonly ProgressBar _progress = new() { Height = 5, Minimum = 0, Maximum = 100 };
    private readonly Button _analyze;
    private readonly Button _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _analysis;
    private VideoInfo? _info;
    private Cv.Mat? _frame;
    private ArrowRegion _region = new(0, 0, 1, 1);
    private bool _busy, _closed, _loading;
    private readonly OnlineVideoFrame? _onlineFrame;
    public VisualDirectionTrack? Result { get; private set; }
    public OnlineVisionProfile? OnlineResult { get; private set; }
    internal bool PreviewReady => _preview.Source != null && !_loading;
    internal double? PreviewAngle { get; private set; }
    internal void SelectGame(VisionGame game) => _game.SelectedIndex = (int)game + 1;

    public VisionCalibrationWindow(Window owner, string ffmpeg, string path, double startTime,
        OnlineVideoFrame? onlineFrame = null, OnlineVisionProfile? onlineProfile = null, VisionGame game = VisionGame.Genshin)
    {
        Owner = owner; _ffmpeg = ffmpeg; _path = path; _startTime = startTime;
        _onlineFrame = onlineFrame;
        Title = onlineFrame == null ? "随引 · 本地视频视觉校准" : "随引 · 在线视频视觉校准";
        Width = 1000; Height = 760; MinWidth = 680; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var root = new Grid { Margin = new(16), Background = Brushes.White };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        for (var i = 0; i < 4; i++) root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(Ui.Text(System.IO.Path.GetFileName(path), 16));
        _picture.Children.Add(_preview); _picture.Children.Add(_selection); _selection.Children.Add(_regionOutline); InitializeSelection();
        var viewbox = new Viewbox { Child = _picture, Stretch = Stretch.Uniform, Margin = new(0, 10, 0, 10) };
        Grid.SetRow(viewbox, 1); root.Children.Add(viewbox);
        viewbox.SizeChanged += (_, _) => { EndSelectionDrag(true); DrawRegion(); };
        var seek = new Grid(); seek.ColumnDefinitions.Add(new()); seek.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); seek.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _clock.Margin = new(12, 0, 8, 0); Grid.SetColumn(_clock, 1); seek.Children.Add(_clock); seek.Children.Add(_time);
        _refresh = Ui.Icon("\uE72C", "读取所选时间的画面", () => _ = RefreshAsync());
        Grid.SetColumn(_refresh, 2); seek.Children.Add(_refresh); Grid.SetRow(seek, 2); root.Children.Add(seek);
        if (onlineFrame != null) seek.Visibility = Visibility.Collapsed;
        _time.ValueChanged += (_, _) => _clock.Text = TimeSpan.FromSeconds(_time.Value).ToString(@"mm\:ss");
        var controls = new WrapPanel { Margin = new(0, 10, 0, 4) };
        controls.Children.Add(Ui.Text("游戏", 12));
        _game.Items.Add("自动识别"); _game.Items.Add("原神"); _game.Items.Add("终末地"); controls.Children.Add(_game);
        _game.ToolTip = "自动识别原神或终末地；手动选择后不再自动更改，保存校准后生效";
        controls.Children.Add(Ui.Text("箭头区域", 12)); _preset.Margin = new(8, 0, 14, 0);
        _preset.ToolTip = "在画面中拖动框选玩家箭头，选区需保留四周边距";
        controls.Children.Add(_preset); controls.Children.Add(_northLocked);
        controls.Children.Add(new TextBlock { Text = "北向角度", Margin = new(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(_northAngle); _northAngle.ToolTip = "从画面正上方向顺时针量出的北向角度；旋转小地图需取消北向固定";
        _coordinates.Margin = new(12, 0, 0, 0); controls.Children.Add(_coordinates);
        Grid.SetRow(controls, 3); root.Children.Add(controls);
        _preset.SelectionChanged += (_, _) =>
        { if (!_updatingPresets && _preset.SelectedItem as string != "手动选区") { EndSelectionDrag(true); ApplyPreset(); } };
        UpdateGamePresets(onlineProfile?.Game ?? game);
        _game.SelectionChanged += (_, _) =>
        {
            _detectedGame = null;
            if (Game is { } selected) UpdateGamePresets(selected);
            _gameResult.Text = Game is { } manual ? $"已手动选择：{GameLabel(manual)} · 选择“自动识别”可重新判断"
                : "框选完整的玩家箭头后自动识别游戏，也可手动选择。";
            DetectPreview();
        };
        _game.SelectedIndex = 0;
        _northLocked.Checked += (_, _) => DetectPreview(); _northLocked.Unchecked += (_, _) => DetectPreview();
        _northAngle.LostFocus += (_, _) => DetectPreview();
        var results = new StackPanel { Margin = new(0, 8, 0, 8) };
        results.Children.Add(_gameResult); results.Children.Add(_result);
        Grid.SetRow(results, 4); root.Children.Add(results);
        var footer = new Grid { Margin = new(0, 4, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _progress.VerticalAlignment = VerticalAlignment.Center; _progress.Margin = new(0, 0, 16, 0); footer.Children.Add(_progress);
        _analyze = Ui.Command("\uE768", onlineFrame == null ? "分析完整视频" : "保存并启用", () => _ = AnalyzeAsync()); _analyze.IsEnabled = false;
        Grid.SetColumn(_analyze, 1); footer.Children.Add(_analyze);
        var cancel = Ui.Command("\uE711", "取消", () => { if (_busy) _analysis?.Cancel(); else Close(); });
        Grid.SetColumn(cancel, 2); footer.Children.Add(cancel); Grid.SetRow(footer, 5); root.Children.Add(footer);
        Content = root;
        Loaded += async (_, _) =>
        {
            try
            {
                _info = onlineFrame == null ? await VideoAnalysis.ProbeAsync(_ffmpeg, _path, _lifetime.Token)
                    : new VideoInfo(onlineFrame.Width, onlineFrame.Height, 1);
                _picture.Width = _info.Width; _picture.Height = _info.Height;
                _preview.Width = _info.Width; _preview.Height = _info.Height;
                _selection.Width = _info.Width; _selection.Height = _info.Height;
                _time.Maximum = Math.Max(0, _info.Duration - 0.1); _time.Value = Math.Clamp(_startTime > 0 ? _startTime : 60, 0, _time.Maximum);
                if (onlineProfile != null)
                {
                    _region = onlineProfile.Region; _northLocked.IsChecked = onlineProfile.NorthLocked;
                    _northAngle.Text = onlineProfile.NorthAngle.ToString(CultureInfo.InvariantCulture);
                    _preset.SelectedItem = "手动选区";
                }
                else ApplyPreset();
                DrawRegion();
                if (onlineFrame == null) await RefreshAsync();
                else { SetPreview(onlineFrame.Image); DetectPreview(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed) _result.Text = "读取失败：" + ex.Message; }
        };
        Deactivated += (_, _) => EndSelectionDrag(true);
        Closed += (_, _) => { EndSelectionDrag(true); _closed = true; _lifetime.Cancel(); _analysis?.Cancel(); _frame?.Dispose(); _lifetime.Dispose(); };
    }

    private static string GameLabel(VisionGame game) => game == VisionGame.Endfield ? "终末地" : "原神";

    private void UpdateGamePresets(VisionGame game)
    {
        if (_preset.Items.Count > 0 && _presetGame == game) return;
        _updatingPresets = true;
        _presetGame = game;
        _preset.Items.Clear();
        foreach (var label in game == VisionGame.Endfield
            ? new[] { "月月放大小地图", "手动选区" }
            : new[] { "作者放大小地图", "原始游戏小地图", "手动选区" }) _preset.Items.Add(label);
        // Rebuilding game-specific presets must not move the user's calibration box.
        _preset.SelectedItem = _info != null ? "手动选区" : (string)_preset.Items[0];
        _updatingPresets = false;
    }

    private void ApplyPreset()
    {
        if (_info == null || _preset.SelectedIndex < 0 || _preset.SelectedItem as string == "手动选区") return;
        var x = _preset.SelectedIndex == 0 ? 280 / 1920d : 160 / 1920d;
        var y = _preset.SelectedIndex == 0 ? 275 / 1080d : 160 / 1080d;
        var size = _info.Height * (_preset.SelectedIndex == 0 ? 140 / 1080d : 80 / 1080d);
        _region = new(x - size / _info.Width / 2, y - size / _info.Height / 2, size / _info.Width, size / _info.Height);
        DrawRegion(); DetectPreview();
    }

    private void DrawRegion()
    {
        if (_info == null) return;
        var rect = VideoAnalysis.PixelRegion(_region, _info);
        Canvas.SetLeft(_regionOutline, rect.X); Canvas.SetTop(_regionOutline, rect.Y);
        _regionOutline.Width = rect.Width; _regionOutline.Height = rect.Height;
        _coordinates.Text = $"{rect.X}, {rect.Y} · {rect.Width}×{rect.Height}";
        DrawSelectionHandles(rect);
    }

    private async Task RefreshAsync()
    {
        if (_info == null || _loading || _busy) return;
        EndSelectionDrag(true);
        _loading = true; _refresh.IsEnabled = false; _analyze.IsEnabled = false;
        try
        {
            var bytes = await VideoAnalysis.PreviewAsync(_ffmpeg, _path, _time.Value, _lifetime.Token);
            if (_closed) return;
            SetPreview(bytes);
            DetectPreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) _result.Text = "预览失败：" + ex.Message; }
        finally { _loading = false; _refresh.IsEnabled = !_closed; }
    }

    private void SetPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        _preview.Source = image; _frame?.Dispose(); _frame = Cv.Cv2.ImDecode(bytes, Cv.ImreadModes.Color);
    }

    private void DetectPreview()
    {
        if (_frame == null || _info == null || _busy || _closed) return;
        PreviewAngle = null; _analyze.IsEnabled = false;
        var automatic = _game.SelectedIndex == 0;
        if (automatic)
        {
            _detectedGame = null;
            _gameResult.Text = "自动识别：未能确定，请框选清晰完整的玩家箭头，或手动选择游戏";
        }
        if (Math.Min(_region.Width * _frame.Width, _region.Height * _frame.Height) < 8)
        { PreviewAngle = null; _result.Text = "预览中的选区过小，请扩大选区"; return; }
        using var crop = new Cv.Mat(_frame, VideoAnalysis.PixelRegion(_region, new VideoInfo(_frame.Width, _frame.Height, 1)));
        using var scaled = new Cv.Mat(); Cv.Cv2.Resize(crop, scaled, new(160, 160));
        var lowResolution = _onlineFrame != null && _onlineFrame.DetailPixels * _region.Width < 60;
        ArrowDetection? detected;
        if (automatic)
        {
            var match = VisionGameDetector.Detect(scaled, lowResolution);
            if (match == null) { _result.Text = "当前帧：未识别到唯一的玩家箭头"; return; }
            _detectedGame = match.Game; detected = match.Arrow;
            UpdateGamePresets(match.Game);
            _gameResult.Text = $"自动识别：{GameLabel(match.Game)} · 可在“游戏”中手动更改";
        }
        else detected = ArrowDetector.Detect(scaled, lowResolution, Game!.Value);
        if (!double.TryParse(_northAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var north) || !double.IsFinite(north)) { _result.Text = "北向角度无效"; return; }
        _analyze.IsEnabled = true;
        PreviewAngle = detected?.Angle;
        if (detected == null) { _result.Text = "当前帧：未识别到玩家箭头"; return; }
        var angle = VisualDirectionTrack.Normalize(detected.Angle - (_northLocked.IsChecked == true ? north : 0));
        _result.Text = $"当前帧：{angle:0}° · 几何得分 {detected.Score:0.00}";
    }

    private async Task AnalyzeAsync()
    {
        if (_info == null || _busy) return;
        if (Game is not { } game) { _result.Text = "尚未确定游戏，请手动选择原神或终末地后再保存"; return; }
        if (!double.TryParse(_northAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var north) || !double.IsFinite(north)) { _result.Text = "北向角度无效"; return; }
        if (_onlineFrame != null)
        {
            OnlineResult = new(_onlineFrame.Width, _onlineFrame.Height, _region, _northLocked.IsChecked == true, north, game);
            DialogResult = true; return;
        }
        _busy = true; _analyze.IsEnabled = _refresh.IsEnabled = _game.IsEnabled = _preset.IsEnabled = _northAngle.IsEnabled = _northLocked.IsEnabled = _time.IsEnabled = false;
        _analysis = new();
        var progress = new Progress<AnalysisProgress>(p =>
        {
            if (_closed) return;
            _progress.Value = p.Time / p.Duration * 100;
            _result.Text = $"已处理 {TimeSpan.FromSeconds(p.Time):mm\\:ss} / {TimeSpan.FromSeconds(p.Duration):mm\\:ss} · 识别帧 {p.Recognized}/{p.Total}";
        });
        try
        {
            var region = _region; var locked = _northLocked.IsChecked == true;
            Result = await Task.Run(() => VideoAnalysis.AnalyzeAsync(_ffmpeg, _path, _info, region, locked, north, progress, _analysis.Token, game));
            if (!_closed) { DialogResult = true; }
        }
        catch (OperationCanceledException) { if (!_closed) _result.Text = "分析已取消，未替换已有时间轴"; }
        catch (Exception ex) { if (!_closed) _result.Text = "分析失败：" + ex.Message; }
        finally
        {
            _analysis.Dispose(); _analysis = null; _busy = false;
            if (!_closed) _analyze.IsEnabled = _refresh.IsEnabled = _game.IsEnabled = _preset.IsEnabled = _northAngle.IsEnabled = _northLocked.IsEnabled = _time.IsEnabled = true;
            if (!_closed) DetectPreview();
        }
    }
}
