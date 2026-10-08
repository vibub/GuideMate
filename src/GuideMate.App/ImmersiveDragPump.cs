using System.Threading;
using System.Windows.Threading;

namespace GuideMate.App;

// Owns one drag's cadence and pending callback; it never opens a window or moves one off-thread.
internal sealed class ImmersiveDragPump : IDisposable
{
    private readonly Timer _timer;
    private int _queued, _stopped;

    public ImmersiveDragPump(Dispatcher dispatcher, Action tick)
    {
        // DispatcherTimer promotion itself waits for the UI message queue. Trigger independently,
        // then sample the newest physical cursor before queued rendering/ordinary input callbacks.
        _timer = new Timer(_ =>
        {
            if (Volatile.Read(ref _stopped) != 0 || Interlocked.CompareExchange(ref _queued, 1, 0) != 0) return;
            _ = dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() =>
            {
                try { if (Volatile.Read(ref _stopped) == 0) tick(); }
                finally { Interlocked.Exchange(ref _queued, 0); }
            }));
        }, null, 16, 16);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _timer.Dispose();
    }
}
