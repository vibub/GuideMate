using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyTopmostAsync(List<string> checks)
    {
        void AssertTopmost(bool expected, string description)
        {
            var native = (GetWindowLongPtrW(new WindowInteropHelper(this).Handle, -20).ToInt64() & 8) != 0;
            if (Topmost != expected || native != expected) throw new Exception(description);
            checks.Add(description);
        }
        void CaptureSelector(string name)
        {
            _topmostMode.BringIntoView(); UpdateLayout();
            if (_topmostMode.ActualWidth < 150 || _topmostMode.ActualHeight < 28)
                throw new Exception("Topmost selector was clipped");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_sidebar.ActualWidth), (int)Math.Ceiling(_sidebar.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(_sidebar), null, new Rect(0, 0, _sidebar.ActualWidth, _sidebar.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(_dataPath, name)); encoder.Save(output);
        }
        var original = _settings.GetTopmostMode();
        var bounds = new Size(Width, Height);
        try
        {
            if (!_topmostMode.Items.Cast<string>().SequenceEqual(new[] { "不置顶", "沉浸置顶", "始终置顶" }))
                throw new Exception("Topmost selector options do not match the requested order");
            checks.Add("topmost selector presents never, immersive-only and always in order");
            AssertTopmost(_settings.ShouldBeTopmost(false), "initial normal window honors its saved topmost policy");
            foreach (var mode in Enum.GetValues<WindowTopmostMode>())
            {
                _topmostMode.SelectedIndex = (int)mode;
                AssertTopmost(mode == WindowTopmostMode.Always, "normal window native topmost matches " + mode);
                WindowState = WindowState.Maximized; await Task.Delay(100);
                AssertTopmost(mode == WindowTopmostMode.Always, "maximized non-immersive window native topmost matches " + mode);
                WindowState = WindowState.Normal; await Task.Delay(100);
                if (_store.Load().GetTopmostMode() != mode) throw new Exception("Topmost selection did not persist");
                checks.Add("topmost selection immediately persists: " + mode);
                ToggleImmersive(); await Task.Delay(150);
                AssertTopmost(mode != WindowTopmostMode.Never, "immersive window native topmost matches " + mode);
                if (mode == WindowTopmostMode.Immersive) EmergencyRestore(); else ToggleImmersive();
                await Task.Delay(150);
                AssertTopmost(mode == WindowTopmostMode.Always, "leaving immersive restores policy without changing selection: " + mode);
                if (_topmostMode.SelectedIndex != (int)mode) throw new Exception("Leaving immersive changed topmost selection");
            }
            _topmostMode.SelectedIndex = (int)WindowTopmostMode.Immersive;
            ToggleImmersive(); await Task.Delay(150);
            _topmostMode.SelectedIndex = (int)WindowTopmostMode.Never;
            AssertTopmost(false, "changing policy to never while immersive removes native topmost");
            _topmostMode.SelectedIndex = (int)WindowTopmostMode.Always;
            AssertTopmost(true, "changing policy to always while immersive enables native topmost");
            _topmostMode.SelectedIndex = (int)WindowTopmostMode.Immersive;
            EmergencyRestore(); await Task.Delay(150);
            AssertTopmost(false, "emergency restore leaves immersive-only window non-topmost");
            CaptureSelector("topmost-selector-normal.png");
            Width = 860; Height = 500; await Task.Delay(150);
            CaptureSelector("topmost-selector-minimum.png");
            checks.Add("topmost selector fits normal and minimum sidebar layouts");
        }
        finally
        {
            if (_immersive) ToggleImmersive();
            Width = bounds.Width; Height = bounds.Height;
            _topmostMode.SelectedIndex = (int)original;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);
}
