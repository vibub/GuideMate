using GuideMate.Core;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    checks++;
}
var srt = Subtitles.Parse("1\r\n00:00:01,000 --> 00:00:03,500\r\n<b>向东北走</b>\r\n\r\n2\r\n00:00:03,000 --> 00:00:05,000\r\n右转\r\n", ".srt");
Check(srt.Count == 2 && srt[0].Text == "向东北走", "SRT markup and CRLF");
Check(Subtitles.At(srt, 1) == "向东北走", "inclusive start");
Check(Subtitles.At(srt, 5) == "", "exclusive end");
Check(Subtitles.At(srt, 3.1) == "向东北走\n右转", "overlapping cues");
Check(Subtitles.At(srt, 2, 1) == "向东北走", "subtitle delay");
Check(Subtitles.At(srt, 1, 2) == "", "positive delay postpones cue");
var vtt = Subtitles.Parse("WEBVTT\n\nNOTE ignored\n00:00.000 --> 00:30.000\nnot a cue\n\nx\n00:01.200 --> 00:02.600 align:start\n<c.green>Turn left &amp; go</c>\n", ".vtt");
Check(vtt.Count == 1 && vtt[0].Start == 1.2 && vtt[0].Text == "Turn left & go", "VTT metadata, settings, entities");
var json = Subtitles.Parse("{\"body\":[{\"from\":1,\"to\":2,\"content\":\"北边\"},{\"from\":3,\"to\":2,\"content\":\"bad\"}]}", ".json");
Check(json.Count == 1, "Bilibili JSON and invalid interval");
Check(Subtitles.Parse("[{\"from\":0,\"to\":2,\"content\":\"east\"}]", ".json").Count == 1, "JSON array");
Check(DirectionAnalyzer.Analyze("向东北走")?.Angle == 45, "compound northeast");
Check(DirectionAnalyzer.Analyze("往西南走")?.Angle == 225, "southwest");
Check(DirectionAnalyzer.Analyze("Go north-east")?.Angle == 45, "English compass");
Check(DirectionAnalyzer.Analyze("先左转然后右转")?.Label == "左转", "first step");
Check(DirectionAnalyzer.Analyze("不要左转，右转")?.Label == "右转", "negated first direction");
Check(DirectionAnalyzer.Analyze("不要往北走") == null, "negated compass");
Check(DirectionAnalyzer.Analyze("Don't turn left") == null, "English negation");
Check(DirectionAnalyzer.Analyze("镜头向右转")?.Category == "视角", "camera distinct from movement");
Check(DirectionAnalyzer.Analyze("捡一下东西") == null, "no false east-west");
Check(DirectionAnalyzer.Analyze("向左")?.Angle == null, "relative is not compass angle");
Check(DirectionAnalyzer.IsCombat("先清怪，再开宝箱"), "combat cue");
Check(!DirectionAnalyzer.IsCombat("不用打怪"), "negated combat");
var temp = Path.Combine(Path.GetTempPath(), "GuideMate-specs-" + Guid.NewGuid());
var store = new SettingsStore(temp);
store.Save(new AppSettings { Rate = 1.5, Bookmarks = [new() { Url = "https://example.com", Position = 12 }] });
var loaded = store.Load();
Check(loaded.Rate == 1.5 && loaded.Bookmarks[0].Position == 12, "settings roundtrip");
Check(loaded.FullscreenDanmaku, "existing profiles enable fullscreen danmaku by default");
loaded.FullscreenDanmaku = false; store.Save(loaded);
Check(!store.Load().FullscreenDanmaku && store.Load().Bookmarks[0].Position == 12 && store.Load().Rate == 1.5,
    "danmaku preference persists without resetting existing video data");
Check(loaded.GetImmersiveOpacity() == loaded.Opacity && !loaded.XRayEnabled && loaded.XRayRadius == 70,
    "new small-window defaults inherit opacity without enabling X-ray");
loaded.Opacity = 0.75; loaded.ImmersiveOpacity = 0.45; loaded.XRayEnabled = true; loaded.XRayRadius = 95;
loaded.Hotkeys["Hide"] = "Ctrl+Shift+H";
store.Save(loaded);
var smallReload = store.Load();
Check(smallReload.GetImmersiveOpacity() == 0.45 && smallReload.Opacity == 0.75
    && smallReload.XRayEnabled && smallReload.XRayRadius == 95, "small-window appearance persists independently of normal opacity");
Check(smallReload.Hotkeys["Hide"] == "Ctrl+Shift+H" && smallReload.Bookmarks[0].Position == 12
    && smallReload.Rate == 1.5, "appearance settings preserve custom hide hotkey and existing library");
