using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using GuideMate.App;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static void Main()
    {
        var window = new Window();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var keys = new NativeHotkeys(window);
        var actions = new List<string>();
        var releases = 0;
        keys.Pressed += actions.Add;
        keys.TemporaryRateReleased += () => releases++;
        try
        {
            Check(Marshal.SizeOf<NativeHotkeys.RawInputHeader>() == 24
                && Marshal.SizeOf<NativeHotkeys.RawInput>() == 48
                && Marshal.OffsetOf<NativeHotkeys.RawInput>("Data").ToInt32() == 24,
                "x64 raw input header and union match Win32 ABI");
            Check(Marshal.SizeOf<NativeHotkeys.RawKeyboard>() == 16
                && Marshal.OffsetOf<NativeHotkeys.RawMouse>("ButtonFlags").ToInt32() == 4,
                "keyboard size and aligned mouse button offset match Win32 ABI");
            Apply(new() { ["PlayPause"] = "Mouse4", ["SeekForward"] = "Mouse5" });
            Check(keys.RawInputAvailable && keys.MouseHookAvailable, "native raw registration and legacy suppression hook available");
            var devices = RegisteredDevices();
            Check(devices.Length == 2 && devices.All(d => d.Page == 1 && d.Flags == 0x100 && d.Target == hwnd)
                && devices.Select(d => d.Usage).Order().SequenceEqual(new ushort[] { 2, 6 }),
                "mouse and modifier keyboard use INPUTSINK without NOLEGACY or EXINPUTSINK");

            // No hook callback: model a game or a later hook preventing our legacy callback.
            Mouse(0x40, 1000); Mouse(0x80, 1100); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "raw-only Mouse4 works when the legacy hook sees no events");
            actions.Clear();
            Mouse(0x100, 1200); Mouse(0x200, 1300); Drain();
            Check(actions.SequenceEqual(new[] { "SeekForward" }), "raw-only Mouse5 works in background");
            actions.Clear();
            Check(keys.ConsumeMouseButton(5, true, 0), "bound legacy down is suppressed");
            Mouse(0x40, 1400); Mouse(0x40, 1401); Drain();
            Check(keys.ConsumeMouseButton(5, false, 7), "paired legacy up stays suppressed after modifiers change");
            Mouse(0x80, 1500); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "hook plus raw and duplicate down dispatch exactly once");
            actions.Clear();
            Check(keys.ConsumeMouseButton(5, true, 0) && keys.ConsumeMouseButton(5, false, 0), "legacy-only pair remains suppressible");
            Drain();
            Check(actions.Count == 0, "legacy hook never duplicates or initiates an action");
            Check(!keys.ConsumeMouseButton(5, true, 2) && !keys.ConsumeMouseButton(5, false, 2)
                && !keys.ConsumeMouseButton(1, true, 0), "unbound modifiers and ordinary buttons pass through");
            Mouse(0xF, 1600); Drain();
            Check(actions.Count == 0, "left/right clicks and movement do not trigger hotkeys");

            Apply(new() { ["PlayPause"] = "Ctrl+Alt+Shift+Mouse4" });
            Key(0x11, false, 1700); Key(0x12, false, 1701); Key(0x10, false, 1702);
            Mouse(0x40, 1710); Mouse(0x80, 1720); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "raw modifiers match without querying the elevated foreground thread");
            ResetModifiers(); actions.Clear();
            Key(0x11, false, 1800); Key(0x12, false, 1801); Key(0x10, false, 1802); Key(0x5B, false, 1803);
            Mouse(0x40, 1810); Mouse(0x80, 1820); Drain();
            Check(actions.Count == 0, "Windows key prevents accidental match of an ordinary combination");
            ResetModifiers();
            Apply(new() { ["PlayPause"] = "Ctrl+Mouse4" });
            Key(0x11, false, 1900); Key(0x11, false, 1901, extended: true); Key(0x11, true, 1902);
            Mouse(0x40, 1910); Mouse(0x80, 1920); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "releasing left Ctrl preserves held right Ctrl");
            ResetModifiers();
            Apply(new() { ["PlayPause"] = "Shift+Mouse4" });
            Key(0x10, false, 2000, scan: 0x36); Key(0x10, true, 2001, scan: 0x2A);
            Mouse(0x40, 2010); Mouse(0x80, 2020); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "right Shift is distinguished by scan code");
            ResetModifiers();
            Apply(new() { ["PlayPause"] = "Alt+Mouse5" });
            Key(0x12, false, 2100, extended: true);
            Mouse(0x100, 2110); Mouse(0x200, 2120); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "right Alt and Mouse5 are recognized");
            ResetModifiers();

            Apply(new() { ["SeekForward"] = "Mouse5", ["TemporaryRate"] = "Mouse5" });
            Mouse(0x100, 3000); Mouse(0x200, 3399); Drain();
            Check(actions.SequenceEqual(new[] { "SeekForward" }) && releases == 0, "queued fast down/up uses original timestamps and emits a single tap");
            actions.Clear();
            Mouse(0x100, 4000); Drain(); keys.PollHeldBinding(4400, true, (nint)123);
            Mouse(0x200, 4500); Drain();
            Check(actions.SequenceEqual(new[] { "TemporaryRate" }) && releases == 1, "raw long press starts and restores once without a short action");
            actions.Clear();
            Mouse(0x100, 5000); Drain(); keys.PollHeldBinding(5100, true, (nint)456);
            Mouse(0x200, 5200); Drain();
            Check(actions.Count == 0 && releases == 1, "foreground change cancels pending tap");
            Mouse(0x100, 6000); Drain(); keys.PollHeldBinding(6400, true, (nint)123);
            keys.PollHeldBinding(6450, true, (nint)456); Mouse(0x200, 6500); Drain();
            Check(actions.SequenceEqual(new[] { "TemporaryRate" }) && releases == 2, "foreground change restores active hold without replaying tap");

            Apply(new() { ["TemporaryRate"] = "Mouse4" });
            Mouse(0x40, 7000); Mouse(0x80, 7100); Drain();
            Check(actions.Count == 0, "hold-only raw short press does nothing");
            Mouse(0x40, 7200); Drain(); keys.PollHeldBinding(7600, true, (nint)123);
            keys.CancelTemporaryRate(); Mouse(0x80, 7700); Drain();
            Check(actions.SequenceEqual(new[] { "TemporaryRate" }) && releases == 3, "navigation/emergency cancellation restores active raw hold once");
            Apply(new() { ["TemporaryRate"] = "Ctrl+Mouse4", ["PlayPause"] = "Ctrl+Mouse4" });
            Key(0x11, false, 8000); Mouse(0x40, 8010); Drain(); keys.PollHeldBinding(8410, true, (nint)123);
            Key(0x11, true, 8420); Mouse(0x80, 8430); Drain();
            Check(actions.SequenceEqual(new[] { "TemporaryRate" }) && releases == 4, "raw modifier release restores hold even if async key state is inaccessible");
            ResetModifiers();

            Apply(new() { ["PlayPause"] = "Mouse4" });
            Mouse(0x40, 9000); keys.SuspendOrdinaryBindings(); Drain();
            Mouse(0x80, 9100); Mouse(0x40, 9200); Mouse(0x80, 9300); Drain();
            Check(actions.Count == 0 && !keys.ConsumeMouseButton(5, true, 0), "recording cancels queued action and suspends both channels");
            Check(keys.ResumeOrdinaryBindings() == null, "recording exit restores original map");
            Mouse(0x40, 9400); Mouse(0x80, 9500); Drain();
            Check(actions.SequenceEqual(new[] { "PlayPause" }), "resumed raw binding fires once");
            actions.Clear();
            Mouse(0x40, 10000);
            Check(keys.Apply(new() { ["SeekBack"] = "Mouse4" }) == null, "binding update succeeds");
            Drain(); Mouse(0x80, 10100); Drain();
            Check(actions.Count == 0, "configuration change drops already queued old actions");
            Mouse(0x40, 10200); Mouse(0x80, 10300); Drain();
            Check(actions.SequenceEqual(new[] { "SeekBack" }), "new binding works after original button release");
            Check(keys.Apply(new() { ["SeekBack"] = "Mouse4", ["PlayPause"] = "XButton1" }) != null,
                "ordinary conflicts remain rejected after alias normalization");
            actions.Clear(); Mouse(0x40, 10400); Mouse(0x80, 10500); Drain();
            Check(actions.SequenceEqual(new[] { "SeekBack" }), "rejected change preserves active raw binding");
            actions.Clear(); Mouse(0x40, 10600); keys.Dispose(); Drain();
            Check(actions.Count == 0 && !keys.RawInputAvailable && !keys.MouseHookAvailable
                && RegisteredDevices().Length == 0, "dispose drops queued work and removes native registrations");
        }
        finally { keys.Dispose(); window.Close(); }
        Console.WriteLine($"{_checks} input checks passed; synthetic events and native registration only, no physical game test.");

        void Apply(Dictionary<string, string> bindings)
        {
            Check(keys.Apply(bindings) == null, "test mouse bindings register");
            actions.Clear();
        }
        void Mouse(ushort flags, long timestamp) => keys.ProcessRawInput(new()
        {
            Header = new() { Type = 0 }, Data = new() { Mouse = new() { ButtonFlags = flags } }
        }, (nint)123, timestamp);
        void Key(ushort key, bool up, long timestamp, bool extended = false, ushort scan = 0x2A) => keys.ProcessRawInput(new()
        {
            Header = new() { Type = 1 }, Data = new() { Keyboard = new()
            { VirtualKey = key, MakeCode = scan, Flags = (ushort)((up ? 1 : 0) | (extended ? 2 : 0)) } }
        }, (nint)123, timestamp);
        void ResetModifiers()
        {
            foreach (var key in new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }) Key(key, true, 0);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Device
    {
        public ushort Page, Usage;
        public uint Flags;
        public nint Target;
    }
    private static Device[] RegisteredDevices()
    {
        uint count = 0;
        Check(GetRegisteredRawInputDevices(null, ref count, (uint)Marshal.SizeOf<Device>()) != uint.MaxValue,
            "raw registration list can be queried");
        if (count == 0) return [];
        var devices = new Device[count];
        Check(GetRegisteredRawInputDevices(devices, ref count, (uint)Marshal.SizeOf<Device>()) == count,
            "registered raw devices can be read");
        return devices;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] Device[]? devices, ref uint count, uint size);
}
