using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using Microsoft.Web.WebView2.Wpf;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

internal static class BrowserCacheSpecs
{
    public static void Run(string directory)
    {
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated test directory.");
        Directory.CreateDirectory(output);
        var profile = Path.Combine(output, "profile");
        var store = new SettingsStore(profile);
        var original = new AppSettings { CheckUpdatesOnStartup = false, Rate = 1.5 };
        original.Hotkeys["SeekForward"] = "Mouse5";
        original.Bookmarks.Add(new() { Url = "https://example.test/saved", Position = 42 });
        store.Save(original);
        var app = new GuideMate.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var startup = typeof(GuideMate.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object), typeof(StartupEventArgs)], null)!;
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using var server = new CacheServer();
        MainWindow? window = null;
        Window? settingsWindow = null;
        try
        {
            window = new MainWindow(profile, true, Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm"));
            window.Show();
            var browser = Program.Field<WebView2CompositionControl>(window, "_browser");
            Until(() => browser.CoreWebView2 != null && browser.CoreWebView2.Source.Contains("local.html"));
            browser.CoreWebView2.Navigate(server.Url);
            Until(() => Wait(browser.CoreWebView2.ExecuteScriptAsync("location.href + '|' + document.readyState"))
                .Contains(server.Url + "|complete"));
            var core = browser.CoreWebView2;
            var cookie = core.CookieManager.CreateCookie("synthetic_login", "keep-me", "127.0.0.1", "/");
            cookie.IsHttpOnly = true; cookie.Expires = DateTime.Now.AddDays(30);
            core.CookieManager.AddOrUpdateCookie(cookie);
            Wait(core.ExecuteScriptAsync("""
                window.seedDone = false;
                (async () => {
                    localStorage.setItem('login_token', 'keep-local');
                    const db = await new Promise((resolve, reject) => {
                        const r = indexedDB.open('login-data', 1);
                        r.onupgradeneeded = () => r.result.createObjectStore('tokens');
                        r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
                    });
                    await new Promise((resolve, reject) => {
                        const tx = db.transaction('tokens', 'readwrite'); tx.objectStore('tokens').put('keep-db', 'login');
                        tx.oncomplete = resolve; tx.onerror = () => reject(tx.error);
                    }); db.close();
                    await (await caches.open('resource-cache')).put('/cached', new Response('discard-me'));
                    await fetch('/resource', {cache: 'force-cache'}).then(r => r.arrayBuffer());
                    window.seedDone = true;
                })().catch(e => window.seedError = String(e));
                """));
            Until(() => Wait(core.ExecuteScriptAsync("window.seedDone")) == "true");
            Program.Check(Wait(core.ExecuteScriptAsync("localStorage.getItem('login_token')")) == "\"keep-local\"",
                "synthetic LocalStorage login token exists before cleanup");
            Wait(core.ExecuteScriptAsync("window.cachePresent = false; caches.has('resource-cache').then(x => window.cachePresent = x)"));
            Until(() => Wait(core.ExecuteScriptAsync("window.cachePresent")) == "true");
            Program.Check(true, "CacheStorage resource exists before cleanup");
            Program.Check(server.ResourceRequests == 1, "HTTP resource was downloaded into browser cache");
            var first = Wait(window.ClearWebViewCacheAsync());
            Program.Check(first.StartsWith("缓存已清理") && store.Load().LastWebViewCacheCleanupUtc != null,
                "manual cleanup succeeds through production WebView2 API and saves completion");
            Program.Check(Wait(core.CookieManager.GetCookiesAsync(server.Url)).Any(c => c.Name == "synthetic_login" && c.Value == "keep-me"),
                "HttpOnly persistent login cookie survives cache cleanup");
            Program.Check(Wait(core.ExecuteScriptAsync("localStorage.getItem('login_token')")) == "\"keep-local\"",
                "LocalStorage login token survives cache cleanup");
            Wait(core.ExecuteScriptAsync("""
                window.inspectDone = false;
                (async () => {
                    window.cacheAfter = await caches.keys();
                    const db = await new Promise(resolve => { const r = indexedDB.open('login-data'); r.onsuccess = () => resolve(r.result); });
                    window.tokenAfter = await new Promise(resolve => { const r = db.transaction('tokens').objectStore('tokens').get('login'); r.onsuccess = () => resolve(r.result); });
                    db.close();
                    await fetch('/resource', {cache: 'force-cache'}).then(r => r.arrayBuffer());
                    window.inspectDone = true;
                })();
                """));
            Until(() => Wait(core.ExecuteScriptAsync("window.inspectDone")) == "true");
            Program.Check(Wait(core.ExecuteScriptAsync("window.cacheAfter.length")) == "0", "CacheStorage resources are removed");
            Program.Check(Wait(core.ExecuteScriptAsync("window.tokenAfter")) == "\"keep-db\"", "IndexedDB login token survives cleanup");
            Program.Check(server.ResourceRequests == 2, "disk cache removal forces resource to download again");

            var settings = Program.Field<AppSettings>(window, "_settings");
            Window OpenSettings()
            {
                var type = typeof(MainWindow).Assembly.GetType("GuideMate.App.SettingsWindow")!;
                var result = (Window)Activator.CreateInstance(type, window, settings,
                    Program.Field<object>(window, "_keys"),
                    new Func<Dictionary<string, string>, int, string?>((_, _) => null))!;
                result.Show(); Program.Pump(80); return result;
            }
            settingsWindow = OpenSettings();
            var auto = Program.Descendants(settingsWindow).OfType<CheckBox>().Single(x => AutomationProperties.GetName(x) == "定时自动清理缓存");
            var interval = Program.Descendants(settingsWindow).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "缓存清理周期");
            auto.IsChecked = true; interval.SelectedIndex = 0;
            var anchor = Program.Descendants(settingsWindow).OfType<Button>().Single(x => Equals(x.Content, "缓存"));
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Program.Pump(100);
            Program.Capture(settingsWindow, Path.Combine(output, "cache-settings.png"));
            settingsWindow.Close(); settingsWindow = null;
            Program.Check(!settings.AutoCleanWebViewCache && !store.Load().AutoCleanWebViewCache,
                "cancel leaves automatic cleanup off and preserves saved preferences");
            settingsWindow = OpenSettings();
            auto = Program.Descendants(settingsWindow).OfType<CheckBox>().Single(x => AutomationProperties.GetName(x) == "定时自动清理缓存");
            interval = Program.Descendants(settingsWindow).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "缓存清理周期");
            auto.IsChecked = true; interval.SelectedIndex = 0;
            var save = Program.Descendants(settingsWindow).OfType<Button>().Single(x => x.ToolTip as string == "保存设置");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); settingsWindow = null;
            Program.Check(store.Load().AutoCleanWebViewCache && store.Load().WebViewCacheCleanupDays == 1
                && Program.Field<DispatcherTimer>(window, "_webViewCacheTimer").IsEnabled, "saving enables daily cleanup and timer");
            settings.NextWebViewCacheCleanupUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            var previous = settings.LastWebViewCacheCleanupUtc;
            typeof(MainWindow).GetField("_paused", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            Wait((Task)Program.Invoke(window, "TryAutoCleanWebViewCacheAsync")!);
            Program.Check(settings.LastWebViewCacheCleanupUtc == previous, "automatic cleanup defers during playback");
            typeof(MainWindow).GetField("_paused", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            Wait((Task)Program.Invoke(window, "TryAutoCleanWebViewCacheAsync")!);
            Program.Check(settings.LastWebViewCacheCleanupUtc > previous && settings.NextWebViewCacheCleanupUtc > DateTimeOffset.UtcNow,
                "overdue automatic cleanup runs when paused and advances next deadline");
            Program.Check(store.Load().Hotkeys["SeekForward"] == "Mouse5" && store.Load().Bookmarks[0].Position == 42,
                "manual and automatic cleanup preserve custom hotkey and bookmark");
            settings.NextWebViewCacheCleanupUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            var beforeRestart = settings.LastWebViewCacheCleanupUtc;
            var timer = Program.Field<DispatcherTimer>(window, "_webViewCacheTimer");
            window.Close(); window = null; Program.Pump(150);
            Program.Check(!timer.IsEnabled, "closing app stops cache cleanup timer");
            window = new MainWindow(profile, true, Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm")); window.Show();
            browser = Program.Field<WebView2CompositionControl>(window, "_browser");
            Until(() => browser.CoreWebView2 != null && browser.CoreWebView2.Source.Contains("local.html"));
            Program.Check(Wait(browser.CoreWebView2.CookieManager.GetCookiesAsync(server.Url)).Any(c => c.Name == "synthetic_login" && c.Value == "keep-me"),
                "synthetic login cookie survives reopening browser profile");
            Program.Check(Program.Field<DispatcherTimer>(window, "_webViewCacheTimer").IsEnabled
                && store.Load().WebViewCacheCleanupDays == 1 && store.Load().LastWebViewCacheCleanupUtc > beforeRestart,
                "saved overdue schedule runs during browser startup and resumes timer");
            var reopened = Program.Field<AppSettings>(window, "_settings");
            WebViewCacheSchedule.Configure(reopened, false, reopened.WebViewCacheCleanupDays, DateTimeOffset.UtcNow);
            window.ApplyWebViewCacheCleanupSettings(); window.SaveSettings();
            Program.Check(!Program.Field<DispatcherTimer>(window, "_webViewCacheTimer").IsEnabled && !store.Load().AutoCleanWebViewCache,
                "disabling automatic cleanup stops timer and persists opt-out");
            Console.WriteLine("RESULT browser cache desktop checks passed");
        }
        finally { settingsWindow?.Close(); window?.Close(); Program.Pump(100); app.Shutdown(); }
    }

    private static T Wait<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("WebView2 cache check timed out."); Program.Pump(20); }
    }

    private sealed class CacheServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private int _resourceRequests;
        public int ResourceRequests => Volatile.Read(ref _resourceRequests);
        public string Url { get; }
        public CacheServer()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url); _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (_listener.IsListening)
                    {
                        var context = await _listener.GetContextAsync();
                        var resource = context.Request.Url!.AbsolutePath == "/resource";
                        if (resource) Interlocked.Increment(ref _resourceRequests);
                        var bytes = resource ? new byte[1024 * 1024] : Encoding.UTF8.GetBytes("<!doctype html><title>Isolated cache test</title>");
                        context.Response.ContentType = resource ? "application/octet-stream" : "text/html";
                        context.Response.Headers["Cache-Control"] = resource ? "public, max-age=3600" : "no-store";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
                    }
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
            });
        }
        public void Dispose() => _listener.Close();
    }
}
