using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GuideMate.App;

internal sealed class NativeHotkeys : IDisposable
{
    private const int EmergencyId = 99;
    private sealed record Binding((uint Modifiers, uint Key) Input, string? TapAction, bool Hold);
    private readonly nint _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Binding> _actions = [];
    private readonly Dictionary<(uint Modifiers, uint Key), Binding> _mouseActions = [];
    private readonly HashSet<uint> _consumedMouseButtons = [];
    private readonly MouseHookProc _mouseCallback;
    private nint _mouseHook;
    private int _generation;
    private bool _disposed;
    private Dictionary<string, string> _active = [];
    private IReadOnlyList<Binding> _activeBindings = [];
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private (uint Modifiers, uint Key)? _held;
    private HoldGesture? _gesture;
    private string? _tapAction;
    private nint _holdForeground;
    public int HoldMilliseconds { get; private set; } = 400;
    public bool OrdinaryBindingsSuspended { get; private set; }
    public bool EmergencyAvailable { get; }
    public string EmergencyBinding { get; } = "Ctrl+Alt+F10";
    public int EmergencyError { get; }
    public event Action<string>? Pressed;
    public event Action? TemporaryRateReleased;
    internal bool MouseHookAvailable => _mouseHook != 0;
    internal static nint CurrentForegroundWindow => GetForegroundWindow();

    public NativeHotkeys(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _mouseCallback = OnMouseMessage;
        _source.AddHook(OnMessage);
        _holdTimer.Tick += (_, _) =>
        {
            if (_held is not { } key) return;
            var buttonDown = IsMouseKey(key.Key) ? _consumedMouseButtons.Contains(key.Key) : GetAsyncKeyState((int)key.Key) < 0;
            var down = buttonDown
                && ((key.Modifiers & 2) == 0 || GetAsyncKeyState(0x11) < 0)
                && ((key.Modifiers & 1) == 0 || GetAsyncKeyState(0x12) < 0)
                && ((key.Modifiers & 4) == 0 || GetAsyncKeyState(0x10) < 0);
            PollHeldBinding(Environment.TickCount64, down, GetForegroundWindow());
        };
        EmergencyAvailable = RegisterHotKey(_handle, EmergencyId, 0x4003, (uint)KeyInterop.VirtualKeyFromKey(Key.F10));
        if (!EmergencyAvailable)
        {
            EmergencyBinding = "Ctrl+Alt+Shift+F10";
            EmergencyAvailable = RegisterHotKey(_handle, EmergencyId, 0x4007, (uint)KeyInterop.VirtualKeyFromKey(Key.F10));
            if (!EmergencyAvailable) EmergencyError = Marshal.GetLastWin32Error();
        }
    }

