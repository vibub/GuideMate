using System.Threading;

namespace GuideMate.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--danmaku-host")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await DanmakuOverlay.RunHostAsync(e.Args[1]); }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.Text.Json.JsonException)
            { System.Diagnostics.Trace.WriteLine("Danmaku renderer: " + ex.Message); }
            finally { Shutdown(); }
            return;
        }
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Color.FromRgb(19, 124, 102));
        var isolated = e.Args.Contains("--isolated") || e.Args.Contains("--smoke-test");
        _instance = new Mutex(true, isolated ? "Local\\GuideMate.Isolated." + Guid.NewGuid() : "Local\\GuideMate.Desktop", out var first);
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
                dataFlag >= 0 && dataFlag + 1 < e.Args.Length ? e.Args[dataFlag + 1] : null, isolated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {
            MessageBox.Show("无法使用原用户数据目录：" + ex.Message + "\n设置与登录资料未被重置。", "随引");
            Shutdown(); return;
        }
        var mediaFlag = Array.IndexOf(e.Args, "--media");
        var mediaPath = mediaFlag >= 0 && mediaFlag + 1 < e.Args.Length ? e.Args[mediaFlag + 1] : null;
        var window = new MainWindow(dataPath, isolated, mediaPath);
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
