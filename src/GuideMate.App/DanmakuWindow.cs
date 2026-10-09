using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GuideMate.App;

// No WPF HwndTarget or full-screen CPU bitmap; only DirectComposition visuals belong to this HWND.
internal sealed class DanmakuWindow
{
    private nint _handle;
    private System.Drawing.Rectangle _bounds;
    private double _dpi = 1;
    private DanmakuComposition? _composition;
    private DanmakuSurface? _surface;
    public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;
    public bool IsVisible { get; private set; }
    public int CommentCount => _surface?.CommentCount ?? 0;
    public double MediaTime => _surface?.MediaTime ?? 0;

    public void Show()
    {
        if (_handle == 0)
        {
            _composition = new DanmakuComposition();
            _surface = new DanmakuSurface(_composition);
            _handle = CreateWindowEx(0x082800a8, WindowClass, "随引全屏弹幕",
                0x80000000, 0, 0, 1, 1, 0, 0, GetModuleHandle(null), 0);
            if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Uniform layered alpha enables WS_EX_TRANSPARENT hit testing across processes.
            // NOREDIRECTIONBITMAP keeps the content on GPU; never call UpdateLayeredWindow.
            if (!SetLayeredWindowAttributes(_handle, 0, 255, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _composition.Attach(_handle);
        }
        ShowWindow(_handle, 4); // SW_SHOWNOACTIVATE
        IsVisible = true;
    }

    public void Hide()
    {
        if (_handle != 0) ShowWindow(_handle, 0);
        IsVisible = false;
        Clear();
    }

    public void Close()
    {
        IsVisible = false;
        _surface?.Clear();
        _composition?.Dispose();
        _surface = null; _composition = null;
        if (_handle != 0) { DestroyWindow(_handle); _handle = 0; }
    }

    public bool FitToMonitor(System.Drawing.Rectangle screen)
    {
        if (_handle == 0) return false;
        var moved = _bounds != screen;
        if (moved && !SetWindowPos(_handle, -1, screen.Left, screen.Top, screen.Width, screen.Height, 0x0210))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var dpi = GetDpiForWindow(_handle) / 96d;
        if (!moved && _dpi == dpi) return false;
        _bounds = screen; _dpi = dpi;
        _surface!.Resize(screen.Width / dpi, screen.Height / dpi, dpi);
        return true;
    }

    public void Update(JsonElement items, double time, bool paused, double rate) => _surface?.Update(items, time, paused, rate);
    public void Configure(double area, double opacity, double fontScale, double speed) => _surface?.Configure(area, opacity, fontScale, speed);
    public void Clear() => _surface?.Clear();

    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);
    private static readonly WindowProcedure Procedure = HandleMessage;
    private static readonly string WindowClass = RegisterWindowClass();
    private static string RegisterWindowClass()
    {
        var name = "GuideMate.DanmakuComposition";
        var definition = new NativeWindowClass { Size = (uint)Marshal.SizeOf<NativeWindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = GetModuleHandle(null), ClassName = name };
        if (RegisterClassEx(ref definition) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return name;
    }
    private static nint HandleMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0084) return -1; // HTTRANSPARENT
        if (message == 0x0021) return 3; // MA_NOACTIVATE
        if (message == 0x0014) return 1; // WM_ERASEBKGND: no GDI background
        if (message == 0x000f) { ValidateRect(hwnd, 0); return 0; }
        if (message == 0x02e0) return 0; // Bounds and scale are set atomically by FitToMonitor.
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeWindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref NativeWindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ValidateRect(nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);

    private sealed class DanmakuSurface
    {
        private sealed record Comment(DanmakuComposition.Sprite Sprite, Rect InkBounds, double Width, double Start, double Duration, int Row, int Mode);
        private readonly List<Comment> _comments = [];
        private readonly HashSet<string> _seen = [];
        private readonly Stopwatch _clock = new();
        private readonly Pen _outline = new(Brushes.Black, 1.6);
        private readonly Typeface _typeface = new("Microsoft YaHei");
        private double _time, _rate = 1;
        private bool _paused = true;
        private readonly DanmakuComposition _composition;
        private double ActualWidth, ActualHeight, _dpi = 1;
        private sealed record Appearance(double Area, double Opacity, double FontScale, double Speed);
        private Appearance _appearance = new(1, 1, 1, 1);
        private double RowHeight => 42 * _appearance.FontScale;
        private double AreaHeight => ActualHeight * _appearance.Area;
        private double ScrollSpeed => ActualWidth / 8 * _appearance.Speed;
        public int CommentCount => _comments.Count;
        public double MediaTime => _time + (_paused ? 0 : _clock.Elapsed.TotalSeconds * _rate);

        public DanmakuSurface(DanmakuComposition composition)
        {
            _composition = composition;
            _outline.Freeze();
        }

        public void Configure(double area, double opacity, double fontScale, double speed)
        {
            var appearance = new Appearance(area, opacity, fontScale, speed);
            if (_appearance == appearance) return;
            _appearance = appearance;
            _composition.Configure(ActualWidth, AreaHeight, _dpi, opacity);
            Clear();
        }

        public void Dispose()
        {
            foreach (var comment in _comments) comment.Sprite.Dispose();
            _comments.Clear(); _seen.Clear();
        }
        public void Resize(double width, double height, double dpi)
        {
            Clear();
            ActualWidth = width; ActualHeight = height; _dpi = dpi;
            _composition.Configure(ActualWidth, AreaHeight, _dpi, _appearance.Opacity);
            _composition.Commit();
        }

        public void Clear()
        {
            foreach (var comment in _comments) _composition.Remove(comment.Sprite);
            _comments.Clear(); _seen.Clear();
            _composition.Commit();
        }

        private void RemoveExpired()
        {
            var time = MediaTime;
            for (var i = _comments.Count - 1; i >= 0; i--)
            {
                if (time - _comments[i].Start < _comments[i].Duration) continue;
                _composition.Remove(_comments[i].Sprite);
                _comments.RemoveAt(i);
            }
        }

        public void Update(JsonElement items, double time, bool paused, double rate)
        {
            if (Math.Abs(time - MediaTime) > 1.25) Clear();
            // Leave DWM animations running across ordinary snapshots; resync only on clock/state changes.
            var resync = Math.Abs(time - MediaTime) > 0.1 || paused != _paused || rate != _rate;
            if (resync) { _time = time; _paused = paused; _rate = rate; _clock.Restart(); }
            RemoveExpired();
            if (resync) foreach (var comment in _comments) Position(comment);
            var current = new HashSet<string>();
            var rows = Math.Max(1, (int)((AreaHeight - 24) / RowHeight));
            Span<bool> blockedRows = stackalloc bool[rows];
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
                    _typeface, size, brush, _dpi);
                var geometry = formatted.BuildGeometry(new Point(0, 0)); geometry.Freeze();
                if (geometry.Bounds.IsEmpty) continue;
                var width = Math.Max(1, formatted.WidthIncludingTrailingWhitespace);
                var start = time - Math.Clamp(Number(item, "offset", 0), 0, 4);
                var duration = mode is 4 or 5 ? 5 / _appearance.Speed : (ActualWidth + width) / ScrollSpeed;
                if (time - start >= duration) continue;
                // Classify occupied lanes once per incoming comment, rather than rescan every sprite for each row.
                blockedRows.Clear();
                foreach (var previous in _comments)
                {
                    if (!blockedRows[previous.Row] && (mode is 4 or 5 || previous.Mode is 4 or 5
                        || (mode == 6) != (previous.Mode == 6) || !HasRoom(previous, width, start, mode)))
                        blockedRows[previous.Row] = true;
                }
                for (var i = 0; i < rows; i++)
                {
                    // Reuse the first safe lane; a rotating cursor sends sparse comments down empty rows.
                    var row = mode == 4 ? rows - i - 1 : i;
                    if (blockedRows[row]) continue;
                    // Rasterize once, then upload only this glyph-sized sprite to DirectComposition.
                    var bounds = geometry.Bounds; bounds.Inflate(2, 2);
                    var dpi = new DpiScale(_dpi, _dpi);
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
                    var comment = new Comment(_composition.Add(image, _dpi), bounds, width, start, duration, row, mode);
                    _comments.Add(comment);
                    Position(comment);
                    break;
                }
            }
            _seen.IntersectWith(current);
            _composition.Commit();
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

        private void Position(Comment comment)
        {
            var velocity = comment.Mode is 4 or 5 ? 0 : (comment.Mode == 6 ? 1 : -1) * ScrollSpeed * _rate;
            var remaining = (comment.Duration - (MediaTime - comment.Start)) / Math.Max(_rate, 0.01);
            _composition.Position(comment.Sprite, X(comment) + comment.InkBounds.X,
                12 + comment.Row * RowHeight + comment.InkBounds.Y, velocity, Math.Max(0.001, remaining), _paused);
        }
    }
}
