using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using ComboBox = System.Windows.Controls.ComboBox;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class PlaybackRateSpecs
{
    public static void Run(string directory)
    {
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated test directory.");
        var profile = Path.Combine(output, "profile");
        var store = new SettingsStore(profile);
        var original = new AppSettings { CheckUpdatesOnStartup = false, Rate = 1.5, TemporaryRate = 3 };
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
            var settings = Program.Field<AppSettings>(window, "_settings");
            var selector = Program.Field<ComboBox>(window, "_rate");
            string Script(string code) => Wait(core.ExecuteScriptAsync(code));
            void Invoke(string name, params object[] args)
            {
                var method = typeof(MainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(method => method.Name == name && method.GetParameters().Length == args.Length);
                var task = (Task)method.Invoke(window, args)!;
                Until(() => task.IsCompleted); task.GetAwaiter().GetResult();
            }
            bool IsRate(double expected) => double.TryParse(Script("document.querySelector('video')?.playbackRate"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) && Math.Abs(actual - expected) < 0.01;
            void CheckRate(double expected, string label)
            {
                Until(() => IsRate(expected) && selector.SelectedItem is double rate && Math.Abs(rate - expected) < 0.01);
                Program.Pump(700);
                Program.Check(IsRate(expected) && selector.SelectedItem is double rate && Math.Abs(rate - expected) < 0.01, label);
            }
            Program.Invoke(window, "ToggleImmersive");
            Invoke("CommandAsync", "play", null!);
            Until(() => Script("document.querySelector('video').paused") == "false");
            CheckRate(1.5, "isolated immersive playback uses the saved actual speed");
            void Reload(int episode)
            {
                Script($$"""
                    window.originalVideo = document.querySelector('video');
                    originalVideo.addEventListener('loadedmetadata', () => originalVideo.playbackRate = 1, {once:true});
                    originalVideo.addEventListener('playing', () => originalVideo.playbackRate = 1, {once:true});
                    originalVideo.src = 'https://media.guidemate.local/video?episode={{episode}}';
                    originalVideo.load(); originalVideo.play();
                    """);
                Until(() => Script("originalVideo.readyState >= 3 && !originalVideo.paused") == "true");
                Program.Check(Script("originalVideo === document.querySelector('video')") == "true",
                    "episode fixture reuses the real HTML5 video element");
            }
            Reload(2);
            CheckRate(1.5, "source switch and late player resets restore speed without a hotkey");
            Program.Check(settings.Rate == 1.5 && Program.Field<bool>(window, "_immersive"),
                "source switching keeps permanent speed and immersive mode");
            Invoke("StartTemporaryRateAsync"); CheckRate(3, "temporary speed changes actual media playback");
            Invoke("EndTemporaryRateAsync"); CheckRate(1.5, "temporary speed release restores permanent speed");
            Invoke("StartTemporaryRateAsync"); Reload(3);
            CheckRate(1.5, "switching media cancels temporary speed and restores permanent speed");
            Program.Check(!Program.Field<bool>(window, "_temporaryRateActive") && settings.Rate == 1.5,
                "temporary speed does not leak into the next media or saved preference");
            Script("originalVideo.playbackRate = 1.75");
            CheckRate(1.75, "manual website speed changes update actual playback and the app selector");
            Invoke("CommandAsync", "pause", null!); Invoke("CommandAsync", "play", null!);
            CheckRate(1.75, "pause and resume preserve a manual speed change");
            core.Navigate("https://guidemate.local/demo.html?p=4");
            Until(() => core.Source.Contains("demo.html?p=4") && Program.Field<double>(window, "_duration") > 0);
            CheckRate(1.75, "full document navigation reapplies saved speed to a new player");
            Program.Check(Script("document.body.classList.contains('guidemate-video-focus')") == "true",
                "full navigation retains immersive video focus");
            Until(() => Script("document.querySelector('video').readyState >= 3") == "true");
            Script("document.querySelector('video').playbackRate = 2");
            CheckRate(2, "manual website speed changes are respected while new media is paused");
            Script("document.querySelector('video').addEventListener('playing', () => document.querySelector('video').playbackRate = 1, {once:true})");
            Invoke("CommandAsync", "play", null!);
            Until(() => Script("document.querySelector('video').paused") == "false");
            CheckRate(2, "first playback restores the latest selected speed after a delayed reset");
            window.SaveSettings();
            var saved = store.Load();
            Program.Check(saved.Rate == 2 && saved.Hotkeys["SeekForward"] == "Mouse5"
                && saved.Bookmarks.Single().Position == 42, "saving speed preserves custom hotkeys and bookmarks");
            Console.WriteLine("RESULT isolated WebView2 playback rate checks passed; local media, no real website or physical input");
        }
        finally { window?.Close(); Program.Pump(100); app.Shutdown(); }
    }
    private static T Wait<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Playback rate check timed out.");
            Program.Pump(25);
        }
    }
}
