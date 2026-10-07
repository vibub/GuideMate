using System.IO.Pipes;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using GuideMate.Core;

// Stdout is reserved for Chrome's length-prefixed protocol. Never log cookie data.
BridgeReply reply;
try
{
    if (args.Length == 0 || args[0] != $"chrome-extension://{ChromeBridge.ExtensionId}/")
        throw new FormatException("只允许随引 B 站扩展发起同步。");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var payload = await ChromeBridge.ReadAsync(Console.OpenStandardInput(), timeout.Token);
    var transfer = ChromeBridge.Parse(payload);
    var pipeName = Environment.GetEnvironmentVariable("GUIDEMATE_TEST_PIPE") ?? ChromeBridge.PipeName;
    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000, timeout.Token);
    if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId)) throw new IOException("Pipe identity unavailable.");
    using var receiver = Process.GetProcessById((int)processId);
    var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "GuideMate.exe"));
    if (!string.Equals(receiver.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Unexpected pipe receiver.");
    await ChromeBridge.WriteAsync(pipe, transfer, timeout.Token);
    reply = JsonSerializer.Deserialize<BridgeReply>(await ChromeBridge.ReadAsync(pipe, timeout.Token), ChromeBridge.JsonOptions)
        ?? new(false, "随引没有返回结果。");
}
catch (FormatException ex) { reply = new(false, ex.Message); }
catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
{ reply = new(false, "连接或同步失败。请先打开随引，再检查配对码和本机通信组件。"); }
await ChromeBridge.WriteAsync(Console.OpenStandardOutput(), reply);

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
