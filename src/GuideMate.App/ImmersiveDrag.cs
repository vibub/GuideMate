using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private ImmersiveDragPump? _dragMovePump;
    private nint _dragWindow;
    private int _dragOffsetX, _dragOffsetY, _dragButton;

    private void BeginImmersiveDrag(DragStartedEventArgs e)
    {
        FinishImmersiveDrag(true);
        if (!_immersive || !IsVisible || WindowState != WindowState.Normal) return;
        _dragWindow = new WindowInteropHelper(this).Handle;
        if (!ReadDragWindowRect(_dragWindow, out var rect)) return;
        // Keep the grabbed point in physical screen pixels across negative origins and DPI changes.
        var grip = _dragHandle.PointToScreen(new(e.HorizontalOffset, e.VerticalOffset));
        _dragOffsetX = (int)Math.Round(grip.X) - rect.Left;
        _dragOffsetY = (int)Math.Round(grip.Y) - rect.Top;
        _dragButton = ReadDragSystemMetric(23) == 0 ? 0x01 : 0x02; // SM_SWAPBUTTON
        _dragMovePump = new(Dispatcher, OnImmersiveDragTick);
        UpdateXRayPointer(null);
    }

    private void OnImmersiveDragTick()
    {
        if (_closing || !_immersive || !IsVisible || WindowState != WindowState.Normal || !_dragHandle.IsDragging)
        { FinishImmersiveDrag(true); return; }
        // A delayed WPF button-up must not keep moving the window after physical release.
        if (ReadDragButtonState(_dragButton) >= 0) { FinishImmersiveDrag(false); return; }
        ApplyImmersiveDragPosition();
    }

    private void ApplyImmersiveDragPosition()
    {
        if (!ReadXRayCursor(out var cursor) || !ReadDragWindowRect(_dragWindow, out var rect)) return;
        var left = cursor.X - _dragOffsetX;
        var top = cursor.Y - _dragOffsetY;
        if (rect.Left == left && rect.Top == top) return;
        // One move updates both axes without resizing, activation, z-order changes or SC_MOVE.
        MoveDragWindow(_dragWindow, 0, left, top, 0, 0, 0x0015);
    }

    private void FinishImmersiveDrag(bool canceled)
    {
        if (_dragMovePump is not { } pump) return;
        _dragMovePump = null;
        pump.Dispose();
        if (!canceled && !_closing && _immersive && IsVisible && WindowState == WindowState.Normal)
        {
            ApplyImmersiveDragPosition();
            SaveSettings();
        }
    }

    private void CancelImmersiveDrag()
    {
        FinishImmersiveDrag(true);
        _dragHandle.CancelDrag();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragWindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDragWindowRect(nint window, out DragWindowRect rect);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveDragWindow(nint window, nint insertAfter, int left, int top, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short ReadDragButtonState(int key);
    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static extern int ReadDragSystemMetric(int index);
}
