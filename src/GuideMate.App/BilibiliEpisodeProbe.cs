using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task RunBilibiliEpisodeProbeAsync()
    {
        var checks = new List<string>();
        var states = new List<JsonElement>();
        var error = "";
        var success = false;
        async Task<JsonElement> WaitForPart(Func<JsonElement, bool> condition, string description)
        {
            var end = DateTime.UtcNow.AddSeconds(40);
            while (true)
            {
                var json = await _browser.CoreWebView2.ExecuteScriptAsync("""
                    (() => {
                        const items = [...document.querySelectorAll('.video-pod__list .video-pod__item')];
                        const current = items.findIndex(item => item.matches('.active,.on,.playing,[aria-current="page"]'));
                        return { href:location.href, page:Number(new URL(location.href).searchParams.get('p') || 1),
                            count:items.length, current, cid:items[current]?.getAttribute('data-key') || '',
                            title:items[current]?.textContent.trim() || '',
                            hydrated:!!document.querySelector('#app') && !document.querySelector('#app[data-server-rendered]'),
                            hidden:items.length > 0 && getComputedStyle(items[0]).visibility === 'hidden' };
                    })()
                    """);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && condition(doc.RootElement))
                {
                    checks.Add(description);
                    var state = doc.RootElement.Clone(); states.Add(state); return state;
                }
                if (DateTime.UtcNow >= end) throw new Exception(description + ": last DOM state " + json + "; browser status: " + _status.Text);
                await Task.Delay(200);
            }
        }
        try
        {
            var initial = await WaitForPart(state => state.GetProperty("count").GetInt32() > 2
                && state.GetProperty("current").GetInt32() > 0 && state.GetProperty("hydrated").GetBoolean() && _duration > 0,
                "live Bilibili multipart DOM has hydrated and playable video loaded without importing account data");
            await using (var preview = File.Create(Path.Combine(_dataPath, "bilibili-episode-initial.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, preview);
            var current = initial.GetProperty("current").GetInt32();
            if (current >= initial.GetProperty("count").GetInt32() - 1) throw new Exception("Choose a middle part for the live episode probe");
            foreach (var immersive in new[] { false, true })
            {
                if (immersive)
                {
                    ToggleImmersive();
                    await WaitForPart(state => state.GetProperty("hidden").GetBoolean(), "live immersive focus hides the real episode list");
                }
                await CommandAsync("nextEpisode");
                await WaitForPart(state => state.GetProperty("current").GetInt32() == current + 1
                    && state.GetProperty("page").GetInt32() == current + 2 && state.GetProperty("hydrated").GetBoolean()
                    && _duration > 0 && (!immersive || state.GetProperty("hidden").GetBoolean()),
                    "live Bilibili next part changes real selection and page URL, immersive=" + immersive);
                await CommandAsync("previousEpisode");
                await WaitForPart(state => state.GetProperty("current").GetInt32() == current
                    && state.GetProperty("page").GetInt32() == current + 1 && state.GetProperty("hydrated").GetBoolean()
                    && _duration > 0 && (!immersive || state.GetProperty("hidden").GetBoolean()),
                    "live Bilibili previous part changes real selection and page URL, immersive=" + immersive);
            }
            await using (var preview = File.Create(Path.Combine(_dataPath, "bilibili-episode-preview.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, preview);
            success = true;
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally { if (_immersive) ToggleImmersive(); }
        await File.WriteAllTextAsync(Path.Combine(_dataPath, "bilibili-episode-states.json"),
            JsonSerializer.Serialize(states, new JsonSerializerOptions { WriteIndented = true }));
        WriteSmokeResult(success, error, checks);
        Close();
    }
}
