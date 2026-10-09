using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text;

internal static class CleanupChecks
{
    public static void Run(string root)
    {
        var checks = 0;
        var work = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GuideMate-update-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work);
        var protectedFile = Path.Combine(root, "preserved-user-data.txt");
        File.WriteAllText(protectedFile, "synthetic settings and login data");
        var method = Assembly.Load("GuideMate.Updater").GetType("GuideMate.Updater.Program")!
            .GetMethod("CreateCleanupStartInfo", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            var start = Create();
            Check(start.UseShellExecute && !start.CreateNoWindow && start.WindowStyle == ProcessWindowStyle.Normal
                && !start.ArgumentList.Contains("Hidden"), "cleanup uses a normal visible console");
            Check(Path.IsPathFullyQualified(start.FileName) && start.FileName.EndsWith("WindowsPowerShell\\v1.0\\powershell.exe", StringComparison.OrdinalIgnoreCase), "cleanup uses the system PowerShell executable");
            var nested = Path.Combine(work, "stage"); Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "临时文件 [test].txt"), "temporary package data");
            var result = Execute(start);
            Check(result.Code == 0 && result.Output.Contains("正在清理临时文件") && result.Output.Contains("临时文件清理完成"), "cleanup reports progress and completion");
            Check(!Directory.Exists(work) && File.ReadAllText(protectedFile) == "synthetic settings and login data", "cleanup deletes only its exact temporary directory");
            Directory.CreateDirectory(work);
            var lockedFile = Path.Combine(work, "locked.dll"); File.WriteAllText(lockedFile, "locked updater dependency");
            using (var locked = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = Execute(Create());
                Check(result.Code == 1 && result.Output.Contains("临时文件清理失败") && result.Output.Contains("剩余临时文件"), "locked files produce visible failure instead of false success");
                Check(File.Exists(lockedFile) && File.Exists(protectedFile), "failed cleanup retains locked files and leaves user data intact");
            }
            try { method.Invoke(null, [root, int.MaxValue]); throw new Exception("Unsafe cleanup path was accepted."); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException)
            { Check(true, "cleanup rejects unrelated directories before launching PowerShell"); }
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        Console.WriteLine($"Cleanup checks passed: {checks}");

        ProcessStartInfo Create() => (ProcessStartInfo)method.Invoke(null, [work, int.MaxValue])!;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    }

    private static (int Code, string Output) Execute(ProcessStartInfo start)
    {
        // Capture script results on CI; production still opens its normal console via the shell.
        start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
        start.ArgumentList[^1] = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " + start.ArgumentList[^1];
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) throw new Exception("Cleanup script timed out.");
        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }
}
