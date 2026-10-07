using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyImmersiveDragAsync(List<string> checks)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        void Drag(double x, double y) => _dragHandle.RaiseEvent(new DragDeltaEventArgs(x, y) { RoutedEvent = Thumb.DragDeltaEvent });
        void Release(bool canceled) => _dragHandle.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });
        var handle = new WindowInteropHelper(this).Handle;
        DragWindowRect Bounds()
        {
            if (!ReadDragWindowRect(handle, out var rect)) throw new Exception("Cannot read drag window bounds");
            return rect;
        }
        var source = HwndSource.FromHwnd(handle);
        var nativeMoveLoop = false;
        var recordMoves = false;
        var moves = new List<(int Left, int Top)>();
        nint Watch(nint window, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x0231 || message == 0x0112 && (wParam.ToInt64() & 0xfff0) == 0xf010) nativeMoveLoop = true;
            if (recordMoves && message == 0x0047)
            {
                var position = Marshal.PtrToStructure<DragWindowPosition>(lParam);
                if ((position.Flags & 0x0002) == 0) moves.Add((position.Left, position.Top));
            }
            return 0;
        }
        source.AddHook(Watch);
        var normal = new Rect(Left, Top, Width, Height);
        try
        {
            Drag(25, -15);
            Check(Left == normal.Left && Top == normal.Top, "immersive drag handle cannot move a normal window");
            ToggleImmersive(); await Task.Delay(200);
            Check(_dragHandle.Visibility != Visibility.Collapsed && _dragHandle.Template != null && _dragHandle.ActualWidth == 34
                && AutomationProperties.GetName(_dragHandle) == "拖动小窗", "immersive Thumb handle keeps its icon, tooltip and stable dimensions");
            var left = Left; var top = Top; var size = Bounds();
            Drag(25, -15); await Task.Delay(50);
            var dragDpi = VisualTreeHelper.GetDpi(this);
            Check(Math.Abs(Left - left - 25) * dragDpi.DpiScaleX <= 1 && Math.Abs(Top - top + 15) * dragDpi.DpiScaleY <= 1,
                $"immersive drag deltas move the WPF window within one physical pixel: ({Left - left}, {Top - top})");
            var moved = Bounds();
            Check(moved.Right - moved.Left == size.Right - size.Left && moved.Bottom - moved.Top == size.Bottom - size.Top,
                "direct drag preserves native window dimensions");
            var stressOrigin = Bounds();
            var nativeTopmost = GetWindowLongPtrW(handle, -20).ToInt64() & 8;
            const int burstInputs = 128;
            recordMoves = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < burstInputs; i++)
            {
                var direction = i % 2 == 0 ? 1 : -1;
                Left += 8 * direction; Top += 4 * direction;
            }
            var legacyDispatchMilliseconds = timer.Elapsed.TotalMilliseconds;
            var legacyMoves = moves.Count;
            recordMoves = false;
            MoveDragWindow(handle, 0, stressOrigin.Left, stressOrigin.Top, 0, 0, 0x0015);
            await Task.Delay(50);
            stressOrigin = Bounds();
            moves.Clear(); recordMoves = true;
            timer.Restart();
            for (var i = 1; i <= burstInputs; i++) Drag(i / 4d, -i / 8d);
            var coalescedDispatchMilliseconds = timer.Elapsed.TotalMilliseconds;
            Check(moves.Count == 0 && Bounds().Left == stressOrigin.Left && Bounds().Top == stressOrigin.Top && _dragMovePending,
                "high-frequency drag input queues a destination without synchronous native moves");
            await Task.Delay(100);
            var burstResult = Bounds();
            var coalescedMoves = moves.Count;
            recordMoves = false;
            Check(coalescedMoves == 1 && legacyMoves >= burstInputs && !_dragMovePending,
                $"{burstInputs} synthetic drag inputs coalesce to one native move instead of {legacyMoves} legacy axis moves");
            Check(Math.Abs(burstResult.Left - stressOrigin.Left - Math.Round(32 * dragDpi.DpiScaleX)) <= 1
                && Math.Abs(burstResult.Top - stressOrigin.Top - Math.Round(-16 * dragDpi.DpiScaleY)) <= 1,
                "coalesced drag uses the latest Thumb displacement without accumulating stale deltas");
            Check(moves[0] == (burstResult.Left, burstResult.Top)
                && burstResult.Right - burstResult.Left == stressOrigin.Right - stressOrigin.Left
                && burstResult.Bottom - burstResult.Top == stressOrigin.Bottom - stressOrigin.Top
                && (GetWindowLongPtrW(handle, -20).ToInt64() & 8) == nativeTopmost,
                "one native move updates both axes atomically while preserving size and topmost policy");
            await File.WriteAllTextAsync(Path.Combine(_dataPath, "drag-update-metrics.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                inputKind = "application-internal synthetic Thumb events, not physical mouse input",
                burstInputs, legacyMoves, coalescedMoves, legacyDispatchMilliseconds, coalescedDispatchMilliseconds
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            moves.Clear(); recordMoves = true;
            var releaseOrigin = Bounds();
            Drag(7, 9); Release(false);
            var released = Bounds();
            Check(!_dragMovePending && moves.Count == 1
                && Math.Abs(released.Left - releaseOrigin.Left - Math.Round(7 * dragDpi.DpiScaleX)) <= 1
                && Math.Abs(released.Top - releaseOrigin.Top - Math.Round(9 * dragDpi.DpiScaleY)) <= 1,
                "mouse release flushes the latest queued destination immediately");
            await Task.Delay(100);
            Check(moves.Count == 1, "released drag leaves no extra move queued for the next frame");
            recordMoves = false;
            var canceledOrigin = Bounds();
            Drag(100, 100); Release(true); await Task.Delay(100);
            Check(!_dragMovePending && Bounds().Left == canceledOrigin.Left && Bounds().Top == canceledOrigin.Top,
                "canceled drag discards its pending destination");
            for (var frame = 1; frame <= 3; frame++)
            {
                var before = Bounds();
                moves.Clear(); recordMoves = true;
                for (var input = 1; input <= 64; input++) Drag(input / 8d, -input / 16d);
                await Task.Delay(100);
                recordMoves = false;
                Check(moves.Count == 1 && Math.Abs(Bounds().Left - before.Left - Math.Round(8 * dragDpi.DpiScaleX)) <= 1
                    && Math.Abs(Bounds().Top - before.Top - Math.Round(-4 * dragDpi.DpiScaleY)) <= 1,
                    "continued drag frame applies one latest destination after earlier frames: " + frame);
            }
            canceledOrigin = Bounds();
            Drag(100, 100); WindowState = WindowState.Minimized;
            Check(!_dragMovePending, "minimization cancels pending immersive drag rendering");
            WindowState = WindowState.Normal; await Task.Delay(100);
            Check(Bounds().Left == canceledOrigin.Left && Bounds().Top == canceledOrigin.Top,
                "restoring a minimized window does not apply stale drag movement");
            Drag(100, 100); HideToTray();
            Check(!_dragMovePending, "hiding to tray cancels pending immersive drag rendering");
            RestoreMainWindow(); await Task.Delay(100);
            Check(Bounds().Left == canceledOrigin.Left && Bounds().Top == canceledOrigin.Top,
                "restoring from tray does not apply stale drag movement");
            var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            foreach (var edge in new[] { "left", "right", "top", "bottom" })
            {
                var before = Bounds(); var dpi = VisualTreeHelper.GetDpi(this);
                var x = edge == "left" ? screen.Left : edge == "right" ? screen.Right - (before.Right - before.Left) : before.Left;
                var y = edge == "top" ? screen.Top : edge == "bottom" ? screen.Bottom - (before.Bottom - before.Top) : before.Top;
                Drag((x - before.Left) / dpi.DpiScaleX, (y - before.Top) / dpi.DpiScaleY); await Task.Delay(100);
                var after = Bounds();
                Check(Math.Abs(after.Left - x) <= 1 && Math.Abs(after.Top - y) <= 1
                    && after.Right - after.Left == before.Right - before.Left && after.Bottom - after.Top == before.Bottom - before.Top
                    && WindowState == WindowState.Normal, "immersive drag reaches " + edge + " working-area edge without tiling or maximization");
            }
            Width = 500; Height = 281; await Task.Delay(100);
            Drag(-20, 10); await Task.Delay(50);
            Check(ResizeMode == ResizeMode.CanResizeWithGrip && Math.Abs(ActualWidth - 500) < 1 && Math.Abs(ActualHeight - 281) < 1,
                "immersive custom drag retains manual resizing and resized dimensions");
            Ui.PlaceVisible(this); UpdateImmersiveHover(new Point(20, 20)); UpdateLayout();
            var bitmap = new RenderTargetBitmap(34, 32, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(new VisualBrush(_dragHandle), null, new Rect(0, 0, 34, 32));
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(_dataPath, "immersive-drag-handle.png"))) encoder.Save(output);
            await using (var output = File.Create(Path.Combine(_dataPath, "immersive-drag-preview.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, output);
            Check(!nativeMoveLoop, "immersive drag never sends SC_MOVE or enters the native size/move loop");
            Drag(100, 100); ToggleImmersive(); await Task.Delay(150);
            Drag(10, 10);
            Check(_dragHandle.Visibility == Visibility.Collapsed && !_dragHandle.IsDragging && !_dragMovePending
                && Math.Abs(Left - normal.Left) < 0.1 && Math.Abs(Top - normal.Top) < 0.1,
                "leaving immersive cancels drag and restores normal bounds and hidden handle");
        }
        finally
        {
            recordMoves = false;
            CancelImmersiveDrag();
            source.RemoveHook(Watch);
            if (_immersive) ToggleImmersive();
            Left = normal.Left; Top = normal.Top; Width = normal.Width; Height = normal.Height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragWindowPosition
    {
        public nint Window, InsertAfter;
        public int Left, Top, Width, Height;
        public uint Flags;
    }
}