store.Save(new AppSettings { ImmersiveOpacity = 0.01, XRayRadius = 999 });
Check(store.Load().GetImmersiveOpacity() == 0.2 && store.Load().XRayRadius == 200, "small-window loaded values clamp to UI bounds");
store.Save(new AppSettings { ImmersiveOpacity = 2, XRayRadius = 1 });
Check(store.Load().GetImmersiveOpacity() == 1 && store.Load().XRayRadius == 20, "small-window upper opacity and lower radius clamp");
Check(loaded.GetTopmostMode() == WindowTopmostMode.Always && loaded.ShouldBeTopmost(false), "default topmost policy retains always-on-top behavior");
foreach (var mode in Enum.GetValues<WindowTopmostMode>())
{
    loaded.TopmostMode = mode;
    Check(loaded.ShouldBeTopmost(false) == (mode == WindowTopmostMode.Always)
        && loaded.ShouldBeTopmost(true) == (mode != WindowTopmostMode.Never), "topmost state matrix for " + mode);
    loaded.Hotkeys["SeekForward"] = "Mouse5";
    store.Save(loaded);
    var modeReload = store.Load();
    Check(modeReload.GetTopmostMode() == mode && modeReload.Hotkeys["SeekForward"] == "Mouse5"
        && modeReload.Rate == 1.5 && modeReload.Bookmarks[0].Position == 12, "topmost policy persists without resetting other settings: " + mode);
}
store.Save(new AppSettings { Topmost = false, TopmostMode = WindowTopmostMode.Always });
Check(store.Load().ShouldBeTopmost(false), "new always policy takes precedence over legacy unchecked value");
store.Save(new AppSettings { Topmost = true, TopmostMode = WindowTopmostMode.Never });
Check(!store.Load().ShouldBeTopmost(true), "new never policy takes precedence over legacy checked value");
store.Save(new AppSettings { Topmost = false, TopmostMode = (WindowTopmostMode)99 });
Check(store.Load().GetTopmostMode() == WindowTopmostMode.Never, "unknown persisted policy falls back to legacy setting");
var calibration = new OnlineVisionProfile(1920, 1080, new(0.1, 0.2, 0.075, 0.13333333333333333), true, 13);
loaded.OnlineVisionCalibration = calibration;
loaded.OnlineVisionProfiles["https://example.com/old?p=1"] = calibration with { NorthAngle = 27 };
store.Save(loaded);
var calibrationReload = store.Load();
Check(calibrationReload.OnlineVisionCalibration == calibration, "persistent online calibration survives reload and takes priority over old page profiles");
Check(calibrationReload.Hotkeys["Hide"] == "Ctrl+Shift+H" && calibrationReload.Bookmarks[0].Position == 12
    && calibrationReload.Rate == 1.5, "online calibration preserves custom keys, library and playback settings");
var legacyCalibration = new AppSettings { OnlineVisionProfiles = new() {
    ["https://example.com/watch?p=1"] = calibration,
    ["https://example.com/watch?p=2"] = calibration with { NorthAngle = 30 } } };
store.Save(legacyCalibration);
Check(store.Load().OnlineVisionCalibration?.NorthAngle == 30, "old page calibration migrates without replacing its saved region");
legacyCalibration.History.Add(new() { Url = "https://example.com/watch?p=1", Position = 37 });
store.Save(legacyCalibration);
var recentCalibration = store.Load();
Check(recentCalibration.OnlineVisionCalibration == calibration && recentCalibration.OnlineVisionProfiles.Count == 2
    && recentCalibration.History[0].Position == 37, "migration prefers recently used selection and keeps old page profiles and history");
recentCalibration.OnlineVisionCalibration = calibration with { Region = new(0.3, 0.3, 0.1, 0.17777777777777778) };
store.Save(recentCalibration);
Check(store.Load().OnlineVisionCalibration == recentCalibration.OnlineVisionCalibration, "manual recalibration replaces persistent region and is retained after restart");
Directory.Delete(temp, true);
Check(ChromeBridge.IsAllowedDomain(".bilibili.com") && ChromeBridge.IsAllowedDomain("passport.bilibili.com"), "Bilibili cookie domain scope");
Check(!ChromeBridge.IsAllowedDomain("bilibili.com.attacker.test") && !ChromeBridge.IsAllowedDomain("evilbilibili.com")
    && !ChromeBridge.IsAllowedDomain("..bilibili.com") && !ChromeBridge.IsAllowedDomain("other.test"), "reject lookalike and unrelated cookie domains");
var transfer = new ChromeTransfer { Type = "import-bilibili", PairingCode = new('A', 32), Cookies = [new() {
    Name = "synthetic", Value = "test-only", Domain = ".bilibili.com", Session = true, Secure = true, HttpOnly = true, SameSite = "lax" }] };
