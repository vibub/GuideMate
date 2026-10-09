using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuideMate.Core;

namespace GuideMate.Updater;

internal sealed class UpdaterWindow : Window
{
    private readonly Dictionary<string, string> _options;
    private readonly string _work;
    private readonly TextBox _directory;
    private readonly TextBox _version;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 18, 0, 12) };
    private readonly ProgressBar _progress = new() { Height = 6, Maximum = 1 };
    private readonly Button _update = new() { Content = "下载并安装", Padding = new(18, 9, 18, 9), Margin = new(0, 16, 10, 0) };
    private readonly Button _exit = new() { Content = "退出", Padding = new(18, 9, 18, 9), Margin = new(0, 16, 0, 0) };
    private CancellationTokenSource? _cancellation;
    private bool _installing, _complete;
    private string? _dataDirectory;
    public bool KeepRecoveryFiles { get; private set; }

    public UpdaterWindow(Dictionary<string, string> options, string work)
    {
        _options = options; _work = work;
        Title = "随引更新程序"; Width = 550; Height = 460; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = options.ContainsKey("--ready-pipe");
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 247));
        var panel = new StackPanel { Margin = new(28) };
        panel.Children.Add(new TextBlock { Text = "更新随引", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "从 GitHub Release 下载完整安装包，保留设置、热键与登录资料。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "安装目录" });
        _directory = new TextBox { Text = options.GetValueOrDefault("--install-dir", ""), Padding = new(8), Margin = new(0, 6, 0, 12), IsReadOnly = options.ContainsKey("--ready-pipe") };
        panel.Children.Add(_directory);
        panel.Children.Add(new TextBlock { Text = "Release 版本（留空使用最新正式版）" });
        _version = new TextBox { Text = options.GetValueOrDefault("--version", ""), Padding = new(8), Margin = new(0, 6, 0, 0), IsReadOnly = options.ContainsKey("--ready-pipe") };
        panel.Children.Add(_version);
        _status.Text = "下载并校验完成后，请正常退出随引以保存播放进度。";
        panel.Children.Add(_status); panel.Children.Add(_progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_update); actions.Children.Add(_exit); panel.Children.Add(actions);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _update.Click += async (_, _) => { if (_complete) LaunchApp(); else await UpdateAsync(); };
        _exit.Click += (_, _) => { if (_cancellation != null) _cancellation.Cancel(); else Close(); };
        Closing += (_, e) =>
        {
            if (_cancellation == null) return;
            e.Cancel = true;
            if (!_installing) _cancellation.Cancel();
        };
        if (options.ContainsKey("--ready-pipe")) Loaded += async (_, _) => await UpdateAsync();
    }

    private async Task UpdateAsync()
    {
        _cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var token = _cancellation.Token;
        _update.IsEnabled = false; _directory.IsEnabled = false; _version.IsEnabled = false; _exit.Content = "取消";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuideMate-Updater/1.0");
        Mutex? lease = null; bool ownsLease = false;
        try
        {
            var install = Path.GetFullPath(_directory.Text.Trim());
            if (!File.Exists(Path.Combine(install, "GuideMate.exe"))) throw new IOException("请选择包含 GuideMate.exe 的安装目录。");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(install.ToUpperInvariant())));
            lease = new Mutex(false, "Local\\GuideMate.Update." + key);
            try { ownsLease = lease.WaitOne(0); } catch (AbandonedMutexException) { ownsLease = true; }
            if (!ownsLease) throw new IOException("此安装目录已有更新任务，请等待其完成。");
            _dataDirectory = ResolveDataDirectory();
            _status.Text = "正在查询 Release…"; _progress.IsIndeterminate = true;
            var tag = string.IsNullOrWhiteSpace(_version.Text) ? null : _version.Text.Trim();
            var client = new GitHubReleaseClient(http);
            var release = await client.GetAsync(tag, token) ?? throw new IOException("仓库尚无公开的正式 Release。");
            _progress.IsIndeterminate = false; _progress.Value = 0;
            _status.Text = "正在下载 " + release.Tag + "…";
            var zip = Path.Combine(_work, "package.zip");
            if (File.Exists(zip)) File.Delete(zip);
            await client.DownloadAsync(release, zip, new Progress<double>(value => { _progress.Value = value; _status.Text = $"正在下载 {release.Tag}… {value:P0}"; }), token);
            _status.Text = "校验通过，正在准备安装…"; _progress.IsIndeterminate = true;
            var stage = Path.Combine(_work, "stage-" + Guid.NewGuid().ToString("N"));
            await Task.Run(() => UpdatePackage.Extract(zip, stage, release.Tag), token);
            if (_options.TryGetValue("--ready-pipe", out var pipeName))
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(15000, token);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync("ready".AsMemory(), token);
                _options.Remove("--ready-pipe");
            }
            _status.Text = "安装包已就绪，等待随引正常退出以保存设置…";
            await WaitForAppExitAsync(install, token);
            token.ThrowIfCancellationRequested();
            _installing = true; _exit.IsEnabled = false;
            _status.Text = "正在覆盖安装，请稍候…";
            await Task.Run(() => UpdatePackage.Install(stage, install, Path.Combine(_work, "backup-" + Guid.NewGuid().ToString("N")), _dataDirectory));
            ShowCompletion(release.Tag, install);
        }
        catch (OperationCanceledException) { _status.Text = "更新已取消或超时，未完成覆盖安装。"; }
        catch (Exception ex)
        {
            KeepRecoveryFiles = ex is UpdateRecoveryException;
            _status.Text = "更新失败：" + ex.Message;
        }
        finally
        {
            if (ownsLease) lease!.ReleaseMutex();
            lease?.Dispose(); _installing = false;
            _cancellation.Dispose(); _cancellation = null;
            _progress.IsIndeterminate = false;
            _update.IsEnabled = !KeepRecoveryFiles; _exit.IsEnabled = true; _exit.Content = "退出";
            _directory.IsEnabled = !_complete; _version.IsEnabled = !_complete;
        }
    }

    private void ShowCompletion(string tag, string install)
    {
        _complete = true; _directory.Text = install; Topmost = false;
        _status.Text = tag + " 更新完成。原设置、热键、播放记录与登录资料已保留。";
        _update.Content = "启动随引";
    }

    private string ResolveDataDirectory()
    {
        if (_options.TryGetValue("--data-dir", out var data)) return Path.GetFullPath(data);
        var defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideMate");
        return DataDirectory.Resolve(defaultDirectory, null, false);
    }

    private async Task WaitForAppExitAsync(string install, CancellationToken token)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var running = false;
            foreach (var process in Process.GetProcessesByName("GuideMate"))
            {
                using (process)
                {
                    try { running |= !process.HasExited && string.Equals(process.MainModule?.FileName, Path.Combine(install, "GuideMate.exe"), StringComparison.OrdinalIgnoreCase); }
                    catch (InvalidOperationException) { }
                }
            }
            if (_options.TryGetValue("--wait-pid", out var pidText))
            {
                if (!int.TryParse(pidText, out var pid)) throw new ArgumentException("等待进程参数无效。");
                try { using var parent = Process.GetProcessById(pid); running |= !parent.HasExited; }
                catch (ArgumentException) { }
            }
            if (!running) return;
            await Task.Delay(500, token);
        }
        throw new IOException("随引仍在运行。请从托盘正常退出后重新更新；未强制结束进程。");
    }

    private void LaunchApp()
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(_directory.Text, "GuideMate.exe")) { UseShellExecute = false, WorkingDirectory = _directory.Text };
            if (_dataDirectory != null) { start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(_dataDirectory); }
            Process.Start(start)?.Dispose(); Close();
        }
        catch (Exception ex) { _status.Text = "更新已完成，启动失败：" + ex.Message; }
    }
}
