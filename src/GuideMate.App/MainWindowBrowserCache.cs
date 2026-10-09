using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _webViewCacheTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMinutes(1)
    };
    private bool _clearingWebViewCache;

    // Never include AllProfile/AllSite/AllDomStorage: login tokens may also live in DOM storage.
    internal const CoreWebView2BrowsingDataKinds WebViewCacheKinds =
        CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;

    public string WebViewCacheCleanupStatus
    {
        get
        {
            var last = _settings.LastWebViewCacheCleanupUtc is { } time
                ? "上次清理：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "尚未清理缓存";
            return _settings.AutoCleanWebViewCache && _settings.NextWebViewCacheCleanupUtc is { } next
                ? last + "\n下次清理：" + next.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : last;
        }
    }

    public void ApplyWebViewCacheCleanupSettings()
    {
        if (!_closing && _settings.AutoCleanWebViewCache && _browser.CoreWebView2 != null)
            _webViewCacheTimer.Start();
        else _webViewCacheTimer.Stop();
    }

    private async Task TryAutoCleanWebViewCacheAsync()
    {
        // Wait for a pause/idle period rather than clear resources while video is playing or seeking.
        if (_closing || _clearingWebViewCache || !_paused || _seeking
            || !WebViewCacheSchedule.IsDue(_settings, DateTimeOffset.UtcNow)) return;
        await ClearWebViewCacheAsync();
    }

    public async Task<string> ClearWebViewCacheAsync()
    {
        if (_closing || _browser.CoreWebView2 == null) return "浏览器尚未就绪，请稍后重试。";
        if (_clearingWebViewCache) return "正在清理缓存，请稍候。";
        _clearingWebViewCache = true;
        try
        {
            await _browser.CoreWebView2.Profile.ClearBrowsingDataAsync(WebViewCacheKinds);
            if (_closing) return "缓存清理已结束。";
            WebViewCacheSchedule.Completed(_settings, DateTimeOffset.UtcNow);
            _store.Save(_settings);
            return _status.Text = "缓存已清理，登录相关数据已保留。";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException)
        {
            return _status.Text = "缓存清理失败：" + ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _status.Text = "缓存已清理，但清理记录保存失败：" + ex.Message;
        }
        finally { _clearingWebViewCache = false; }
    }
}
