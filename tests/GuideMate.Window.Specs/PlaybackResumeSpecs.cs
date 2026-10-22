using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class PlaybackResumeSpecs
{
    public static void Run(string directory)
    {
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated test directory.");
        var profile = Path.Combine(output, "profile");
        var site = Path.Combine(output, "site");
        Directory.CreateDirectory(site);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm"), Path.Combine(site, "demo.webm"));
        File.WriteAllText(Path.Combine(site, "video.html"), """
            <!doctype html><html><body style="margin:0">
            <video src="demo.webm" controls muted preload="auto" style="width:100vw;height:100vh"></video>
            <script>
            window.commands = [];
            const command = window.guideMate.command.bind(window.guideMate);
            window.guideMate.command = request => { commands.push(request); return command(request); };
            const video = document.querySelector('video');
            video.addEventListener('loadedmetadata', () => {
              const delay = Number(new URLSearchParams(location.search).get('delay'));
              const restore = () => { video.currentTime = 3; window.siteRestored = true; };
              if (delay) setTimeout(restore, delay); else restore();
            }, {once:true});
            </script></body></html>
            """);
        File.WriteAllText(Path.Combine(site, "frame.html"), """
            <!doctype html><html><body style="margin:0">
            <iframe src="video.html" style="border:0;width:100vw;height:100vh"></iframe>
            </body></html>
            """);
        var store = new SettingsStore(profile);
        var original = new AppSettings { CheckUpdatesOnStartup = false, Rate = 1.5, Opacity = 0.75 };
        original.Hotkeys["SeekForward"] = "Mouse5";
        original.Bookmarks.Add(new() { Url = "https://example.test/saved", Position = 42 });
        store.Save(original);
        var app = new GuideMate.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var startup = typeof(GuideMate.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(object), typeof(StartupEventArgs)], null)!;
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        MainWindow? window = null;
        try
        {
            window = new MainWindow(profile, true, Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm"));
            window.Show();
            var browser = Program.Field<WebView2CompositionControl>(window, "_browser");
            Until(() => browser.CoreWebView2 != null && Program.Field<double>(window, "_duration") > 0);
            var core = browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("resume.guidemate.local", site, CoreWebView2HostResourceAccessKind.DenyCors);
            var settings = Program.Field<AppSettings>(window, "_settings");
            string Script(string code) => Wait(core.ExecuteScriptAsync(code));
            bool NearTime(double expected, bool frame = false) => double.TryParse(Script(
                (frame ? "document.querySelector('iframe').contentDocument." : "document.") + "querySelector('video').currentTime"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var time) && Math.Abs(time - expected) < 0.1;
            void Load(string path, bool appNavigation)
            {
                var url = "https://resume.guidemate.local/" + path;
                if (appNavigation) Program.Invoke(window, "Navigate", url, null!);
                else core.Navigate(url);
                Until(() => core.Source == url && Program.Field<double>(window, "_duration") > 0
                    && Program.Field<bool>(window, "_resumeApplied"));
                Until(() => Script("window.siteRestored === true") == "true");
                Program.Pump(700);
            }
            Load("video.html?p=1", true);
            Program.Check(NearTime(3), "opening a URL preserves the website's automatic history seek");
            Program.Check(Script("commands.some(c => c.action === 'position')") == "false",
                "ordinary navigation sends no position command, including zero");
            Program.Check(Script("document.querySelector('video').playbackRate") == "1.5",
                "ordinary navigation still restores the configured playback rate");
            Load("video.html?p=2&delay=700", false);
            Program.Check(NearTime(3) && Script("commands.some(c => c.action === 'position')") == "false",
                "in-page navigation leaves a delayed website history restore in control");
            Program.Invoke(window, "Navigate", "https://resume.guidemate.local/video.html?p=3",
                new SavedVideo { Position = 6, Rate = 1.75 });
            Until(() => core.Source.EndsWith("?p=3") && NearTime(6));
            Program.Pump(700);
            Program.Check(NearTime(6) && Script("document.querySelector('video').playbackRate") == "1.75",
                "explicit saved history restores its position and rate");
            Script("document.querySelector('video').currentTime = 5");
            Until(() => Math.Abs(Program.Field<double>(window, "_position") - 5) < 0.1);
            core.Reload();
            Until(() => Script("commands.some(c => c.action === 'position' && c.value === 5)") == "true");
            Program.Pump(700);
            Program.Check(NearTime(5), "refreshing the current video restores its actual known position");
            Program.Invoke(window, "Navigate", "https://resume.guidemate.local/video.html?p=4",
                new SavedVideo { Position = 0, Rate = 1.5 });
            Until(() => core.Source.EndsWith("?p=4") && Script("commands.some(c => c.action === 'position' && c.value === 0)") == "true");
            Program.Pump(700);
            Program.Check(NearTime(0), "an explicitly saved zero position can still restart the video");
            core.Navigate("https://resume.guidemate.local/frame.html");
            Until(() => core.Source.EndsWith("frame.html") && Program.Field<double>(window, "_duration") > 0
                && Script("document.querySelector('iframe')?.contentWindow.siteRestored === true") == "true");
            Program.Pump(700);
            Program.Check(NearTime(3, true)
                && Script("document.querySelector('iframe').contentWindow.commands.some(c => c.action === 'position')") == "false",
                "an iframe player also retains website history without an app seek");
            CheckAppearanceHotkeys(window, store);
            window.SaveSettings();
            var saved = store.Load();
            Program.Check(saved.Hotkeys["SeekForward"] == "Mouse5" && saved.Bookmarks.Single().Position == 42,
                "navigation and appearance hotkeys preserve custom side keys and bookmarks");
            Console.WriteLine("RESULT isolated WebView2 resume and appearance hotkey checks passed; local website fixture, simulated action dispatch");
        }
        finally { window?.Close(); Program.Pump(100); app.Shutdown(); }
    }

    private static void CheckAppearanceHotkeys(MainWindow window, SettingsStore store)
    {
        var settings = Program.Field<AppSettings>(window, "_settings");
        void Hotkey(string action) => Program.Invoke(window, "OnHotkey", action);
        bool OpacityIs(double value) => Math.Abs(settings.GetImmersiveOpacity() - value) < 0.0001;
        Hotkey("ImmersiveTransparencyUp");
        Program.Check(OpacityIs(0.7) && Math.Abs(window.Opacity - 0.75) < 0.0001,
            "transparency up changes only the small-window preference in normal mode");
        Program.Invoke(window, "ToggleImmersive");
        Program.Check(Math.Abs(window.Opacity - 0.7) < 0.0001, "entering immersive applies the hotkey transparency preference");
        Hotkey("ImmersiveTransparencyDown");
        Program.Check(OpacityIs(0.75) && Math.Abs(window.Opacity - 0.75) < 0.0001,
            "transparency down makes the real immersive window more opaque");
        for (var i = 0; i < 20; i++) Hotkey("ImmersiveTransparencyUp");
        Program.Check(OpacityIs(0.2) && Math.Abs(window.Opacity - 0.2) < 0.0001, "small-window transparency is capped at eighty percent");
        for (var i = 0; i < 20; i++) Hotkey("ImmersiveTransparencyDown");
        Program.Check(OpacityIs(1) && Math.Abs(window.Opacity - 1) < 0.0001, "small-window transparency stops at fully opaque");
        Hotkey("SubtitleOverlay"); Program.Pump(80);
        var overlay = Program.Field<Window>(window, "_overlay");
        Program.Check(settings.SubtitleOverlay && overlay.IsVisible && store.Load().SubtitleOverlay,
            "subtitle shortcut shows the immersive overlay and immediately saves its preference");
        Hotkey("SubtitleOverlay");
        Program.Check(!settings.SubtitleOverlay && !overlay.IsVisible && !store.Load().SubtitleOverlay,
            "subtitle shortcut hides the overlay and saves the disabled preference");
        Hotkey("SubtitleOverlay");
        Program.Invoke(window, "HideToTray");
        Program.Check(!overlay.IsVisible && settings.SubtitleOverlay, "hiding still preserves the subtitle shortcut preference");
        Program.Invoke(window, "RestoreMainWindow"); Program.Pump(80);
        Program.Check(overlay.IsVisible, "restoring immersive mode shows the enabled subtitle overlay");
        Program.Invoke(window, "ToggleImmersive");
        Hotkey("SubtitleOverlay"); Hotkey("SubtitleOverlay");
        Program.Check(settings.SubtitleOverlay && !overlay.IsVisible && Math.Abs(window.Opacity - 0.75) < 0.0001,
            "subtitle shortcut in normal mode changes preference without showing the immersive-only overlay");
        Hotkey("ImmersiveTransparencyUp");
        var reloaded = store.Load();
        Program.Check(Math.Abs(reloaded.GetImmersiveOpacity() - 0.95) < 0.0001 && reloaded.Opacity == 0.75
            && reloaded.SubtitleOverlay, "appearance shortcut preferences survive a settings reload independently of normal opacity");
    }

    private static T Wait<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Playback resume check timed out.");
            Program.Pump(25);
        }
    }
}
