using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    internal static string CurrentVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _checkingUpdates, _startingUpdater;

    private async Task CheckUpdatesAtStartupAsync()
    {
        if (_isolated || !_settings.CheckUpdatesOnStartup) return;
        try { await Task.Delay(2000, _updateLifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (_settings.CheckUpdatesOnStartup) await CheckForUpdatesAsync(manual: false);
    }

    internal async Task CheckForUpdatesAsync(Window? owner = null, bool manual = true)
    {
        owner ??= OwnedWindows.OfType<Window>().LastOrDefault(window => window.IsVisible) ?? this;
        if (_checkingUpdates || _startingUpdater)
        {
            if (manual) MessageBox.Show(owner, "已有更新检查或下载任务，请稍候。", "随引更新");
            return;
        }
        _checkingUpdates = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var release = await new GitHubReleaseClient(http).GetAsync(null, _updateLifetime.Token);
            if (_closing) return;
            if (!owner.IsVisible) owner = this;
            if (release == null || ReleaseVersion.Parse(release.Tag).CompareTo(ReleaseVersion.Parse(CurrentVersion)) <= 0)
            {
                if (manual) MessageBox.Show(owner, release == null ? "仓库尚无公开的正式 Release。" : "当前已是最新版本（" + CurrentVersion + "）。", "随引更新");
                return;
            }
            var dialog = new UpdateAvailableWindow(owner, CurrentVersion, release, !_isolated);
            if (dialog.ShowDialog() == true) await StartUpdaterAsync(release.Tag, owner);
        }
        catch (OperationCanceledException)
        {
            if (manual && !_closing) MessageBox.Show(owner.IsVisible ? owner : this, "检查更新超时，请检查网络后重试。", "随引更新");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (manual && !_closing) MessageBox.Show(owner.IsVisible ? owner : this, "检查更新失败：" + ex.Message, "随引更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            else Trace.WriteLine("启动检查更新失败：" + ex.Message);
        }
        finally { _checkingUpdates = false; }
    }

    private async Task StartUpdaterAsync(string tag, Window owner)
    {
        _startingUpdater = true;
        string? work = null; bool launched = false;
        try
        {
            var updaterDirectory = Path.Combine(AppContext.BaseDirectory, "updater");
            if (!File.Exists(Path.Combine(updaterDirectory, "GuideMate.Updater.exe")))
                throw new IOException("更新程序缺失，请从仓库 Release 下载完整安装包。");
            work = Path.Combine(Path.GetTempPath(), "GuideMate-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            foreach (var file in Directory.EnumerateFiles(updaterDirectory)) File.Copy(file, Path.Combine(work, Path.GetFileName(file)));
            var pipeName = "GuideMate-update-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(Path.Combine(work, "GuideMate.Updater.exe")) { UseShellExecute = false, WorkingDirectory = work };
            foreach (var arg in new[] { "--version", tag, "--install-dir", AppContext.BaseDirectory, "--data-dir", _dataPath,
                "--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--ready-pipe", pipeName, "--work-dir", work })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("无法启动更新程序。");
            launched = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(21));
            async Task<bool> ReadyAsync()
            {
                await pipe.WaitForConnectionAsync(timeout.Token);
                using var reader = new StreamReader(pipe);
                return await reader.ReadLineAsync(timeout.Token) == "ready";
            }
            var ready = ReadyAsync();
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(ready, exited) != ready)
            {
                timeout.Cancel();
                try { await ready; } catch (OperationCanceledException) { }
                return;
            }
            if (!await ready) throw new IOException("更新程序未能准备安装包。");
            UpdateHistory(); SaveSettings();
            // Ensure a failed save leaves the app open and prevents the updater from installing.
            _store.Save(_settings);
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { if (!_closing) MessageBox.Show(owner.IsVisible ? owner : this, "无法开始更新：" + ex.Message, "随引更新", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            _startingUpdater = false;
            if (!launched && work != null && Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
}
