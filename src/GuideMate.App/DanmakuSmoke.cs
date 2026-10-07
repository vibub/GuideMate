using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private readonly bool _danmakuProbe;
    private readonly string? _danmakuLiveUrl;

    private async Task RunDanmakuProbeAsync()
    {
        var checks = new List<string>();
        async Task Wait(Func<bool> condition, string label, int seconds = 15)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception(label + "; status=" + _status.Text); await Task.Delay(100); }
            checks.Add(label);
        }
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            checks.Add(label);
        }
        try
        {
            if (_danmakuLiveUrl == null)
            {
                _browser.CoreWebView2.SetVirtualHostNameToFolderMapping("danmaku-test.bilibili.com",
                    Path.Combine(AppContext.BaseDirectory, "assets"), CoreWebView2HostResourceAccessKind.DenyCors);
                Navigate("https://danmaku-test.bilibili.com/danmaku-test.html");
            }
            else Navigate(_danmakuLiveUrl);
            await Wait(() => _duration > 20 && _danmakuAvailable, "playable video and Bilibili renderer available", 45);
            await CommandAsync("pause");
            await Wait(() => _paused, "video pauses");
            await CommandAsync("position", _danmakuLiveUrl == null ? 10 : 30);
            await Task.Delay(600);
            if (_danmakuLiveUrl == null) await _browser.CoreWebView2.ExecuteScriptAsync("fixtureComments()");
            await Wait(() => _danmakuSourceEnabled, "site danmaku enabled");
            ToggleImmersive();
            await CommandAsync("play");
            await Wait(() => _danmakuOverlay?.IsVisible == true && _danmakuOverlay.CommentCount > 0,
                _danmakuLiveUrl == null ? "offline render models reach desktop overlay" : "real Bilibili comments reach desktop overlay", 35);
            await CommandAsync("pause"); await Wait(() => _paused, "overlay video paused after capture");
            await Task.Delay(300);
            var overlay = _danmakuOverlay!;
            Check(NativeHotkeys.IsClickThrough(overlay) && !overlay.ShowActivated && !overlay.ShowInTaskbar && overlay.Topmost,
                "overlay is topmost, nonactivating and always click-through");
            // Show did not activate the overlay itself; page play/pause can focus the main window.
            Check(GetDanmakuForegroundWindow() != new WindowInteropHelper(overlay).Handle,
                "overlay never takes foreground focus");
            Check(GetDanmakuWindowRect(new WindowInteropHelper(this).Handle, out var owner), "main native rectangle available");
            var monitor = System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(owner.Left, owner.Top, owner.Right, owner.Bottom)).Bounds;
            Check(GetDanmakuWindowRect(new WindowInteropHelper(overlay).Handle, out var bounds)
                && bounds.Left == monitor.Left && bounds.Top == monitor.Top && bounds.Right == monitor.Right && bounds.Bottom == monitor.Bottom,
                "overlay covers full physical monitor including taskbar area");
            var frozen = overlay.MediaTime;
            await Task.Delay(550);
            Check(Math.Abs(overlay.MediaTime - frozen) < 0.05, "paused comments freeze with video clock");
            if (_danmakuLiveUrl == null) Check(overlay.CommentCount == 4, "repeated source snapshots do not duplicate comments");
            var surface = overlay.Surface;
            var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            image.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(_dataPath, "danmaku-overlay.png"))) encoder.Save(output);
            await using (var output = File.Create(Path.Combine(_dataPath, "danmaku-player.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            var hotkeys = JsonSerializer.Serialize(_settings.Hotkeys);
            _settings.FullscreenDanmaku = false; ApplyDanmakuSettings(); SaveSettings();
            Check(!overlay.IsVisible && !_store.Load().FullscreenDanmaku, "disable persists and immediately hides overlay");
            _settings.FullscreenDanmaku = true; ApplyDanmakuSettings(); SaveSettings();
            Check(_store.Load().FullscreenDanmaku && JsonSerializer.Serialize(_settings.Hotkeys) == hotkeys,
                "enable persists without changing custom hotkeys");
            await Wait(() => overlay.CommentCount > 0, "restore repopulates active comments");
            HideToTray();
            Check(!overlay.IsVisible && overlay.CommentCount == 0, "hide clears desktop comments");
            RestoreMainWindow(); await Wait(() => overlay.IsVisible && overlay.CommentCount > 0, "restore resumes same page and comments");
            WindowState = WindowState.Minimized;
            Check(!overlay.IsVisible, "minimize hides desktop comments");
            RestoreMainWindow(); await Wait(() => overlay.IsVisible, "unminimize restores overlay");
            if (_danmakuLiveUrl == null)
            {
                await _browser.CoreWebView2.ExecuteScriptAsync("player.danmaku.getDanmakuX().visible=false");
                await Wait(() => !overlay.IsVisible && overlay.CommentCount == 0, "native Bilibili switch clears full-screen layer");
                await _browser.CoreWebView2.ExecuteScriptAsync("player.danmaku.getDanmakuX().visible=true");
                await Wait(() => overlay.IsVisible, "native Bilibili switch restores full-screen layer");
            }
            await SetRateAsync(2); await CommandAsync("play");
            await Wait(() => !_paused && _settings.Rate == 2, "double-speed playback synchronized");
            var time = overlay.MediaTime; await Task.Delay(600);
            Check(overlay.MediaTime > time + 0.8, "overlay clock follows double speed");
            await CommandAsync("pause"); await Wait(() => _paused, "pause after rate test");
            await CommandAsync("position", 18);
            await Wait(() => Math.Abs(_position - 18) < 0.3, "seek rebases playback clock");
            ToggleImmersive();
            Check(!overlay.IsVisible && overlay.CommentCount == 0, "exit immersive clears full-screen comments");
            ToggleImmersive(); await Wait(() => overlay.IsVisible, "reenter resumes source");
            Navigate("https://guidemate.local/demo.html");
            Check(!overlay.IsVisible && overlay.CommentCount == 0, "navigation immediately discards previous comments");
            await Wait(() => _duration > 20 && !_danmakuAvailable, "other site has no Bilibili overlay");
            WriteSmokeResult(true, "", checks);
        }
        catch (Exception ex)
        {
            if (_browser.CoreWebView2 != null)
            {
                var probe = await _browser.CoreWebView2.ExecuteScriptAsync("""
                    (() => ({url:location.href, title:document.title, player:typeof player,
                      danmaku:typeof window.player?.danmaku, available:!!window.player?.danmaku?.getDanmakuX?.()?.manager,
                      video:!!document.querySelector('video')}))()
                    """);
                File.WriteAllText(Path.Combine(_dataPath, "danmaku-diagnostic.json"), probe);
            }
            WriteSmokeResult(false, ex.ToString(), checks);
        }
        finally { Close(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DanmakuRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetDanmakuWindowRect(nint handle, out DanmakuRect rect);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetDanmakuForegroundWindow();
}