    public static (uint Modifiers, uint Key) Parse(string value)
    {
        uint modifiers = 0;
        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("请选择键盘组合或鼠标侧键。");
        foreach (var token in tokens[..^1])
            modifiers |= token.ToLowerInvariant() switch { "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, _ => throw new FormatException("未知修饰键：" + token) };
        var mouseKey = tokens[^1].ToLowerInvariant() switch { "mouse4" or "xbutton1" => 5u, "mouse5" or "xbutton2" => 6u, _ => 0u };
        if (mouseKey != 0) return (modifiers, mouseKey);
        if (modifiers == 0) throw new FormatException("键盘热键至少包含 Ctrl、Alt 或 Shift 中的一项修饰键；鼠标侧键可单独使用。");
        var keyName = tokens[^1].Length == 1 && tokens[^1][0] is >= '0' and <= '9' ? "D" + tokens[^1] : tokens[^1];
        if (!Enum.TryParse<Key>(keyName, true, out var key) || key is Key.None or Key.F12 || KeyInterop.VirtualKeyFromKey(key) == 0)
            throw new FormatException("无效按键：" + tokens[^1]);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (modifiers is 3 or 7 && key == Key.F10) throw new FormatException("Ctrl+Alt+F10 和 Ctrl+Alt+Shift+F10 已保留用于紧急恢复。");
        return (modifiers, vk);
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        return Format(key switch
        {
            Key.Return => "Enter",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            _ => key.ToString()
        }, modifiers);
    }

    public static string Format(MouseButton button, ModifierKeys modifiers) => Format(button switch
    {
        MouseButton.XButton1 => "Mouse4",
        MouseButton.XButton2 => "Mouse5",
        _ => throw new FormatException("仅支持鼠标侧键 Mouse4 和 Mouse5。")
    }, modifiers);

    private static string Format(string key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Windows) != 0) throw new FormatException("不支持 Windows 键组合。");
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(key);
        var binding = string.Join("+", parts);
        Parse(binding);
        return binding;
    }

    public void SuspendOrdinaryBindings()
    {
        OrdinaryBindingsSuspended = true;
        Clear();
    }

    public string? ResumeOrdinaryBindings()
    {
        if (!OrdinaryBindingsSuspended) return null;
        OrdinaryBindingsSuspended = false;
        return Apply(new(_active), HoldMilliseconds);
    }

    public string? Apply(Dictionary<string, string> bindings, int holdMilliseconds = 400)
    {
        if (holdMilliseconds is < 100 or > 2000) return "长按触发时间应为 100 至 2000 毫秒。";
        var parsed = new List<Binding>();
        try
        {
            foreach (var group in bindings.GroupBy(pair => Parse(pair.Value)))
            {
                var taps = group.Where(pair => pair.Key != "TemporaryRate").ToArray();
                if (taps.Length > 1) return "两个普通操作不能使用相同热键；长按临时倍速可以与一个普通操作共用。";
                parsed.Add(new(group.Key, taps.FirstOrDefault().Key, group.Any(pair => pair.Key == "TemporaryRate")));
            }
        }
        catch (FormatException ex) { return ex.Message; }
        var previous = new Dictionary<string, string>(_active);
        var previousBindings = _activeBindings;
        Clear();
        var error = RegisterBindings(parsed);
        if (error == null) { _active = new(bindings); _activeBindings = parsed; HoldMilliseconds = holdMilliseconds; return null; }
        Clear();
        var restoreError = RegisterBindings(previousBindings);
        _active = previous;
        return error + (restoreError == null ? " 已恢复原热键。" : " 原热键恢复失败：" + restoreError);
    }

    private string? RegisterBindings(IEnumerable<Binding> bindings)
    {
        var id = 100;
        foreach (var binding in bindings)
        {
            var (modifiers, key) = binding.Input;
            if (IsMouseKey(key))
            {
                if (_mouseHook == 0)
                {
                    _mouseHook = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
                    if (_mouseHook == 0) return $"鼠标侧键注册失败（错误 {Marshal.GetLastWin32Error()}）。";
                }
                _mouseActions[(modifiers, key)] = binding;
                continue;
            }
            var repeat = !binding.Hold && binding.TapAction is ("SeekBack" or "SeekForward") ? 0u : 0x4000u;
            if (!RegisterHotKey(_handle, id, modifiers | repeat, key))
            {
                var action = binding.TapAction ?? "TemporaryRate";
                return "热键冲突或注册失败：" + action + $"（错误 {Marshal.GetLastWin32Error()}）";
            }
            _actions[id++] = binding;
        }
        return null;
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312) return 0;
        var id = (int)wParam;
        if (id == EmergencyId) { handled = true; Pressed?.Invoke("Emergency"); }
        else if (_actions.TryGetValue(id, out var binding))
        {
            handled = true;
            ProcessKeyboardPress(binding.Input, GetForegroundWindow(), Environment.TickCount64);
        }
        return 0;
    }

    internal void ProcessKeyboardPress((uint Modifiers, uint Key) input, nint foreground, long timestamp)
    {
        var binding = _actions.Values.FirstOrDefault(value => value.Input == input);
        if (binding != null) Dispatch(binding, foreground, timestamp);
    }

    private void Dispatch(Binding binding, nint foreground, long timestamp)
    {
        if (!binding.Hold) { Pressed?.Invoke(binding.TapAction!); return; }
        if (_held != null) return;
        _held = binding.Input; _holdForeground = foreground; _tapAction = binding.TapAction;
        _gesture = new(timestamp, HoldMilliseconds, binding.TapAction != null);
        _holdTimer.Start();
    }

    internal void PollHeldBinding(long timestamp, bool down, nint foreground)
    {
        if (_gesture == null) return;
        if (foreground != _holdForeground) { CancelTemporaryRate(); return; }
        if (!down) { FinishGesture(timestamp, false); return; }
        if (_gesture.Advance(timestamp) == HoldOutcome.Start) Pressed?.Invoke("TemporaryRate");
    }

    private void FinishGesture(long timestamp, bool cancel)
    {
        _holdTimer.Stop();
        if (_gesture == null) return;
        var outcome = cancel ? _gesture.Cancel() : _gesture.Release(timestamp);
        var tap = _tapAction;
        _gesture = null; _held = null; _tapAction = null;
        if (outcome == HoldOutcome.Stop) TemporaryRateReleased?.Invoke();
        else if (outcome == HoldOutcome.Tap) Pressed?.Invoke(tap!);
    }

    private static bool IsMouseKey(uint key) => key is 5 or 6;

    private nint OnMouseMessage(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !_disposed && wParam.ToInt64() is 0x020B or 0x020C)
        {
            var data = Marshal.PtrToStructure<MouseHookData>(lParam);
            var key = (data.MouseData >> 16) switch { 1 => 5u, 2 => 6u, _ => 0u };
            // Ignore injected input; inspect only side-button messages, never mouse motion.
            if (key != 0 && (data.Flags & 1) == 0)
            {
                var modifiers = (GetAsyncKeyState(0x11) < 0 ? 2u : 0u) | (GetAsyncKeyState(0x12) < 0 ? 1u : 0u)
                    | (GetAsyncKeyState(0x10) < 0 ? 4u : 0u);
                if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) modifiers |= 8;
                if (ProcessMouseButton(key, wParam.ToInt64() == 0x020B, modifiers, GetForegroundWindow())) return 1;
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    internal bool ProcessMouseButton(uint key, bool down, uint modifiers, nint foreground, long? timestamp = null)
    {
        if (_disposed || !IsMouseKey(key)) return false;
        var generation = _generation;
        var time = timestamp ?? Environment.TickCount64;
        if (!down)
        {
            if (!_consumedMouseButtons.Remove(key)) return false;
            _source.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && generation == _generation && _held?.Key == key) PollHeldBinding(time, false, foreground);
            });
            return true;
        }
        if (_consumedMouseButtons.Contains(key)) return true;
        if (OrdinaryBindingsSuspended || !_mouseActions.TryGetValue((modifiers, key), out var binding)) return false;
        _consumedMouseButtons.Add(key);
        // Keep the native hook short; execute application actions on the next dispatcher turn.
        _source.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && generation == _generation) Dispatch(binding, foreground, time);
        });
        return true;
    }
    public void CancelTemporaryRate()
    {
        FinishGesture(Environment.TickCount64, true);
    }
    private void Clear()
    {
        _generation++;
        CancelTemporaryRate();
        foreach (var id in _actions.Keys) UnregisterHotKey(_handle, id);
        _actions.Clear();
        _mouseActions.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        if (_mouseHook != 0) { UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
        _consumedMouseButtons.Clear();
        UnregisterHotKey(_handle, EmergencyId);
        _source.RemoveHook(OnMessage);
    }

    public static void ClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        const long flags = 0x20 | 0x08000000;
        SetWindowLongPtr(hwnd, -20, (nint)(enabled ? style | flags : style & ~flags));
    }
    public static bool IsClickThrough(Window window) => (GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & 0x20) != 0;
    private delegate nint MouseHookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int hook, MouseHookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
