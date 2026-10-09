using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private Thumb? _resizeGrip;
    private readonly List<Thumb> _resizeGrips = [];
    private ImmersiveDragPump? _resizePump;
    private WindowPlacement _resizeOrigin = new(0, 0, 1, 1);
    private Point _resizeOriginMouse;
    private ResizeEdges _resizeEdges;
    private double? _resizeAspectRatio;
    private double? _videoAspectRatio;

    private void BuildWindowResizeControls()
    {
        AddResizeGrip(ResizeEdges.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch,
            6, double.NaN, Cursors.SizeWE, "左边", new(0, 18, 0, 18));
        AddResizeGrip(ResizeEdges.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch,
            6, double.NaN, Cursors.SizeWE, "右边", new(0, 18, 0, 18));
        AddResizeGrip(ResizeEdges.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top,
            double.NaN, 6, Cursors.SizeNS, "上边", new(18, 0, 18, 0));
        AddResizeGrip(ResizeEdges.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
            double.NaN, 6, Cursors.SizeNS, "下边", new(18, 0, 18, 0));
        AddResizeGrip(ResizeEdges.Left | ResizeEdges.Top, HorizontalAlignment.Left,
            VerticalAlignment.Top, 18, 18, Cursors.SizeNWSE, "左上角");
        AddResizeGrip(ResizeEdges.Right | ResizeEdges.Top, HorizontalAlignment.Right,
            VerticalAlignment.Top, 18, 18, Cursors.SizeNESW, "右上角");
        AddResizeGrip(ResizeEdges.Left | ResizeEdges.Bottom, HorizontalAlignment.Left,
            VerticalAlignment.Bottom, 18, 18, Cursors.SizeNESW, "左下角");
        AddResizeGrip(ResizeEdges.Right | ResizeEdges.Bottom, HorizontalAlignment.Right,
            VerticalAlignment.Bottom, 18, 18, Cursors.SizeNWSE, "右下角");
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(OnResizeHitTest);
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelWindowResize(); };
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Normal) CancelWindowResize();
            foreach (var grip in _resizeGrips) grip.Visibility = WindowState == WindowState.Normal ? Visibility.Visible : Visibility.Collapsed;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _resizeGrip != null) { CancelWindowResize(); e.Handled = true; } };
    }
    private void AddResizeGrip(ResizeEdges edges, HorizontalAlignment horizontal, VerticalAlignment vertical,
        double width, double height, Cursor cursor, string name, Thickness margin = default)
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        // Non-zero alpha keeps the transparent window's outer pixels hit-testable.
        surface.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        if (edges == (ResizeEdges.Right | ResizeEdges.Bottom))
        {
            var icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            icon.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 3,13 L 13,3 M 7,13 L 13,7 M 11,13 L 13,11"));
            icon.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brushes.SlateGray);
            icon.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.5);
            icon.SetValue(UIElement.IsHitTestVisibleProperty, false);
            surface.AppendChild(icon);
        }
        var grip = new Thumb { Width = width, Height = height, HorizontalAlignment = horizontal,
            VerticalAlignment = vertical, Margin = margin, Cursor = cursor, Focusable = false,
            ToolTip = "调整窗口大小：" + name, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = surface } };
        System.Windows.Automation.AutomationProperties.SetName(grip, "窗口缩放：" + name);
        grip.DragStarted += (_, _) => BeginWindowResize(grip, edges);
        grip.DragCompleted += (_, e) => CompleteWindowResize(e.Canceled);
        _resizeGrips.Add(grip);
        Grid.SetRowSpan(grip, _root.RowDefinitions.Count); Panel.SetZIndex(grip, 50); _root.Children.Add(grip);
    }
    private nint OnResizeHitTest(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0084 || _through || !ReadDragWindowRect(handle, out var rect)) return 0;
        // Signed screen coordinates also work on monitors left/above the primary display.
        var packed = lParam.ToInt64(); var x = (short)(packed & 0xFFFF); var y = (short)((packed >> 16) & 0xFFFF);
        if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) return 0;
        // All borders belong to WPF grips. The ordinary title still explicitly calls DragMove.
        handled = true; return 1; // HTCLIENT, never a native sizing border.
    }

    private void BeginWindowResize(Thumb grip, ResizeEdges edges)
    {
        CancelWindowResize();
        if (_closing || !IsVisible || WindowState != WindowState.Normal || _through) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (!ReadDragWindowRect(handle, out var rect) || !ReadXRayCursor(out var cursor)) return;
        CancelImmersiveDrag(); CancelEdgeSeek(); UpdateXRayPointer(null);
        _resizeGrip = grip; _resizeEdges = edges;
        _resizeOrigin = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        _resizeOriginMouse = new(cursor.X, cursor.Y);
        _resizeAspectRatio = _immersive ? ImmersiveAspectRatio : null;
        _resizePump = new(Dispatcher, OnWindowResizeTick);
    }
    private void OnWindowResizeTick()
    {
        if (_resizeGrip == null || !_resizeGrip.IsDragging || _closing || !IsVisible || WindowState != WindowState.Normal)
        { CancelWindowResize(); return; }
        var button = ReadDragSystemMetric(23) == 0 ? 0x01 : 0x02;
        if (ReadDragButtonState(button) >= 0) { CompleteWindowResize(false); return; }
        ApplyWindowResizeCursor();
    }
    private void ApplyWindowResizeCursor()
    {
        if (ReadXRayCursor(out var cursor)) ResizeWindowAt(new(cursor.X, cursor.Y));
    }
    private void ResizeWindowAt(Point cursor)
    {
        if (_resizeGrip == null) return;
        var handle = new WindowInteropHelper(this).Handle;
        var transform = HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice;
        var bounds = WindowResizing.Calculate(_resizeOrigin, _resizeEdges,
            cursor.X - _resizeOriginMouse.X, cursor.Y - _resizeOriginMouse.Y,
            MinWidth * transform.M11, MinHeight * transform.M22, _resizeAspectRatio);
        // Update both dimensions and coordinates in one native call, without SC_SIZE/SC_MOVE or Snap.
        MoveDragWindow(handle, 0, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014);
    }
    private void CompleteWindowResize(bool canceled)
    {
        if (_resizeGrip is not { } grip) return;
        _resizePump?.Dispose(); _resizePump = null;
        if (!canceled && !_closing && IsVisible && WindowState == WindowState.Normal) ApplyWindowResizeCursor();
        _resizeGrip = null; grip.CancelDrag();
        if (!canceled) SaveSettings();
    }
    private void CancelWindowResize() => CompleteWindowResize(true);

    private double ImmersiveAspectRatio => _videoAspectRatio
        ?? (_settings.ImmersiveBounds is { } saved ? saved.Width / saved.Height : 16d / 9);

    private void ResetVideoAspectRatio()
    {
        CancelWindowResize();
        _videoAspectRatio = null;
    }

    private void UpdateVideoAspectRatio(JsonElement state)
    {
        var width = Finite(state, "width", 0); var height = Finite(state, "height", 0);
        if (width <= 0 || height <= 0) return;
        var ratio = width / height;
        if (!double.IsFinite(ratio) || ratio <= 0 || _videoAspectRatio == ratio) return;
        CancelWindowResize();
        _videoAspectRatio = ratio;
        if (_immersive && WindowState == WindowState.Normal) ApplyImmersiveAspectRatio();
    }

    private void ApplyImmersiveAspectRatio()
    {
        var bounds = WindowResizing.Calculate(new(Left, Top, Width, Height), ResizeEdges.Right,
            0, 0, MinWidth, MinHeight, ImmersiveAspectRatio);
        Width = bounds.Width; Height = bounds.Height;
    }
}
