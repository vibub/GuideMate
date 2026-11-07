using System.Windows.Interop;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private Window? _controlLayer;
    private readonly Grid _controlSurface = new();

    private void UpdateControlLayer()
    {
        // A layered HWND ignores all mouse input with WS_EX_TRANSPARENT. Keep the video
        // in that HWND, but place its actual controls in an owned, per-pixel input window.
        // Zero-alpha pixels pass straight to other applications without input replay/polling.
        if (_through && _controlLayer == null)
        {
            _controlLayer = new Window
            {
                Title = "随引小窗控件", Owner = this, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                Content = _controlSurface, Left = Left, Top = Top, Width = ActualWidth, Height = ActualHeight
            };
            _controlLayer.SourceInitialized += (_, _) =>
            {
                NativeHotkeys.HideFromWindowSwitcher(_controlLayer, true);
                HwndSource.FromHwnd(new WindowInteropHelper(_controlLayer).Handle).AddHook(ControlLayerMessage);
            };
            MoveControl(_dragHandle, _controlSurface);
            MoveControl(_edgeProgress, _controlSurface);
            foreach (var grip in _resizeGrips) MoveControl(grip, _controlSurface);
            _controlLayer.MouseEnter += (_, _) => SampleImmersiveCursor();
            _controlLayer.MouseLeave += (_, _) => SampleImmersiveCursor();
        }
        else if (!_through && _controlLayer != null)
        {
            MoveControl(_dragHandle, _workspace);
            MoveControl(_edgeProgress, _workspace);
            foreach (var grip in _resizeGrips) MoveControl(grip, _root);
            _controlLayer.Close(); _controlLayer = null;
        }
        SyncControlLayer();
    }

    private static void MoveControl(FrameworkElement control, Panel target)
    {
        ((Panel)control.Parent).Children.Remove(control);
        target.Children.Add(control);
    }

    private static nint ControlLayerMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Controls operate without taking focus from the application behind the video.
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }

    private void SyncControlLayer()
    {
        if (_controlLayer == null) return;
        if (_closing || !IsVisible || WindowState == WindowState.Minimized) { _controlLayer.Hide(); return; }
        _controlLayer.Topmost = Topmost;
        _controlLayer.Opacity = Opacity;
        if (!_controlLayer.IsVisible) _controlLayer.Show();
        if (ReadDragWindowRect(new WindowInteropHelper(this).Handle, out var bounds))
            MoveDragWindow(new WindowInteropHelper(_controlLayer).Handle, 0, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0014);
    }
}
