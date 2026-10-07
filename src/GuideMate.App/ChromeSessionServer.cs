using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Text.Json;

namespace GuideMate.App;

internal sealed class ChromeSessionServer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<ChromeTransfer, CancellationToken, Task<BridgeReply>> _import;
    private readonly string _pipeName;
    public string PairingCode { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public Task Completion { get; }

    public ChromeSessionServer(Func<ChromeTransfer, CancellationToken, Task<BridgeReply>> import, string? pipeName = null)
    {
        _import = import; _pipeName = pipeName ?? ChromeBridge.PipeName;
        Completion = RunAsync();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                BridgeReply reply;
                try
                {
                    var transfer = ChromeBridge.Parse(await ChromeBridge.ReadAsync(pipe, timeout.Token));
                    if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(transfer.PairingCode), Convert.FromHexString(PairingCode)))
                        reply = new(false, "配对码不匹配。请使用当前随引窗口中的配对码。");
                    else reply = await _import(transfer, timeout.Token).WaitAsync(timeout.Token);
                }
                catch (Exception ex) when (ex is FormatException or JsonException) { reply = new(false, "同步数据格式无效。未导入。"); }
                await ChromeBridge.WriteAsync(pipe, reply, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (!_stop.IsCancellationRequested) await Task.Delay(250).ConfigureAwait(false);
            }
        }
    }
    public void Dispose() => _stop.Cancel();
}
