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
        try { Process.Start(CreateCleanupStartInfo(work, Environment.ProcessId))?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show("无法启动临时文件清理：" + ex.Message + "\n剩余临时文件位于：" + work,
                "随引更新程序", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static ProcessStartInfo CreateCleanupStartInfo(string work, int processId)
    {
        ValidateWorkDirectory(work);
        var quoted = Path.GetFullPath(work).Replace("'", "''");
        // Show cleanup explicitly and wait for the updater to release its own temporary EXE and DLLs.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $updateTemp = [IO.Path]::GetFullPath('{{quoted}}')
            if (-not [Console]::IsOutputRedirected) { $Host.UI.RawUI.WindowTitle = '随引 - 清理临时文件' }
            Write-Host '正在清理临时文件，请稍候…' -ForegroundColor Cyan
            Write-Host ('清理目录：' + $updateTemp)
            Write-Host '等待更新程序退出…'
            Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue
            try {
                if ([IO.Path]::GetDirectoryName($updateTemp) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or
                    [IO.Path]::GetFileName($updateTemp) -notmatch '^GuideMate-update-[0-9a-f]{32}$') { throw '更新暂存目录无效。' }
                if (Test-Path -LiteralPath $updateTemp) {
                    if ((Get-Item -LiteralPath $updateTemp -Force).Attributes -band [IO.FileAttributes]::ReparsePoint -or
                        (Get-ChildItem -LiteralPath $updateTemp -Recurse -Force -Attributes ReparsePoint)) { throw '暂存目录中存在链接，已停止清理。' }
                    Remove-Item -LiteralPath $updateTemp -Recurse -Force
                }
                Write-Host '临时文件清理完成，窗口即将关闭。' -ForegroundColor Green
                Start-Sleep -Seconds 1
                exit 0
            } catch {
                Write-Host ('临时文件清理失败：' + $_.Exception.Message) -ForegroundColor Red
                Write-Host '剩余临时文件将保留在上述目录。此窗口将在 5 秒后关闭。'
                Start-Sleep -Seconds 5
                exit 1
            }
            """;
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Normal", "-Command", script }) start.ArgumentList.Add(arg);
        return start;
    }
}
