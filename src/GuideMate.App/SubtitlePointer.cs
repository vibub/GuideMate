using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GuideMate.App;

internal sealed partial class SubtitleWindow
{
    private readonly DispatcherTimer _pointerTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _buttonWasDown, _pointerHeld, _pointerDragging, _pointerResizing;
    private int _pointerButton;
    private long _pointerPressedAt;
    private nint _pointerForeground;
    private Point _pointerOrigin, _pointerOffset;
    private double _pointerWidth;

    private void InitializePointerGesture()
    {
        _pointerTimer.Tick += (_, _) => PollPointerGesture();
        IsVisibleChanged += (_, _) => UpdatePointerTracking();
        Closed += (_, _) => { _pointerTimer.Stop(); CancelPointerGesture(); };
    }

    private void UpdatePointerTracking()
    {
        _pointerTimer.Stop(); CancelPointerGesture();
        if (!IsVisible) return;
        _pointerButton = PointerSystemMetric(23) == 0 ? 1 : 2; // GetAsyncKeyState uses physical buttons.
        _buttonWasDown = PointerButtonState(_pointerButton) < 0; // Do not grab a press begun before showing.
        _pointerTimer.Start();
    }

    private void PollPointerGesture()
    {
        var down = PointerButtonState(_pointerButton) < 0;
        if (down == _buttonWasDown && !_pointerHeld) return;
        if (!PointerCursor(out var cursor)) { CancelPointerGesture(); return; }
        SamplePointerGesture(down, new(cursor.X, cursor.Y), Environment.TickCount64, PointerForeground());
    }

    private void SamplePointerGesture(bool down, Point cursor, long timestamp, nint foreground)
    {
        if (!down)
        {
            _buttonWasDown = false;
            var completed = _pointerHeld && _pointerDragging && IsVisible && foreground == _pointerForeground;
            if (completed) ApplyPointerPosition(cursor);
            CancelPointerGesture();
            if (completed) PlacementChanged?.Invoke();
            return;
        }
        if (!_buttonWasDown)
        {
            _buttonWasDown = true;
            if (!IsVisible || !new Rect(RenderSize).Contains(PointFromScreen(cursor))) return;
            var handle = new WindowInteropHelper(this).Handle;
            if (!PointerBounds(handle, out var bounds)) return;
            _pointerHeld = true; _pointerOrigin = cursor; _pointerPressedAt = timestamp;
            _pointerForeground = foreground; _pointerWidth = ActualWidth;
            _pointerOffset = new(cursor.X - bounds.Left, cursor.Y - bounds.Top);
            _pointerResizing = new Rect(_widthGrip.TranslatePoint(new(), this), _widthGrip.RenderSize).Contains(PointFromScreen(cursor));
            _pointerTimer.Interval = TimeSpan.FromMilliseconds(16);
        }
        if (!_pointerHeld) return;
        if (!IsVisible || foreground != _pointerForeground) { CancelPointerGesture(); return; }
        var delta = PointFromScreen(cursor) - PointFromScreen(_pointerOrigin);
        var moved = Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (timestamp - _pointerPressedAt < 400)
        {
            // A quick swipe belongs to the underlying app; it must not turn into a late subtitle drag.
            if (moved) CancelPointerGesture();
            return;
        }
        if (!_pointerDragging && !moved) return;
        _pointerDragging = true;
        ApplyPointerPosition(cursor);
    }

    private void ApplyPointerPosition(Point cursor)
    {
        if (_pointerResizing)
        {
            Width = Math.Max(MinWidth, _pointerWidth + (PointFromScreen(cursor) - PointFromScreen(_pointerOrigin)).X);
            return;
        }
        // Keep native click-through enabled throughout; never capture, inject or replay an underlying click.
        var handle = new WindowInteropHelper(this).Handle;
        var left = (int)Math.Round(cursor.X - _pointerOffset.X);
        var top = (int)Math.Round(cursor.Y - _pointerOffset.Y);
        if (PointerBounds(handle, out var bounds) && bounds.Left == left && bounds.Top == top) return;
        PointerMove(handle, 0, left, top, 0, 0, 0x0015);
    }

    private void CancelPointerGesture()
    {
        _pointerHeld = false; _pointerDragging = false;
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(50);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PointerRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PointerCursor(out PointerPoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PointerBounds(nint handle, out PointerRectangle bounds);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PointerMove(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")] private static extern short PointerButtonState(int key);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint PointerForeground();
    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")] private static extern int PointerSystemMetric(int index);
}