byte[] Payload() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transfer, ChromeBridge.JsonOptions);
Check(ChromeBridge.Parse(Payload()).Cookies[0].HttpOnly, "cookie transfer attributes survive parsing");
bool Rejected(Action operation) { try { operation(); return false; } catch (FormatException) { return true; } }
transfer.Cookies[0].Domain = "other.test";
Check(Rejected(() => ChromeBridge.Parse(Payload())), "cookie transfer rejects other websites");
transfer.Cookies[0].Domain = ".bilibili.com"; transfer.Cookies[0].ExpirationDate = null; transfer.Cookies[0].Session = false;
Check(Rejected(() => ChromeBridge.Parse(Payload())), "persistent cookies require finite expiry");
transfer.Cookies[0].Session = true; transfer.Cookies[0].Name = "bad\r\nname";
Check(Rejected(() => ChromeBridge.Parse(Payload())), "cookie names reject control characters");
transfer.Cookies[0].Name = "synthetic"; transfer.PairingCode = "bad";
Check(Rejected(() => ChromeBridge.Parse(Payload())), "pairing code must be 128-bit hex");
using var framed = new MemoryStream();
await ChromeBridge.WriteAsync(framed, new BridgeReply(true, "test", 1)); framed.Position = 0;
var roundtrip = System.Text.Json.JsonSerializer.Deserialize<BridgeReply>(await ChromeBridge.ReadAsync(framed), ChromeBridge.JsonOptions);
Check(roundtrip is { Success: true, Imported: 1 }, "native messaging length framing roundtrip");
using var oversized = new MemoryStream([1, 0, 16, 0]);
var sizeRejected = false; try { await ChromeBridge.ReadAsync(oversized); } catch (FormatException) { sizeRejected = true; }
Check(sizeRejected, "reject oversized native messages before allocation");
var oldDir = Path.Combine(Path.GetTempPath(), "GuideMate-migration-" + Guid.NewGuid());
Directory.CreateDirectory(oldDir);
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"Hotkeys\":{\"PlayPause\":\"Ctrl+Alt+Shift+K\"},\"Rate\":1.5}");
var migrated = new SettingsStore(oldDir).Load();
Check(migrated.Hotkeys["PlayPause"] == "Ctrl+Alt+Shift+K" && migrated.Hotkeys.ContainsKey("TemporaryRate")
    && migrated.Hotkeys.ContainsKey("NextEpisode") && migrated.TemporaryRate == 2, "old settings preserve custom keys and gain new defaults");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"Opacity\":0.6,\"Hotkeys\":{\"Hide\":\"Ctrl+Shift+H\"}}");
var oldAppearance = new SettingsStore(oldDir).Load();
Check(oldAppearance.ImmersiveOpacity == null && oldAppearance.GetImmersiveOpacity() == 0.6
    && oldAppearance.Hotkeys["Hide"] == "Ctrl+Shift+H", "legacy opacity and hide binding migrate without a forced new value");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"Hotkeys\":{\"PlayPause\":\"Ctrl+Alt+Shift+V\"}}");
var collision = new SettingsStore(oldDir).Load();
Check(collision.Hotkeys["PlayPause"] == "Ctrl+Alt+Shift+V" && collision.Hotkeys["TemporaryRate"] != "Ctrl+Alt+Shift+V",
    "new hotkey defaults do not replace colliding existing user bindings");
foreach (var legacy in new[] { false, true })
{
    File.WriteAllText(Path.Combine(oldDir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { Topmost = legacy }));
    var oldTopmost = new SettingsStore(oldDir).Load();
    Check(oldTopmost.GetTopmostMode() == (legacy ? WindowTopmostMode.Always : WindowTopmostMode.Never)
        && oldTopmost.ShouldBeTopmost(false) == legacy && oldTopmost.ShouldBeTopmost(true) == legacy,
        "legacy topmost checkbox value is preserved: " + legacy);
}
Directory.Delete(oldDir, true);
var visual = new VisualDirectionTrack { SourcePath = "synthetic.mp4", Frames = [new(0, 350, 0.9), new(0.5, null, 0), new(1, 10, 0.8)] };
Check(visual.IsValid && visual.At(0.2)?.Angle == 350, "visual timeline follows current time");
Check(visual.At(0.7) == null, "unknown frame does not keep a preceding direction");
Check(visual.At(1)?.Angle == 10 && visual.At(2) == null, "visual seeking and stale-tail expiry");
Check(visual.At(-1) == null && visual.At(double.NaN) == null, "visual invalid timestamps rejected");
Check(visual.HintAt(0)?.Angle == null && visual.HintAt(0)?.Label.StartsWith("画面角度") == true, "rotating map is not absolute compass north");
visual.NorthLocked = true; visual.NorthAngle = 20;
Check(visual.HintAt(1)?.Angle == 350 && visual.HintAt(1)?.Label.StartsWith("北") == true, "calibrated compass wraps at north");
visual.Frames.Add(new(1, 20, 1));
Check(!visual.IsValid, "duplicate direction timestamps rejected at cache boundary");
Check(!new ArrowRegion(double.NaN, 0, 0.1, 0.1).IsValid && !new ArrowRegion(0.9, 0, 0.2, 0.1).IsValid, "invalid and out-of-frame regions rejected");

