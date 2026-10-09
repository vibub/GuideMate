using System.Windows.Shapes;
using GuideMate.Vision;

namespace GuideMate.App;

internal sealed partial class VisionCalibrationWindow
{
    private enum SelectionDrag { New, Move, Resize }
    private readonly List<(int X, int Y, Rectangle Handle)> _handles = [];
    private Point? _dragOrigin;
    private ArrowRegion? _dragStartRegion;
    private Rect _dragRect;
    private SelectionDrag _dragKind;
    private int _gripX, _gripY;

    private double SelectionScale()
    {
        var root = (FrameworkElement)Content;
        var origin = _selection.TranslatePoint(new(0, 0), root);
        var unit = _selection.TranslatePoint(new(1, 0), root);
        return Math.Max(0.001, (unit - origin).Length);
    }

    private Rect SelectionRect()
    {
        var r = VideoAnalysis.PixelRegion(_region, _info!);
        return new(r.X, r.Y, r.Width, r.Height);
    }

    private void InitializeSelection()
    {
        foreach (var (x, y) in new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) })
        {
            var handle = new Rectangle { Fill = Brushes.White, Stroke = Brushes.OrangeRed, IsHitTestVisible = false };
            _handles.Add((x, y, handle)); _selection.Children.Add(handle);
        }
        _selection.Focusable = true;
        _selection.ToolTip = "框内拖动移动；拖动边角调整大小；框外拖动重新框选。Esc 取消本次拖动。";
        _selection.MouseLeftButtonDown += (_, e) => { BeginSelectionDrag(e.GetPosition(_selection)); e.Handled = true; };
        _selection.MouseMove += (_, e) =>
        {
            var point = e.GetPosition(_selection);
            if (_dragOrigin != null) UpdateSelectionDrag(point);
            else if (_info != null) SetSelectionCursor(HitSelection(point));
        };
        _selection.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragOrigin == null) return;
            UpdateSelectionDrag(e.GetPosition(_selection)); EndSelectionDrag(false); e.Handled = true;
        };
        _selection.LostMouseCapture += (_, _) => EndSelectionDrag(true);
        _selection.MouseLeave += (_, _) => { if (_dragOrigin == null) _selection.Cursor = Cursors.Cross; };
        PreviewKeyDown += (_, e) =>
        { if (e.Key == Key.Escape && _dragOrigin != null) { EndSelectionDrag(true); e.Handled = true; } };
    }

    private (SelectionDrag Kind, int X, int Y) HitSelection(Point point)
    {
        var rect = SelectionRect(); var tolerance = 7 / SelectionScale();
        foreach (var (x, y, _) in _handles)
        {
            var center = new Point(rect.Left + (x + 1) * rect.Width / 2, rect.Top + (y + 1) * rect.Height / 2);
            if (Math.Abs(point.X - center.X) <= tolerance && Math.Abs(point.Y - center.Y) <= tolerance)
                return (SelectionDrag.Resize, x, y);
        }
        if (point.Y >= rect.Top && point.Y <= rect.Bottom)
        {
            if (Math.Abs(point.X - rect.Left) <= tolerance) return (SelectionDrag.Resize, -1, 0);
            if (Math.Abs(point.X - rect.Right) <= tolerance) return (SelectionDrag.Resize, 1, 0);
        }
        if (point.X >= rect.Left && point.X <= rect.Right)
        {
            if (Math.Abs(point.Y - rect.Top) <= tolerance) return (SelectionDrag.Resize, 0, -1);
            if (Math.Abs(point.Y - rect.Bottom) <= tolerance) return (SelectionDrag.Resize, 0, 1);
        }
        return (rect.Contains(point) ? SelectionDrag.Move : SelectionDrag.New, 0, 0);
    }

    private void SetSelectionCursor((SelectionDrag Kind, int X, int Y) hit) => _selection.Cursor = hit.Kind switch
    {
        SelectionDrag.Move => Cursors.SizeAll,
        SelectionDrag.Resize when hit.X == 0 => Cursors.SizeNS,
        SelectionDrag.Resize when hit.Y == 0 => Cursors.SizeWE,
        SelectionDrag.Resize when hit.X == hit.Y => Cursors.SizeNWSE,
        SelectionDrag.Resize => Cursors.SizeNESW,
        _ => Cursors.Cross
    };

    private void BeginSelectionDrag(Point point)
    {
        if (_busy || _loading || _info == null) return;
        EndSelectionDrag(true);
        (_dragKind, _gripX, _gripY) = HitSelection(point);
        _dragRect = SelectionRect(); _dragStartRegion = _region; _dragOrigin = point;
        _selection.Focus(); _selection.CaptureMouse(); SetSelectionCursor((_dragKind, _gripX, _gripY));
    }

    private void UpdateSelectionDrag(Point point)
    {
        if (_dragOrigin is not { } origin) return;
        var width = _info!.Width; var height = _info.Height;
        var delta = point - origin; var rect = _dragRect; var size = rect.Width;
        double x = rect.X, y = rect.Y;
        if (_dragKind == SelectionDrag.New)
        {
            point = new(Math.Clamp(point.X, 0, width), Math.Clamp(point.Y, 0, height));
            delta = point - origin; size = Math.Min(Math.Abs(delta.X), Math.Abs(delta.Y));
            if (size < 8) return;
            x = delta.X < 0 ? origin.X - size : origin.X; y = delta.Y < 0 ? origin.Y - size : origin.Y;
        }
        else if (_dragKind == SelectionDrag.Move)
        { x = Math.Clamp(x + delta.X, 0, width - size); y = Math.Clamp(y + delta.Y, 0, height - size); }
        else
        {
            var ax = _gripX < 0 ? rect.Right : _gripX > 0 ? rect.Left : rect.Left + size / 2;
            var ay = _gripY < 0 ? rect.Bottom : _gripY > 0 ? rect.Top : rect.Top + size / 2;
            var growth = _gripX == 0 ? _gripY * delta.Y : _gripY == 0 ? _gripX * delta.X
                : Math.Abs(delta.X) >= Math.Abs(delta.Y) ? _gripX * delta.X : _gripY * delta.Y;
            var limitX = _gripX < 0 ? ax : _gripX > 0 ? width - ax : 2 * Math.Min(ax, width - ax);
            var limitY = _gripY < 0 ? ay : _gripY > 0 ? height - ay : 2 * Math.Min(ay, height - ay);
            size = Math.Clamp(size + growth, 8, Math.Min(limitX, limitY));
            x = _gripX < 0 ? ax - size : _gripX > 0 ? ax : ax - size / 2;
            y = _gripY < 0 ? ay - size : _gripY > 0 ? ay : ay - size / 2;
        }
        _region = new(x / width, y / height, size / width, size / height);
        _preset.SelectedItem = "手动选区";
        DrawRegion(); PreviewAngle = null; _analyze.IsEnabled = false;
        _result.Text = "正在调整选区，松开鼠标更新识别结果";
    }

    private void EndSelectionDrag(bool cancel)
    {
        if (_dragOrigin == null) return;
        if (cancel) _region = _dragStartRegion!;
        _dragOrigin = null; _dragStartRegion = null;
        if (_selection.IsMouseCaptured) _selection.ReleaseMouseCapture();
        _selection.Cursor = Cursors.Cross; DrawRegion(); DetectPreview();
    }

    private void DrawSelectionHandles(OpenCvSharp.Rect rect)
    {
        var scale = SelectionScale(); var size = 10 / scale;
        _regionOutline.StrokeThickness = 2 / scale;
        foreach (var (x, y, handle) in _handles)
        {
            handle.Width = handle.Height = size; handle.StrokeThickness = 1.5 / scale;
            Canvas.SetLeft(handle, rect.X + (x + 1) * rect.Width / 2 - size / 2);
            Canvas.SetTop(handle, rect.Y + (y + 1) * rect.Height / 2 - size / 2);
        }
    }
}
