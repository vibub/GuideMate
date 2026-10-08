using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private bool _dragMovePending;
    private nint _dragWindow;
    private int _dragLeft, _dragTop;
    // Input owns window movement; full-screen layered rendering must not gate its deadline.
    private readonly DispatcherTimer _dragMoveTimer = new(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(16) };

    private void QueueImmersiveDrag(DragDeltaEventArgs e)
    {
        if (!_immersive || !IsVisible || WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (!ReadDragWindowRect(handle, out var rect)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        // Thumb reports displacement from its grip point, not additive mouse steps.
        // Replace the pending destination so a burst cannot accumulate stale deltas.
        _dragWindow = handle;
        _dragLeft = rect.Left + (int)Math.Round(e.HorizontalChange * dpi.DpiScaleX);
        _dragTop = rect.Top + (int)Math.Round(e.VerticalChange * dpi.DpiScaleY);
        if (!_dragMovePending)
        {
            _dragMovePending = true;
            _dragMoveTimer.Tick += OnImmersiveDragTick;
            _dragMoveTimer.Start();
        }
        e.Handled = true;
    }

    private void OnImmersiveDragTick(object? sender, EventArgs e) => FinishImmersiveDrag(false);

    private void FinishImmersiveDrag(bool canceled)
    {
        if (!_dragMovePending) return;
        _dragMoveTimer.Stop();
        _dragMoveTimer.Tick -= OnImmersiveDragTick;
        _dragMovePending = false;
        if (canceled || _closing || !_immersive || !IsVisible || WindowState != WindowState.Normal) return;
        if (!ReadDragWindowRect(_dragWindow, out var rect) || rect.Left == _dragLeft && rect.Top == _dragTop) return;
        // One move updates both axes without resizing, activation, z-order changes or SC_MOVE.
        MoveDragWindow(_dragWindow, 0, _dragLeft, _dragTop, 0, 0, 0x0015);
    }

    private void CancelImmersiveDrag()
    {
        _dragHandle.CancelDrag();
        FinishImmersiveDrag(true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragWindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDragWindowRect(nint window, out DragWindowRect rect);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveDragWindow(nint window, nint insertAfter, int left, int top, int width, int height, uint flags);
}