var tap = new HoldGesture(1000, 400, true);
Check(tap.Advance(1399) == HoldOutcome.None && tap.Release(1399) == HoldOutcome.Tap, "shared binding short press emits tap only on release");
Check(tap.Release(1400) == HoldOutcome.None && tap.Advance(1500) == HoldOutcome.None, "finished gesture cannot fire again");
var hold = new HoldGesture(1000, 400, true);
Check(hold.Advance(1400) == HoldOutcome.Start && hold.Advance(1800) == HoldOutcome.None, "hold starts once at exact threshold");
Check(hold.Release(1900) == HoldOutcome.Stop && hold.Release(2000) == HoldOutcome.None, "hold release stops without emitting tap");
var holdOnly = new HoldGesture(0, 400, false);
Check(holdOnly.Release(200) == HoldOutcome.None, "hold-only short press does nothing");
var canceledTap = new HoldGesture(0, 400, true);
Check(canceledTap.Cancel() == HoldOutcome.None && canceledTap.Release(100) == HoldOutcome.None, "cancellation does not replay tap");
var canceledHold = new HoldGesture(0, 400, true);
canceledHold.Advance(400);
Check(canceledHold.Cancel() == HoldOutcome.Stop && canceledHold.Cancel() == HoldOutcome.None, "cancellation restores active hold exactly once");
var lateRelease = new HoldGesture(0, 400, true);
Check(lateRelease.Release(500) == HoldOutcome.None, "late release without timer activation never becomes a short press");
var customHold = new HoldGesture(500, 800, true);
Check(customHold.Advance(1299) == HoldOutcome.None && customHold.Advance(1300) == HoldOutcome.Start, "custom hold threshold is honored");

var profileTest = Path.Combine(Path.GetTempPath(), "GuideMate-profile-specs-" + Guid.NewGuid());
var defaultProfile = Path.Combine(profileTest, "default");
var selectedProfile = Path.Combine(profileTest, "existing-user");
Directory.CreateDirectory(selectedProfile);
var profileStore = new SettingsStore(selectedProfile);
var customSettings = new AppSettings { TemporaryHoldMilliseconds = 600 };
customSettings.Hotkeys["SeekForward"] = "Mouse5"; customSettings.Hotkeys["TemporaryRate"] = "Mouse5";
profileStore.Save(customSettings);
var settingsBeforeSelection = File.ReadAllText(Path.Combine(selectedProfile, "settings.json"));
Check(DataDirectory.Resolve(defaultProfile, selectedProfile, false) == selectedProfile
    && DataDirectory.Resolve(defaultProfile, null, false) == selectedProfile, "normal startup remembers selected data folder across executable updates");
Check(File.ReadAllText(Path.Combine(selectedProfile, "settings.json")) == settingsBeforeSelection, "profile selection never rewrites user settings");
var profileSelection = File.ReadAllText(Path.Combine(defaultProfile, "active-profile.json"));
var smokeProfile = Path.Combine(profileTest, "smoke");
Check(DataDirectory.Resolve(defaultProfile, smokeProfile, true) == smokeProfile
    && File.ReadAllText(Path.Combine(defaultProfile, "active-profile.json")) == profileSelection, "explicit smoke profile never replaces normal profile selection");
Check(DataDirectory.Resolve(defaultProfile, null, true) != selectedProfile
    && File.ReadAllText(Path.Combine(defaultProfile, "active-profile.json")) == profileSelection, "smoke without data flag remains isolated from user data");
var profileReload = profileStore.Load();
Check(profileReload.Hotkeys["SeekForward"] == "Mouse5" && profileReload.Hotkeys["TemporaryRate"] == "Mouse5"
    && profileReload.TemporaryHoldMilliseconds == 600, "shared bindings and custom hold threshold survive settings reload");
Directory.Delete(selectedProfile, true);
var missingProfileRejected = false;
try { DataDirectory.Resolve(defaultProfile, null, false); } catch (DirectoryNotFoundException) { missingProfileRejected = true; }
Check(missingProfileRejected && !Directory.Exists(selectedProfile), "missing selected profile reports error instead of creating empty user data");
File.WriteAllText(Path.Combine(defaultProfile, "active-profile.json"), "{\"Directory\":\"\"}");
Check(Rejected(() => DataDirectory.Resolve(defaultProfile, null, false)), "invalid profile selection is not silently reset");
Directory.Delete(profileTest, true);
Console.WriteLine($"{checks} checks passed.");
