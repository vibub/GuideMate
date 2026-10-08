using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GuideMate.App;

// A separate layered window keeps the video, X-ray mask and desktop input independent.
internal sealed class DanmakuWindow : Window
{
    private readonly DanmakuSurface _surface = new();
    public int CommentCount => _surface.CommentCount;
    public double MediaTime => _surface.MediaTime;

    public DanmakuWindow()
    {
        Title = "随引全屏弹幕";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; Topmost = true;
        Content = _surface;
        SourceInitialized += (_, _) =>
        {
            NativeHotkeys.ClickThrough(this, true);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x0084) { handled = true; return -1; } // HTTRANSPARENT
                if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
                return 0;
            });
        };
        IsVisibleChanged += (_, _) => _surface.SetVisible(IsVisible);
        Closed += (_, _) => _surface.SetVisible(false);
    }

    public void FitToMonitor(System.Drawing.Rectangle screen)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        if (GetWindowRect(handle, out var rect) && rect.Left == screen.Left && rect.Top == screen.Top
            && rect.Right == screen.Right && rect.Bottom == screen.Bottom) return;
        // Native pixel coordinates are essential for monitors with different DPI and negative origins.
        SetWindowPos(handle, -1, screen.Left, screen.Top, screen.Width, screen.Height, 0x0210);
        UpdateLayout();
        _surface.Clear();
    }

    public void Update(JsonElement items, double time, bool paused, double rate) => _surface.Update(items, time, paused, rate);
    public void Configure(double area, double opacity, double fontScale, double speed) => _surface.Configure(area, opacity, fontScale, speed);
    public void Clear() => _surface.Clear();
    internal FrameworkElement Surface => _surface;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);

    private sealed class DanmakuSurface : FrameworkElement
    {
        private sealed record Comment(BitmapSource Image, Rect InkBounds, double Width, double Start, double Duration, int Row, int Mode);
        private readonly List<Comment> _comments = [];
        private readonly HashSet<string> _seen = [];
        private readonly Stopwatch _clock = new();
        private readonly Pen _outline = new(Brushes.Black, 1.6);
        private readonly Typeface _typeface = new("Microsoft YaHei");
        private double _time, _rate = 1;
        private bool _paused = true, _rendering;
        private int _nextRow;
        private sealed record Appearance(double Area, double Opacity, double FontScale, double Speed);
        private Appearance _appearance = new(1, 1, 1, 1);
        private double RowHeight => 42 * _appearance.FontScale;
        private double AreaHeight => ActualHeight * _appearance.Area;
        private double ScrollSpeed => ActualWidth / 8 * _appearance.Speed;
        public int CommentCount => _comments.Count;
        public double MediaTime => _time + (_paused ? 0 : _clock.Elapsed.TotalSeconds * _rate);

        public DanmakuSurface() { IsHitTestVisible = false; ClipToBounds = true; _outline.Freeze(); }

        public void Configure(double area, double opacity, double fontScale, double speed)
        {
            var appearance = new Appearance(area, opacity, fontScale, speed);
            if (_appearance == appearance) return;
            _appearance = appearance;
            Opacity = opacity;
            // Rebuild active geometry and lane assignments from the next source snapshot.
            Clear();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Clear();
        }

        public void SetVisible(bool visible)
        {
            if (_rendering == visible) return;
            _rendering = visible;
            if (visible) CompositionTarget.Rendering += RenderFrame;
            else { CompositionTarget.Rendering -= RenderFrame; Clear(); }
        }

        private void RenderFrame(object? sender, EventArgs e)
        {
            if (_paused || _comments.Count == 0 || _appearance.Opacity == 0) return;
            RemoveExpired();
            InvalidateVisual();
        }

        public void Clear()
        {
            _comments.Clear(); _seen.Clear(); _nextRow = 0;
            InvalidateVisual();
        }

        private void RemoveExpired()
        {
            var time = MediaTime;
            _comments.RemoveAll(comment => time - comment.Start >= comment.Duration);
        }

        public void Update(JsonElement items, double time, bool paused, double rate)
        {
            if (Math.Abs(time - MediaTime) > 1.25) Clear();
            _time = time; _paused = paused; _rate = rate; _clock.Restart();
            RemoveExpired();
            var current = new HashSet<string>();
            foreach (var item in items.EnumerateArray().Take(240))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String) continue;
                var id = idValue.GetString() ?? "";
                var text = textValue.GetString() ?? "";
                if (id.Length == 0 || text.Length == 0 || text.Length > 200) continue;
                current.Add(id);
                if (!_seen.Add(id) || _comments.Count >= 120 || ActualWidth < 1 || AreaHeight < 24) continue;
                var mode = item.TryGetProperty("mode", out var modeValue) && modeValue.ValueKind == JsonValueKind.Number && modeValue.TryGetInt32(out var value) ? value : 1;
                if (mode is < 1 or > 6) continue;
                var size = Math.Clamp(Number(item, "size", 25), 18, 36) * _appearance.FontScale;
                var color = (int)Math.Clamp(Number(item, "color", 0xffffff), 0, 0xffffff);
                var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
                brush.Freeze();
                var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    _typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var geometry = formatted.BuildGeometry(new Point(0, 0)); geometry.Freeze();
                if (geometry.Bounds.IsEmpty) continue;
                var width = Math.Max(1, formatted.WidthIncludingTrailingWhitespace);
                var start = time - Math.Clamp(Number(item, "offset", 0), 0, 4);
                var duration = mode is 4 or 5 ? 5 / _appearance.Speed : (ActualWidth + width) / ScrollSpeed;
                if (time - start >= duration) continue;
                var rows = Math.Max(1, (int)((AreaHeight - 24) / RowHeight));
                for (var i = 0; i < rows; i++)
                {
                    var row = mode == 5 ? i : mode == 4 ? rows - i - 1 : (_nextRow + i) % rows;
                    if (_comments.Any(comment => comment.Row == row && (mode is 4 or 5 || comment.Mode is 4 or 5
                        || (mode == 6) != (comment.Mode == 6) || !HasRoom(comment, width, start, mode)))) continue;
                    // Rasterize the outline once per accepted comment; each frame only blits its sprite.
                    var bounds = geometry.Bounds; bounds.Inflate(2, 2);
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        drawing.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                        drawing.DrawGeometry(null, _outline, geometry);
                        drawing.DrawGeometry(brush, null, geometry);
                    }
                    var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX)),
                        Math.Max(1, (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY)),
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    image.Render(visual); image.Freeze();
                    _comments.Add(new(image, bounds, width, start, duration, row, mode));
                    _nextRow = (row + 1) % rows;
                    break;
                }
            }
            _seen.IntersectWith(current);
            InvalidateVisual();
        }

        private static double Number(JsonElement item, string key, double fallback) =>
            item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : fallback;

        private double X(Comment comment) => comment.Mode is 4 or 5 ? (ActualWidth - comment.Width) / 2
            : comment.Mode == 6 ? -comment.Width + (MediaTime - comment.Start) * ScrollSpeed
            : ActualWidth - (MediaTime - comment.Start) * ScrollSpeed;

        private bool HasRoom(Comment previous, double width, double start, int mode) =>
            mode == 6 ? X(previous) >= -width + (MediaTime - start) * ScrollSpeed + width + 28
                : X(previous) + previous.Width + 28 <= ActualWidth - (MediaTime - start) * ScrollSpeed;

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, AreaHeight)));
            foreach (var comment in _comments)
            {
                drawing.DrawImage(comment.Image, new Rect(X(comment) + comment.InkBounds.X,
                    12 + comment.Row * RowHeight + comment.InkBounds.Y, comment.InkBounds.Width, comment.InkBounds.Height));
            }
            drawing.Pop();
        }
    }
}
