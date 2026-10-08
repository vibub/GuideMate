using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Windows.Threading;

namespace GuideMate.App;

internal sealed record DanmakuState(bool Visible, Rectangle Bounds, double Area, double Opacity,
    double FontScale, double Speed, JsonElement Items, double Time, bool Paused, double Rate, int Generation, long Snapshot);

// WPF dispatchers share a compositor within one process. Isolate the full-screen layered window too.
internal sealed class DanmakuOverlay : IDisposable
{
    private readonly Channel<DanmakuState> _states = Channel.CreateBounded<DanmakuState>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Process _process;
    public event Action<string>? Failed;

    public DanmakuOverlay(string executable)
    {
        var pipeName = "GuideMate.Danmaku." + Guid.NewGuid().ToString("N");
        _pipe = new(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--danmaku-host"); start.ArgumentList.Add(pipeName);
        _process = Process.Start(start)!;
        _ = WriteStatesAsync();
    }

    public void SetState(DanmakuState state) => _states.Writer.TryWrite(state);

    private async Task WriteStatesAsync()
    {
        try
        {
            var connecting = _pipe.WaitForConnectionAsync(_lifetime.Token);
            var exiting = _process.WaitForExitAsync(_lifetime.Token);
            if (await Task.WhenAny(connecting, exiting).ConfigureAwait(false) == exiting)
                throw new IOException("弹幕绘制进程未能启动。");
            await connecting.ConfigureAwait(false);
            using var writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            await foreach (var state in _states.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
                await writer.WriteLineAsync(JsonSerializer.Serialize(state).AsMemory(), _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            if (!_lifetime.IsCancellationRequested) Failed?.Invoke("全屏弹幕绘制失败：" + ex.Message);
        }
        finally { _pipe.Dispose(); _process.Dispose(); }
    }

    public void Dispose()
    {
        _states.Writer.TryComplete();
        _lifetime.Cancel();
        _pipe.Dispose(); // EOF closes only the child renderer; no production window is killed.
    }

    public static async Task RunHostAsync(string pipeName)
    {
        var renderer = new Renderer();
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            using var reader = new StreamReader(pipe);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } json)
            {
                var state = JsonSerializer.Deserialize<DanmakuState>(json);
                if (state != null) renderer.SetState(state);
            }
        }
        finally { await renderer.Dispatcher.InvokeAsync(renderer.Close); }
    }

    private sealed class Renderer
    {
        private readonly object _gate = new();
        private readonly DanmakuWindow _window = new() { Style = null };
        private DanmakuState? _pending;
        private bool _scheduled;
        private int _generation = -1;
        private long _snapshot = -1;
        private (Rectangle Bounds, double Area, double Opacity, double Font, double Speed) _appearance;
        public Dispatcher Dispatcher => _window.Dispatcher;
        public void Close() => _window.Close();

        public void SetState(DanmakuState state)
        {
            lock (_gate)
            {
                _pending = state;
                if (_scheduled) return;
                _scheduled = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyPending);
            }
        }
        private void ApplyPending()
        {
            DanmakuState state;
            lock (_gate)
            {
                _scheduled = false;
                if (_pending == null) return;
                state = _pending; _pending = null;
            }
            var window = _window!;
            if (!state.Visible) { window.Hide(); _snapshot = -1; return; }
            var showing = !window.IsVisible;
            if (showing) window.Show();
            var appearance = (state.Bounds, state.Area, state.Opacity, state.FontScale, state.Speed);
            var changed = _appearance != appearance || _generation != state.Generation;
            if (changed || showing)
            {
                window.FitToMonitor(state.Bounds);
                window.Configure(state.Area, state.Opacity, state.FontScale, state.Speed);
                window.Clear();
                _appearance = appearance; _generation = state.Generation;
            }
            // Moving the video within a monitor only publishes bounds; it must not rebase the clock.
            if (state.Items.ValueKind == JsonValueKind.Array && (changed || showing || _snapshot != state.Snapshot))
            {
                window.Update(state.Items, state.Time, state.Paused, state.Rate);
                _snapshot = state.Snapshot;
            }
        }

    }
}
