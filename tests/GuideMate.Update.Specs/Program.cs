using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuideMate.Core;

internal static class Program
{
    private static int _checks;
    private static readonly string[] Required = ["GuideMate.exe", "GuideMate.dll", "GuideMate.deps.json", "GuideMate.runtimeconfig.json", "assets/bridge.js"];
    [STAThread]
    private static void Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "GuideMate-update-specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (args.Length == 2 && args[0] == "--preview") { WindowChecks(root, args[1]); return; }
            if (args.SequenceEqual(new[] { "--live" })) { LiveCheckAsync(root).GetAwaiter().GetResult(); return; }
            if (args.Length == 3 && args[0] == "--package") { PublishedPackageChecks(root, args[1], args[2]); return; }
            VersionChecks(); SettingsChecks(root); NetworkChecksAsync(root).GetAwaiter().GetResult(); PackageChecks(root); WindowChecks(root); CleanupChecks.Run(root);
            Console.WriteLine($"Update checks passed: {_checks}");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void PublishedPackageChecks(string root, string zip, string tag)
    {
        var stage = Path.Combine(root, "published");
        var manifest = UpdatePackage.Extract(zip, stage, tag);
        var install = Path.Combine(root, "old-install"); Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "GuideMate.exe"), "old program");
        File.WriteAllText(Path.Combine(install, "settings.json"), "private settings");
        var cookies = Path.Combine(install, "WebView2", "Default", "Cookies");
        Directory.CreateDirectory(Path.GetDirectoryName(cookies)!); File.WriteAllText(cookies, "synthetic cookies");
        UpdatePackage.Install(stage, install, Path.Combine(root, "backup"), install);
        Check(File.Exists(Path.Combine(install, "updater", "GuideMate.Updater.exe")), "published updater installs");
        Check(File.ReadAllText(Path.Combine(install, "settings.json")) == "private settings" && File.ReadAllText(cookies) == "synthetic cookies", "published installation preserves colocated user data");
        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(install, "GuideMate.dll")).ProductVersion!.Split('+')[0];
        Check(ReleaseVersion.Parse(version).CompareTo(ReleaseVersion.Parse(tag)) == 0, "binary version matches Release tag");
        Console.WriteLine($"Published package verified: {tag}, {manifest.Files.Length} program files, updater and user data preserved.");
    }

    private static async Task LiveCheckAsync(string root)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuideMate-Updater/1.0");
        var client = new GitHubReleaseClient(http);
        var release = await client.GetAsync(null) ?? throw new IOException("No public stable Release");
        var zip = Path.Combine(root, "real-release.zip");
        await client.DownloadAsync(release, zip, null, default);
        var manifest = UpdatePackage.Extract(zip, Path.Combine(root, "real-release"), release.Tag);
        Console.WriteLine($"Live Release verified: {release.Tag}, {release.Package.Size} bytes, {manifest.Files.Length} files; no installation performed.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _checks++;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is FormatException or IOException) { Check(true, message); return; }
        throw new Exception(message);
    }

    private static void VersionChecks()
    {
        Check(ReleaseVersion.Parse("v1.10.0").CompareTo(ReleaseVersion.Parse("1.9.9")) > 0, "numeric version ordering");
        Check(ReleaseVersion.Parse("v1.0.2").CompareTo(ReleaseVersion.Parse("1.0.2+commit")) == 0, "build metadata ignored");
        Check(ReleaseVersion.Parse("1.0.2").CompareTo(ReleaseVersion.Parse("1.0.2-rc.10")) > 0, "stable follows prerelease");
        Check(ReleaseVersion.Parse("1.0.2-rc.10").CompareTo(ReleaseVersion.Parse("1.0.2-rc.2")) > 0, "numeric prerelease identifiers");
        Reject(() => ReleaseVersion.Parse("latest"), "invalid tag rejected");
    }

    private static void SettingsChecks(string root)
    {
        var store = new SettingsStore(Path.Combine(root, "profile"));
        Directory.CreateDirectory(Path.Combine(root, "profile"));
        File.WriteAllText(Path.Combine(root, "profile", "settings.json"), "{\"Hotkeys\":{\"Hide\":\"Mouse4\"},\"Bookmarks\":[{\"Url\":\"https://example.test\",\"Position\":42}],\"ImmersiveOpacity\":0.6}");
        var settings = store.Load();
        Check(settings.CheckUpdatesOnStartup, "legacy profiles default startup checking on");
        settings.CheckUpdatesOnStartup = false; store.Save(settings);
        var reloaded = store.Load();
        Check(!reloaded.CheckUpdatesOnStartup && reloaded.Hotkeys["Hide"] == "Mouse4" && reloaded.Bookmarks[0].Position == 42
            && reloaded.ImmersiveOpacity == 0.6, "opt-out persists without resetting user data");
    }

    private static async Task NetworkChecksAsync(string root)
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic release zip");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var asset = new ReleaseAsset("GuideMate-v1.2.0-win-x64.zip", "https://github.com/vibub/GuideMate/releases/download/v1.2.0/GuideMate-v1.2.0-win-x64.zip", bytes.Length, "sha256:" + hash);
        var release = new GitHubRelease("v1.2.0", "随引", "notes", false, false, [asset]);
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.Host == "api.github.com")
            {
                Check(request.Headers.UserAgent.Count > 0 && request.Headers.Contains("X-GitHub-Api-Version"), "GitHub API headers");
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var client = new GitHubReleaseClient(http);
        Check((await client.GetAsync(null))?.Tag == "v1.2.0", "latest Release parsed");
        Check((await client.GetAsync("v1.2.0"))?.Package.Name == asset.Name, "specified Release parsed");
        Check(requests[0].EndsWith("/releases/latest") && requests[1].EndsWith("/releases/tags/v1.2.0"), "latest and tag endpoints");
        var download = Path.Combine(root, "download.zip");
        await client.DownloadAsync(release, download, null, default);
        Check(File.ReadAllBytes(download).SequenceEqual(bytes), "download with GitHub digest verifies");
        async Task RejectDownload(GitHubRelease invalid, string name)
        {
            try { await client.DownloadAsync(invalid, Path.Combine(root, name), null, default); }
            catch (Exception ex) when (ex is IOException or FormatException) { Check(true, name); return; }
            throw new Exception(name);
        }
        await RejectDownload(release with { Assets = [asset with { Digest = "sha256:" + new string('0', 64) }] }, "bad-digest.zip");
        await RejectDownload(release with { Assets = [asset with { Size = bytes.Length + 1 }] }, "truncated.zip");
        await RejectDownload(release with { Assets = [asset with { DownloadUrl = "https://example.test/install.zip" }] }, "foreign-source.zip");
        await RejectDownload(release with { Assets = [asset with { Digest = null }] }, "missing-checksum.zip");
        var checksumAsset = new ReleaseAsset(asset.Name + ".sha256", asset.DownloadUrl + ".sha256", 100, null);
        using var fallbackHttp = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? new StringContent(hash + "  " + asset.Name + "\n") : new ByteArrayContent(bytes) }));
        await new GitHubReleaseClient(fallbackHttp).DownloadAsync(release with { Assets = [asset with { Digest = null }, checksumAsset] }, Path.Combine(root, "fallback.zip"), null, default);
        Check(File.Exists(Path.Combine(root, "fallback.zip")), "legacy SHA256 sidecar verifies");
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden })
        {
            using var failed = new HttpClient(new Handler(_ => new(status)));
            if (status == HttpStatusCode.NotFound) Check(await new GitHubReleaseClient(failed).GetAsync(null) == null, "no Release handled");
            else
            {
                try { await new GitHubReleaseClient(failed).GetAsync(null); throw new Exception("rate limit ignored"); }
                catch (HttpRequestException) { Check(true, "rate limit reported"); }
            }
        }
        using var prereleaseHttp = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release with { Prerelease = true })) }));
        try { await new GitHubReleaseClient(prereleaseHttp).GetAsync(null); throw new Exception("automatic prerelease accepted"); }
        catch (FormatException) { Check(true, "automatic prerelease rejected"); }
        using var draftHttp = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release with { Draft = true })) }));
        try { await new GitHubReleaseClient(draftHttp).GetAsync(null); throw new Exception("draft accepted"); }
        catch (FormatException) { Check(true, "draft rejected"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await client.DownloadAsync(release, Path.Combine(root, "canceled.zip"), null, canceled.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "download cancellation honored"); }
    }

    private static void PackageChecks(string root)
    {
        string Zip(string name, string[] paths, bool manifest = true, string version = "v1.2.0")
        {
            var path = Path.Combine(root, name + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in paths) { using var writer = new StreamWriter(archive.CreateEntry(file).Open()); writer.Write("new:" + file); }
            if (manifest) { using var writer = new StreamWriter(archive.CreateEntry(UpdatePackage.ManifestName).Open()); writer.Write(JsonSerializer.Serialize(new UpdateManifest(version, paths))); }
            return path;
        }
        var files = Required.Append("updater/GuideMate.Updater.exe").ToArray();
        var zip = Zip("valid", files);
        var stage = Path.Combine(root, "stage");
        Check(UpdatePackage.Extract(zip, stage, "v1.2.0").Files.Length == files.Length, "manifest package extracts");
        var legacyStage = Path.Combine(root, "legacy");
        UpdatePackage.Extract(Zip("legacy", files, false), legacyStage, "v1.0.1");
        Check(File.Exists(Path.Combine(legacyStage, UpdatePackage.ManifestName)), "old published ZIP supported");
        foreach (var path in new[] { "../escape.dll", "assets/../../escape.dll", "assets\\escape.dll", "assets/CON.txt", "assets/evil. ",
            "settings.json", "WebView2/account", "docs/WebView2/account", "vision/cache.json", "active-profile.json" })
            Reject(() => UpdatePackage.Extract(Zip("bad-" + _checks, files.Append(path).ToArray()), Path.Combine(root, "bad-stage-" + _checks), "v1.2.0"), "unsafe ZIP path rejected: " + path);
        Reject(() => UpdatePackage.Extract(Zip("duplicate", files.Append("guidemate.EXE").ToArray()), Path.Combine(root, "duplicate"), "v1.2.0"), "case collision rejected");
        Reject(() => UpdatePackage.Extract(Zip("wrong-version", files, version: "v9.0.0"), Path.Combine(root, "wrong-version"), "v1.2.0"), "manifest version mismatch rejected");
        Reject(() => UpdatePackage.Extract(Zip("incomplete", ["GuideMate.exe"]), Path.Combine(root, "incomplete"), "v1.2.0"), "incomplete ZIP rejected");
        var install = Path.Combine(root, "install"); Directory.CreateDirectory(install);
        foreach (var file in Required.Append("obsolete.dll"))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(install, file))!); File.WriteAllText(Path.Combine(install, file), "old:" + file);
        }
        File.WriteAllText(Path.Combine(install, UpdatePackage.ManifestName), JsonSerializer.Serialize(new UpdateManifest("v1.1.0", Required.Append("obsolete.dll").ToArray())));
        var userFiles = new[] { "settings.json", "WebView2/Default/Cookies", "vision/result.json", "custom-notes.txt", "profile/settings.json" };
        foreach (var file in userFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(install, file))!); File.WriteAllText(Path.Combine(install, file), "private:" + file);
        }
        var rollbackStage = Path.Combine(root, "rollback-stage");
        UpdatePackage.Extract(Zip("rollback", ["GuideMate.exe", "new.dll", .. Required.Skip(1)]), rollbackStage, "v1.2.0");
        using (var locked = new FileStream(Path.Combine(install, "GuideMate.runtimeconfig.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => UpdatePackage.Install(rollbackStage, install, Path.Combine(root, "rollback-backup"), Path.Combine(install, "profile")), "locked file stops update");
        Check(Required.All(file => File.ReadAllText(Path.Combine(install, file)) == "old:" + file) && !File.Exists(Path.Combine(install, "new.dll")), "failed update restores old files and removes new ones");
        UpdatePackage.Install(stage, install, Path.Combine(root, "backup"), Path.Combine(install, "profile"));
        Check(files.All(file => File.ReadAllText(Path.Combine(install, file)) == "new:" + file), "full program updated including updater");
        Check(!File.Exists(Path.Combine(install, "obsolete.dll")), "only obsolete tracked program files removed");
        Check(userFiles.All(file => File.ReadAllText(Path.Combine(install, file)) == "private:" + file), "settings browser cookies visual cache and unrelated files preserved byte for byte");
        var overlapping = Path.Combine(root, "overlap-stage");
        UpdatePackage.Extract(Zip("overlap", files.Append("docs/profile/note.txt").ToArray()), overlapping, "v1.2.0");
        Reject(() => UpdatePackage.Install(overlapping, install, Path.Combine(root, "overlap-backup"), Path.Combine(install, "docs/profile")), "nested active data directory protected");
    }

    private static void WindowChecks(string root, string? screenshotDirectory = null)
    {
        // Construct controls on STA without showing desktop windows, running app Startup, or opening WebView2.
        var app = new GuideMate.App.App(); app.InitializeComponent();
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app,
            typeof(GuideMate.App.App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(object), typeof(StartupEventArgs)], null)!);
        var before = System.Diagnostics.Process.GetProcessesByName("GuideMate").Select(process => { using (process) return process.Id; }).ToHashSet();
        var type = Assembly.Load("GuideMate.Updater").GetType("GuideMate.Updater.UpdaterWindow")!;
        var window = (Window)Activator.CreateInstance(type, new Dictionary<string, string>(), root)!;
        type.GetMethod("ShowCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["v1.2.0", root]);
        var buttons = Descendants(window).OfType<Button>().ToArray();
        Check(buttons.Length == 2 && buttons[0].Content?.ToString() == "启动随引" && buttons[1].Content?.ToString() == "退出", "completion offers launch and exit");
        var after = System.Diagnostics.Process.GetProcessesByName("GuideMate").Select(process => { using (process) return process.Id; }).ToHashSet();
        Check(!window.IsVisible && before.SetEquals(after), "completion does not launch main app automatically");
        UpdateDialogChecks.Run(screenshotDirectory);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var next in Descendants(child)) yield return next; }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
}
