using Microsoft.Web.WebView2.Core;
using System.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private ChromeSessionServer? _chromeServer;

    private void StartChromeBridge()
    {
        _chromeServer = new((transfer, cancellation) => Dispatcher.InvokeAsync(() => ConfirmImportAsync(transfer, cancellation)).Task.Unwrap());
    }

    private void OpenChromeSync()
    {
        if (_chromeServer == null) { _status.Text = "浏览器或同步组件尚未就绪。"; return; }
        var panel = new StackPanel { Margin = new(22) };
        panel.Children.Add(Ui.Heading("B 站"));
        panel.Children.Add(Ui.Text("等待 Chrome 扩展连接", 13, Ui.Green));
        panel.Children.Add(Ui.Heading("本次配对码"));
        panel.Children.Add(new TextBox { Text = _chromeServer.PairingCode, IsReadOnly = true });
        panel.Children.Add(Ui.Command("\uE8C8", "复制配对码", () => Clipboard.SetText(_chromeServer.PairingCode)));
        var registered = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\com.guidemate.bilibili", "", null) != null;
        panel.Children.Add(Ui.Text(registered ? "本机通信组件：已注册" : "本机通信组件：未注册", 12, Ui.Muted));
        new Window { Title = "随引 · Chrome 登录同步", Owner = this, Width = 440, Height = 300,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, Content = panel }.ShowDialog();
    }

    private async Task<BridgeReply> ConfirmImportAsync(ChromeTransfer transfer, CancellationToken cancellation)
    {
        if (_closing || _browser.CoreWebView2 == null) return new(false, "随引正在关闭或浏览器尚未就绪。");
        if (transfer.Cookies.Count == 0) return new(false, "Chrome 没有可导入的 B 站会话。");
        Show(); Activate();
        var answer = MessageBox.Show(this,
            $"将导入 Chrome 当前配置中的 {transfer.Cookies.Count} 个 B 站 Cookie，更新随引的 B 站登录态。\n\n仅限 bilibili.com，不读取其他站点，不上传至服务器。\n分区 Cookie 已跳过：{transfer.SkippedPartitioned}。\n\n是否导入？",
            "随引 · 确认 B 站登录同步", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || cancellation.IsCancellationRequested || _closing) return new(false, "已取消或请求已过期，未修改登录态。");
        var result = await ImportCookiesAsync(transfer.Cookies);
        _status.Text = result.Message;
        if (result.Success && Uri.TryCreate(_url, UriKind.Absolute, out var uri) && ChromeBridge.IsAllowedDomain(uri.Host))
            _browser.CoreWebView2.Reload();
        return result;
    }

    private async Task<BridgeReply> ImportCookiesAsync(IReadOnlyList<ChromeCookie> cookies)
    {
        var manager = _browser.CoreWebView2.CookieManager;
        var prepared = new List<CoreWebView2Cookie>();
        var previous = new Dictionary<(string Name, string Domain, string Path), CoreWebView2Cookie>();
        foreach (var group in cookies.GroupBy(c => "https://" + c.Domain.TrimStart('.') + c.Path))
            foreach (var existing in await manager.GetCookiesAsync(group.Key))
                if (ChromeBridge.IsAllowedDomain(existing.Domain)) previous.TryAdd((existing.Name, existing.Domain, existing.Path), existing);
        try
        {
            foreach (var source in cookies)
            {
                if (!source.Session && source.ExpirationDate <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
                var cookie = manager.CreateCookie(source.Name, source.Value, source.Domain, source.Path);
                cookie.IsHttpOnly = source.HttpOnly; cookie.IsSecure = source.Secure;
                cookie.SameSite = source.SameSite switch { "strict" => CoreWebView2CookieSameSiteKind.Strict, "no_restriction" => CoreWebView2CookieSameSiteKind.None, _ => CoreWebView2CookieSameSiteKind.Lax };
                if (!source.Session) cookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)source.ExpirationDate!.Value).UtcDateTime;
                prepared.Add(cookie);
            }
            foreach (var cookie in prepared) manager.AddOrUpdateCookie(cookie);
            return new(true, $"B 站会话已导入 {prepared.Count} 项。登录是否有效由 B 站验证。", prepared.Count);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            foreach (var cookie in prepared)
            {
                if (previous.TryGetValue((cookie.Name, cookie.Domain, cookie.Path), out var old)) manager.AddOrUpdateCookie(old);
                else manager.DeleteCookie(cookie);
            }
            return new(false, "B 站会话导入失败，已恢复原 Cookie。");
        }
    }
}
