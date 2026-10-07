using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyBilibiliEpisodesAsync(List<string> checks)
    {
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
        }
        async Task Wait(Func<bool> condition, string description)
        {
            var end = DateTime.UtcNow.AddSeconds(15);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception(description); await Task.Delay(100); }
            checks.Add(description);
        }
        bool AtPage(int page) => System.Web.HttpUtility.ParseQueryString(new Uri(_url).Query)["p"] == page.ToString();
        // This virtual host is registered only by the isolated smoke test, never normal startup.
        _browser.CoreWebView2.SetVirtualHostNameToFolderMapping("episodes-test.bilibili.com",
            Path.Combine(AppContext.BaseDirectory, "assets"), CoreWebView2HostResourceAccessKind.DenyCors);
        const string fixture = "https://episodes-test.bilibili.com/bilibili-episodes-test.html";
        foreach (var layout in new[] { "modern", "legacy", "multi" })
        {
            Navigate(fixture + "?layout=" + layout + "&p=2" + (layout == "modern" ? "&source=keep&t=99" : ""));
            await Wait(() => _url.Contains("layout=" + layout) && AtPage(2) && _duration > 35,
                "Bilibili episode fixture loads: " + layout);
            await CommandAsync("position", 10);
            LoadSubtitles(Path.Combine(AppContext.BaseDirectory, "assets", "demo.srt"));
            await CommandAsync("nextEpisode");
            await Wait(() => AtPage(3) && _duration > 35 && _position < 1 && _cues.Count == 0,
                "Bilibili next episode advances selection and clears old time/subtitles: " + layout);
            if (layout == "modern")
            {
                var parameters = System.Web.HttpUtility.ParseQueryString(new Uri(_url).Query);
                Check(parameters["source"] == "keep" && parameters["t"] == null && new Uri(_url).Fragment.Length == 0,
                    "Bilibili multipart URL preserves source parameters, removes stale time and ignores embedded unrelated links");
            }
            await CommandAsync("nextEpisode");
            Check(AtPage(3) && !new Uri(_url).Fragment.Contains("unrelated") && _notice.Contains("上一集"),
                "Bilibili last episode does not wrap or click an unrelated next link: " + layout);
            OnHotkey("PreviousEpisode");
            await Wait(() => AtPage(2) && _duration > 35, "Bilibili previous episode hotkey route moves back: " + layout);
            await CommandAsync("previousEpisode");
            await Wait(() => AtPage(1) && _duration > 35, "Bilibili previous episode reaches first part: " + layout);
            await CommandAsync("previousEpisode");
            Check(AtPage(1) && _notice.Contains("上一集"), "Bilibili first episode stays at its boundary: " + layout);
        }
        Navigate(fixture + "?layout=modern&hidden=1&p=2");
        await Wait(() => _url.Contains("hidden=1") && AtPage(2) && _duration > 35, "collapsed Bilibili episode fixture loads");
        Check(await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.video-pod__item').getBoundingClientRect().height === 0") == "true",
            "collapsed episode list has no layout height, reproducing the old visibility filter failure");
        OnHotkey("NextEpisode");
        await Wait(() => AtPage(3) && _duration > 35, "Bilibili next hotkey route works with a collapsed episode list");
        await CommandAsync("previousEpisode");
        await Wait(() => AtPage(2) && _duration > 35, "Bilibili previous episode works with a collapsed episode list");
        Navigate(fixture + "?layout=modern&p=2");
        await Wait(() => !_url.Contains("hidden=1") && AtPage(2) && _duration > 35, "Bilibili fixture ready for immersive switching");
        try
        {
            ToggleImmersive(); await Task.Delay(250);
            Check(await _browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('.video-pod__item')).visibility === 'hidden'") == "true",
                "immersive focus actually hides Bilibili episode list");
            await CommandAsync("nextEpisode");
            await Wait(() => AtPage(3) && _duration > 35 && _immersive, "Bilibili next episode works while immersed");
            Check(await _browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('.video-pod__item')).visibility === 'hidden'") == "true",
                "Bilibili multipart navigation reapplies immersive video focus in the new document");
            await CommandAsync("previousEpisode");
            await Wait(() => AtPage(2) && _duration > 35 && _immersive, "Bilibili previous episode works while immersed");
        }
        finally { if (_immersive) ToggleImmersive(); }
        Navigate(fixture + "?layout=modern&disabled=1&p=1");
        await Wait(() => _url.Contains("disabled=1") && AtPage(1) && _duration > 35, "disabled Bilibili episode fixture loads");
        await CommandAsync("nextEpisode");
        Check(AtPage(1) && _notice.Contains("上一集"), "disabled Bilibili next episode is not clicked");
    }
}
