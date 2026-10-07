using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    // WebView2CompositionControl exposes OpacityMask read-only; mask its WPF video layer instead.
    private readonly Grid _videoSurface = new();
    private readonly Grid _edgeProgress = new() { Height = 14, VerticalAlignment = VerticalAlignment.Bottom,
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed };
    private readonly Border _edgeTrack = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom,
        Background = new SolidColorBrush(Color.FromArgb(200, 28, 32, 34)), IsHitTestVisible = false };
    private readonly Border _edgePlayed = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(56, 218, 179)), IsHitTestVisible = false };
    private readonly TextBlock _edgeTime = new() { HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 8, 18), Padding = new(6, 3, 6, 3),
        FontSize = 11, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 28, 32, 34)),
        IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _xrayTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16.7) };
    private readonly RadialGradientBrush _xrayMask = new()
    {
        MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Pad,
        GradientStops = new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0),
            new(Color.FromArgb(0, 255, 255, 255), 0.7),
            new(Color.FromArgb(40, 255, 255, 255), 0.775),
            new(Color.FromArgb(128, 255, 255, 255), 0.85),
            new(Color.FromArgb(215, 255, 255, 255), 0.925),
            new(Colors.White, 1)
        }
    };
    private bool _edgeSeeking;
    private double _edgeSeekPosition;

    private void BuildSmallWindowControls()
    {
        _xrayTimer.Tick += (_, _) => SampleXRayCursor();
        _browser.SizeChanged += (_, _) => UpdateSmallWindowLayout();
        _edgeProgress.SizeChanged += (_, _) => UpdateEdgeProgress();
        _edgeProgress.Children.Add(_edgeTrack); _edgeProgress.Children.Add(_edgePlayed);
        System.Windows.Automation.AutomationProperties.SetName(_edgeProgress, "小窗视频进度");
        _edgeProgress.MouseEnter += (_, e) => ShowEdgeTime(e.GetPosition(_edgeProgress).X);
        _edgeProgress.MouseLeave += (_, _) => { if (!_edgeSeeking) _edgeTime.Visibility = Visibility.Collapsed; };
        _edgeProgress.MouseMove += (_, e) =>
        {
            var x = e.GetPosition(_edgeProgress).X;
            if (_edgeSeeking) PreviewEdgeSeek(x);
            ShowEdgeTime(x);
        };
        _edgeProgress.MouseLeftButtonDown += (_, e) =>
        {
            if (!_immersive || _duration <= 0) return;
            _edgeSeeking = _edgeProgress.CaptureMouse();
            if (_edgeSeeking) PreviewEdgeSeek(e.GetPosition(_edgeProgress).X);
            e.Handled = true;
        };
        _edgeProgress.MouseLeftButtonUp += (_, e) =>
        {
            if (!_edgeSeeking) return;
            PreviewEdgeSeek(e.GetPosition(_edgeProgress).X);
            var position = _edgeSeekPosition;
            CancelEdgeSeek();
            Fire(() => CommandAsync("position", position));
            e.Handled = true;
        };
        _edgeProgress.LostMouseCapture += (_, _) => CancelEdgeSeek();
        _workspace.Children.Add(_edgeProgress); _workspace.Children.Add(_edgeTime);
    }

    internal void ApplySmallWindowSettings()
    {
        ApplyWindowOpacity(); UpdateSmallWindowLayout(); UpdateXRayTracking();
    }

    private void ApplyWindowOpacity()
    {
        var opacity = _immersive ? _settings.GetImmersiveOpacity() : _settings.Opacity;
        Opacity = _faded ? Math.Min(opacity, 0.25) : opacity;
    }

    private void UpdateSmallWindowLayout()
    {
        var radius = _settings.XRayRadius;
        _xrayMask.RadiusX = _xrayMask.RadiusY = radius;
        // Feather inside the saved outer radius, with a fully transparent core.
        var inner = 1 - Math.Min(radius * 0.3, 32) / radius;
        for (var i = 1; i < _xrayMask.GradientStops.Count; i++)
            _xrayMask.GradientStops[i].Offset = inner + (1 - inner) * (i - 1) / 4;
        UpdateEdgeProgress();
        if (_xrayTimer.IsEnabled) SampleXRayCursor();
    }

    private void UpdateXRayTracking()
    {
        if (_immersive && _settings.XRayEnabled && IsVisible && WindowState != WindowState.Minimized && !_closing)
        {
            _xrayTimer.Start(); SampleXRayCursor();
        }
        else StopXRayTracking();
    }

    private void StopXRayTracking()
    {
        _xrayTimer.Stop(); _videoSurface.OpacityMask = null;
    }

    private void SampleXRayCursor()
    {
        // Transparent pixels may route input to the game, so WPF MouseLeave is not an exit signal.
        UpdateXRayPointer(ReadXRayCursor(out var cursor) ? _browser.PointFromScreen(new(cursor.X, cursor.Y)) : null);
    }

    private void UpdateXRayPointer(Point? pointer)
    {
        if (!_immersive || !_settings.XRayEnabled || !IsVisible || WindowState == WindowState.Minimized
            || _dragHandle.IsDragging || _edgeSeeking || pointer is not { } point
            || !new Rect(5, 5, Math.Max(0, _browser.ActualWidth - 10), Math.Max(0, _browser.ActualHeight - 19)).Contains(point))
        { _videoSurface.OpacityMask = null; return; }
        var grip = new Rect(_dragHandle.TranslatePoint(new Point(), _browser), _dragHandle.RenderSize);
        grip.Inflate(3, 3);
        if (grip.Contains(point)) { _videoSurface.OpacityMask = null; return; }
        if (_xrayMask.Center != point) _xrayMask.Center = _xrayMask.GradientOrigin = point;
        if (_videoSurface.OpacityMask != _xrayMask) _videoSurface.OpacityMask = _xrayMask;
    }

    private static double ProgressFraction(double position, double duration) =>
        double.IsFinite(position) && double.IsFinite(duration) && duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;

    private double EdgePosition(double x) => ProgressFraction(x, _edgeProgress.ActualWidth) * Math.Max(0, _duration);

    private void UpdateEdgeProgress()
    {
        _edgePlayed.Width = _edgeProgress.ActualWidth * ProgressFraction(_edgeSeeking ? _edgeSeekPosition : _position, _duration);
        _edgeProgress.IsEnabled = _duration > 0;
        if (_edgeSeeking) ShowEdgeTime(_edgeProgress.ActualWidth * ProgressFraction(_edgeSeekPosition, _duration));
    }

    private void ShowEdgeTime(double x)
    {
        if (!_immersive || _duration <= 0) return;
        _edgeTime.Text = FormatTime(EdgePosition(x)) + " / " + FormatTime(_duration);
        _edgeTime.Visibility = Visibility.Visible;
    }

    private void PreviewEdgeSeek(double x)
    {
        _edgeSeekPosition = EdgePosition(x); UpdateEdgeProgress();
    }

    private void CancelEdgeSeek()
    {
        _edgeSeeking = false;
        if (_edgeProgress.IsMouseCaptured) _edgeProgress.ReleaseMouseCapture();
        _edgeTime.Visibility = Visibility.Collapsed; UpdateEdgeProgress();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRayCursorPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadXRayCursor(out XRayCursorPoint point);
}
