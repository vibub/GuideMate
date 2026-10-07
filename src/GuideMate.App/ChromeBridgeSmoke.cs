using System.Diagnostics;
using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyChromeHostAsync(List<string> checks)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "chrome-host", "GuideMate.ChromeHost.exe");
        if (!File.Exists(exe)) return;
        var pipeName = "GuideMate.BridgeTest." + Guid.NewGuid().ToString("N");
        var accepted = 0;
        using var server = new ChromeSessionServer((transfer, _) => { accepted++; return Task.FromResult(new BridgeReply(true, "synthetic accepted", transfer.Cookies.Count)); }, pipeName);
        async Task<BridgeReply> SendAsync(string pairing, string? origin = null)
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
            info.ArgumentList.Add(origin ?? $"chrome-extension://{ChromeBridge.ExtensionId}/");
            info.Environment["GUIDEMATE_TEST_PIPE"] = pipeName;
            using var host = Process.Start(info)!;
            await ChromeBridge.WriteAsync(host.StandardInput.BaseStream, new ChromeTransfer { Type = "import-bilibili", PairingCode = pairing,
                Cookies = [new() { Name = "synthetic", Value = "test-only", Domain = ".bilibili.com", Session = true }] });
            host.StandardInput.Close();
            var response = JsonSerializer.Deserialize<BridgeReply>(await ChromeBridge.ReadAsync(host.StandardOutput.BaseStream), ChromeBridge.JsonOptions)!;
            await host.WaitForExitAsync();
            return response;
        }
        var foreign = await SendAsync(server.PairingCode, "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/");
        if (foreign.Success || accepted != 0) throw new Exception("Foreign extension origin was accepted");
        checks.Add("native host rejects an unapproved extension origin");
        var wrong = await SendAsync(new('0', 32));
        if (wrong.Success || accepted != 0) throw new Exception("Incorrect pairing was accepted");
        checks.Add("native host rejects incorrect pairing without import");
        var correct = await SendAsync(server.PairingCode);
        if (!correct.Success || correct.Imported != 1 || accepted != 1) throw new Exception("Native host did not complete framed pipe transfer");
        checks.Add("native host framing and current-user pipe transfer succeed with synthetic cookies");
        server.Dispose();
        try { await server.Completion; } catch (OperationCanceledException) { }
    }
}
