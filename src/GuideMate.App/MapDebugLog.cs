using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace GuideMate.App;

internal sealed class MapDebugLog
{
    private const long MaximumBytes = 20 * 1024 * 1024;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly long _started = Environment.TickCount64;
    private long _sequence, _dropped;
    private volatile string? _problem;
    public string FilePath { get; }
    public string? Problem => _problem;
    public Task Completion { get; }

    public MapDebugLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"map-debug-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        Completion = Task.Run(() => WriteAsync(stream));
        Record("session_start", new { version = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
            maximumBytes = MaximumBytes, formatVersion = 1 });
    }

    public void Record(string kind, object? detail)
    {
        if (_problem != null) return;
        var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, elapsedMs = Environment.TickCount64 - _started,
            sequence = ++_sequence, kind, droppedBefore = _dropped, detail });
        if (!_queue.Writer.TryWrite(line)) _dropped++;
    }

    public void Complete()
    {
        Record("session_end", new { dropped = _dropped });
        _queue.Writer.TryComplete();
    }

    private async Task WriteAsync(FileStream stream)
    {
        try
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
            long bytes = 0;
            while (await _queue.Reader.WaitToReadAsync())
            {
                while (_queue.Reader.TryRead(out var line))
                {
                    var size = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                    if (bytes + size > MaximumBytes)
                    {
                        _problem = "已达到单次 20 MiB 上限，已停止写入；重新勾选可创建新日志。";
                        _queue.Writer.TryComplete(); return;
                    }
                    await writer.WriteLineAsync(line); bytes += size;
                }
                await writer.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _problem = $"日志写入失败（{ex.GetType().Name}），已停止写入。";
            _queue.Writer.TryComplete();
        }
        finally { await stream.DisposeAsync(); }
    }
}
