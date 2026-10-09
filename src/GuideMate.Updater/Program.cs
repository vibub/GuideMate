using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GuideMate.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length || args[i] is not ("--version" or "--install-dir" or "--data-dir" or "--wait-pid" or "--ready-pipe" or "--work-dir")
                    || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("更新参数无效。");
            }
            if (!options.TryGetValue("--work-dir", out var work))
            {
                work = Path.Combine(Path.GetTempPath(), "GuideMate-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
                    File.Copy(file, Path.Combine(work, Path.GetFileName(file)));
                var start = new ProcessStartInfo(Path.Combine(work, "GuideMate.Updater.exe")) { UseShellExecute = false, WorkingDirectory = work };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                if (!options.ContainsKey("--install-dir"))
                {
                    start.ArgumentList.Add("--install-dir");
                    var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
                    start.ArgumentList.Add(Path.GetFileName(directory).Equals("updater", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(directory)! : directory);
                }
                start.ArgumentList.Add("--work-dir"); start.ArgumentList.Add(work);
                try { Process.Start(start)?.Dispose(); }
                catch { Directory.Delete(work, true); throw; }
                return 0;
            }
            ValidateWorkDirectory(work);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new UpdaterWindow(options, work);
            app.Exit += (_, _) => { if (!window.KeepRecoveryFiles) ScheduleCleanup(work); };
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("更新程序无法启动：" + ex.Message, "随引更新程序", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static void ValidateWorkDirectory(string work)
    {
        var full = Path.GetFullPath(work);
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "^GuideMate-update-[0-9a-f]{32}$")
            || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("更新暂存目录无效。");
    }

    private static void ScheduleCleanup(string work)
    {
        // Wait for this EXE to unlock before removing only its generated temporary directory.
        var quoted = work.Replace("'", "''");
        var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; "
            + $"$updateTemp = [IO.Path]::GetFullPath('{quoted}'); "
            + "if ([IO.Path]::GetDirectoryName($updateTemp) -eq [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\\') "
            + "-and [IO.Path]::GetFileName($updateTemp) -match '^GuideMate-update-[0-9a-f]{32}$') { "
            + "if (Test-Path -LiteralPath $updateTemp) { Remove-Item -LiteralPath $updateTemp -Recurse -Force } }";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script }) start.ArgumentList.Add(arg);
        try { Process.Start(start)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Trace.WriteLine("更新暂存清理未启动：" + ex.Message); }
    }
}
