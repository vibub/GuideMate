using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task RunProfileProbeAsync()
    {
        const string name = "GuideMate_ProfileProbe";
        const string value = "synthetic-persistence-only";
        var checks = new List<string>(); var error = "";
        try
        {
            var end = DateTime.UtcNow.AddSeconds(20);
            while (_duration < 20) { if (DateTime.UtcNow > end) throw new Exception("Profile probe video did not load"); await Task.Delay(100); }
            var core = _browser.CoreWebView2;
            var expectedFolder = Path.GetFullPath(Path.Combine(_dataPath, "WebView2"));
            if (!string.Equals(Path.GetFullPath(core.Environment.UserDataFolder), expectedFolder, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Unexpected browser user data folder");
            checks.Add("actual WebView2 environment uses selected data directory");
            if (_profileProbe == "write")
            {
                _settings.Hotkeys["SeekForward"] = "Mouse5"; _settings.Hotkeys["TemporaryRate"] = "Mouse5";
                _settings.TemporaryHoldMilliseconds = 650;
                var result = _keys!.Apply(_settings.Hotkeys, 650);
                if (result != null) throw new Exception(result);
                SaveSettings();
                checks.Add("shared bindings and hold threshold saved in isolated profile");
                var cookie = core.CookieManager.CreateCookie(name, value, "guidemate.local", "/");
                cookie.Expires = DateTime.UtcNow.AddDays(1); cookie.IsSecure = true;
                core.CookieManager.AddOrUpdateCookie(cookie);
                await core.ExecuteScriptAsync("localStorage.setItem('GuideMate_ProfileProbe', 'synthetic-persistence-only')");
                if ((await core.CookieManager.GetCookiesAsync("https://guidemate.local/")).All(c => c.Name != name || c.Value != value))
                    throw new Exception("Synthetic probe cookie was not stored");
                checks.Add("synthetic persistent cookie and local storage written without real login data");
            }
            else
            {
                if (_settings.Hotkeys["SeekForward"] != "Mouse5" || _settings.Hotkeys["TemporaryRate"] != "Mouse5"
                    || _settings.TemporaryHoldMilliseconds != 650) throw new Exception("Custom hotkeys reset across executables");
                checks.Add("shared hotkeys and threshold preserved across executable locations and restart");
                var cookie = (await core.CookieManager.GetCookiesAsync("https://guidemate.local/")).SingleOrDefault(c => c.Name == name);
                if (cookie == null || cookie.Value != value || cookie.IsSession) throw new Exception("Persistent probe cookie lost on restart");
                checks.Add("synthetic persistent cookie preserved across executable locations and restart");
                if (await core.ExecuteScriptAsync("localStorage.getItem('GuideMate_ProfileProbe')") != "\"synthetic-persistence-only\"")
                    throw new Exception("Local storage lost on restart");
                checks.Add("local storage preserved across executable locations and restart");
                core.CookieManager.DeleteCookie(cookie);
                await core.ExecuteScriptAsync("localStorage.removeItem('GuideMate_ProfileProbe')");
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(_dataPath, "profile-probe-" + _profileProbe + ".json"), JsonSerializer.Serialize(new
            {
                success = error.Length == 0, error, checks, executable = Environment.ProcessPath, directory = _dataPath, time = DateTime.Now
            }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
