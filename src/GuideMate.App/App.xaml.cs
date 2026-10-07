using System.Threading;

namespace GuideMate.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var smoke = e.Args.Contains("--smoke-test");
        _instance = new Mutex(true, smoke ? "Local\\GuideMate.Smoke" : "Local\\GuideMate.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("随引已在运行。请通过托盘或紧急恢复热键恢复窗口（Ctrl+Alt+F10，冲突时为 Ctrl+Alt+Shift+F10）。", "随引");
            Shutdown();
            return;
        }
        var dataFlag = Array.IndexOf(e.Args, "--data-dir");
        var defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideMate");
        string dataPath;
        try
        {
            dataPath = DataDirectory.Resolve(defaultDirectory,
                dataFlag >= 0 && dataFlag + 1 < e.Args.Length ? e.Args[dataFlag + 1] : null, smoke);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {
            MessageBox.Show("无法使用原用户数据目录：" + ex.Message + "\n设置与登录资料未被重置。", "随引");
            Shutdown(); return;
        }
        var mediaFlag = Array.IndexOf(e.Args, "--media");
        var mediaPath = mediaFlag >= 0 && mediaFlag + 1 < e.Args.Length ? e.Args[mediaFlag + 1] : null;
        var profileProbe = smoke ? e.Args.Contains("--profile-probe-write") ? "write" : e.Args.Contains("--profile-probe-read") ? "read" : null : null;
        var episodeFlag = Array.IndexOf(e.Args, "--bilibili-episode-probe");
        var episodeProbe = smoke && episodeFlag >= 0 && episodeFlag + 1 < e.Args.Length ? e.Args[episodeFlag + 1] : null;
        var onlineFlag = Array.IndexOf(e.Args, "--online-vision-live");
        var onlineUrl = smoke && onlineFlag >= 0 && onlineFlag + 1 < e.Args.Length ? e.Args[onlineFlag + 1] : null;
        var danmakuFlag = Array.IndexOf(e.Args, "--bilibili-danmaku-live");
        var danmakuUrl = smoke && danmakuFlag >= 0 && danmakuFlag + 1 < e.Args.Length ? e.Args[danmakuFlag + 1] : null;
        var window = new MainWindow(dataPath, smoke, mediaPath, profileProbe, episodeProbe, smoke && e.Args.Contains("--small-window-probe"),
            smoke && (e.Args.Contains("--online-vision-probe") || onlineUrl != null), onlineUrl, smoke && (e.Args.Contains("--danmaku-probe") || danmakuUrl != null), danmakuUrl);
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
