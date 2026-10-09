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
Check(loaded.HideImmersiveFromAltTab, "new profiles hide immersive windows from Alt+Tab by default");
loaded.HideImmersiveFromAltTab = false; store.Save(loaded);
Check(!store.Load().HideImmersiveFromAltTab && store.Load().Bookmarks[0].Position == 12 && store.Load().Rate == 1.5,
    "Alt+Tab opt-out persists without resetting video data");
loaded.HideImmersiveFromAltTab = true; store.Save(loaded);
Check(store.Load().HideImmersiveFromAltTab, "Alt+Tab exclusion can be saved again");
loaded.FullscreenDanmaku = false; store.Save(loaded);
Check(!store.Load().FullscreenDanmaku && store.Load().Bookmarks[0].Position == 12 && store.Load().Rate == 1.5,
    "danmaku preference persists without resetting existing video data");
Check(loaded.DanmakuDisplayArea == 1 && loaded.DanmakuOpacity == 1 && loaded.DanmakuFontScale == 1 && loaded.DanmakuSpeed == 1,
    "new danmaku appearance defaults preserve previous full-screen behavior");
loaded.Hotkeys["Hide"] = "Ctrl+Shift+H";
loaded.DanmakuDisplayArea = 0.3; loaded.DanmakuOpacity = 0.5; loaded.DanmakuFontScale = 1.5; loaded.DanmakuSpeed = 1.75;
store.Save(loaded);
var danmakuReload = store.Load();
Check(danmakuReload.DanmakuDisplayArea == 0.3 && danmakuReload.DanmakuOpacity == 0.5
    && danmakuReload.DanmakuFontScale == 1.5 && danmakuReload.DanmakuSpeed == 1.75, "four danmaku appearance fields survive reload");
Check(danmakuReload.Hotkeys["Hide"] == "Ctrl+Shift+H" && danmakuReload.Bookmarks[0].Position == 12
    && danmakuReload.Rate == 1.5 && danmakuReload.Opacity == 1 && !danmakuReload.FullscreenDanmaku,
    "danmaku appearance preserves hotkeys, bookmarks, playback and independent window opacity");
File.WriteAllText(Path.Combine(temp, "settings.json"), "{\"DanmakuDisplayArea\":0,\"DanmakuOpacity\":-1,\"DanmakuFontScale\":0,\"DanmakuSpeed\":0}");
var lowDanmaku = store.Load();
Check(lowDanmaku.DanmakuDisplayArea == 0.1 && lowDanmaku.DanmakuOpacity == 0
    && lowDanmaku.DanmakuFontScale == 0.5 && lowDanmaku.DanmakuSpeed == 0.5, "danmaku appearance lower bounds normalize on load");
File.WriteAllText(Path.Combine(temp, "settings.json"), "{\"DanmakuDisplayArea\":3,\"DanmakuOpacity\":3,\"DanmakuFontScale\":3,\"DanmakuSpeed\":3}");
var highDanmaku = store.Load();
Check(highDanmaku.DanmakuDisplayArea == 1 && highDanmaku.DanmakuOpacity == 1
    && highDanmaku.DanmakuFontScale == 2 && highDanmaku.DanmakuSpeed == 2, "danmaku appearance upper bounds normalize on load");
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
loaded.ImmersiveBounds = new(-420, 260, 800, 450);
loaded.OverlayLeft = -360; loaded.OverlayTop = 510; loaded.OverlayWidth = 1180;
store.Save(loaded);
var windowReload = store.Load();
Check(windowReload.ImmersiveBounds == loaded.ImmersiveBounds && windowReload.OverlayLeft == -360
    && windowReload.OverlayTop == 510 && windowReload.OverlayWidth == 1180, "immersive bounds and subtitle placement/width survive restart including negative monitor origins");
Check(windowReload.Width == loaded.Width && windowReload.Height == loaded.Height && windowReload.Hotkeys["Hide"] == "Ctrl+Shift+H"
    && windowReload.Bookmarks[0].Position == 12, "separate immersive geometry preserves normal bounds and existing user data");
store.Save(new AppSettings { ImmersiveBounds = new(10, 20, 0, 360), OverlayWidth = 10 });
var invalidBounds = store.Load();
Check(invalidBounds.ImmersiveBounds == null && invalidBounds.OverlayWidth == 260, "invalid saved bounds are rejected without affecting legacy placement defaults");
loaded.SubtitleOpacity = 0.55;
store.Save(loaded);
var subtitleAppearance = store.Load();
Check(subtitleAppearance.SubtitleOpacity == 0.55 && subtitleAppearance.Opacity == loaded.Opacity
    && subtitleAppearance.GetImmersiveOpacity() == loaded.GetImmersiveOpacity(), "subtitle opacity persists independently of main and immersive opacity");
Check(subtitleAppearance.OverlayWidth == loaded.OverlayWidth && subtitleAppearance.Hotkeys["Hide"] == "Ctrl+Shift+H"
    && subtitleAppearance.Bookmarks[0].Position == 12, "subtitle opacity preserves placement and user data");
store.Save(new AppSettings { SubtitleOpacity = -1 });
Check(store.Load().SubtitleOpacity == 0.2, "subtitle opacity lower bound keeps overlay visible");
store.Save(new AppSettings { SubtitleOpacity = 2 });
Check(store.Load().SubtitleOpacity == 1, "subtitle opacity upper bound is normalized");
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
var endfieldProfile = calibration with { Game = VisionGame.Endfield };
recentCalibration.OnlineVisionCalibration = endfieldProfile;
recentCalibration.Hotkeys["Hide"] = "Ctrl+Shift+H";
recentCalibration.Bookmarks.Add(new() { Url = "https://example.com/saved", Position = 42 });
store.Save(recentCalibration);
var endfieldReload = store.Load();
Check(endfieldReload.OnlineVisionCalibration == endfieldProfile && endfieldReload.Hotkeys["Hide"] == "Ctrl+Shift+H"
    && endfieldReload.Bookmarks[0].Position == 42 && endfieldReload.OnlineVisionProfiles.Count == 2,
    "Endfield calibration persists without resetting older selections, hotkeys or bookmarks");
var oldOnline = System.Text.Json.JsonSerializer.Deserialize<OnlineVisionProfile>("{\"Width\":1920,\"Height\":1080,\"Region\":{\"X\":0.1,\"Y\":0.2,\"Width\":0.1,\"Height\":0.1},\"NorthLocked\":true,\"NorthAngle\":27}");
Check(oldOnline?.Game == VisionGame.Genshin && oldOnline.NorthAngle == 27, "old online profiles default to Genshin and retain north angle");
Check(System.Text.Json.JsonSerializer.Deserialize<VisualDirectionTrack>("{}")!.Game == VisionGame.Genshin,
    "old local direction tracks retain Genshin recognition");
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
Check(oldAppearance.HideImmersiveFromAltTab && oldAppearance.Opacity == 0.6
    && oldAppearance.Hotkeys["Hide"] == "Ctrl+Shift+H", "legacy profiles gain Alt+Tab exclusion without resetting opacity or custom hotkeys");
Check(oldAppearance.ImmersiveOpacity == null && oldAppearance.GetImmersiveOpacity() == 0.6
    && oldAppearance.Hotkeys["Hide"] == "Ctrl+Shift+H", "legacy opacity and hide binding migrate without a forced new value");
Check(oldAppearance.DanmakuDisplayArea == 1 && oldAppearance.DanmakuOpacity == 1
    && oldAppearance.DanmakuFontScale == 1 && oldAppearance.DanmakuSpeed == 1
    && oldAppearance.Hotkeys["Hide"] == "Ctrl+Shift+H", "legacy JSON without danmaku fields retains appearance and custom binding");
Check(oldAppearance.SubtitleOpacity == 1, "legacy profiles preserve previous subtitle opacity");
Check(oldAppearance.SubtitleFontSize == 16 && oldAppearance.DirectionFontSize == 21,
    "legacy settings retain the existing subtitle and direction font sizes");
oldAppearance.SubtitleFontSize = 28; oldAppearance.DirectionFontSize = 36;
oldAppearance.OverlayWidth = 630; oldAppearance.OverlayLeft = 321; oldAppearance.OverlayTop = 654;
oldAppearance.Bookmarks = [new() { Url = "https://example.com/font-check", Position = 42 }];
var fontStore = new SettingsStore(oldDir); fontStore.Save(oldAppearance);
var fontReload = fontStore.Load();
Check(fontReload.SubtitleFontSize == 28 && fontReload.DirectionFontSize == 36,
    "independent subtitle and direction font sizes survive settings reload");
Check(fontReload.OverlayWidth == 630 && fontReload.OverlayLeft == 321 && fontReload.OverlayTop == 654
    && fontReload.Hotkeys["Hide"] == "Ctrl+Shift+H" && fontReload.Opacity == 0.6
    && fontReload.Bookmarks[0].Position == 42, "font changes preserve overlay geometry, custom hotkeys and video data");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"SubtitleFontSize\":0,\"DirectionFontSize\":100}");
var fontBounds = fontStore.Load();
Check(fontBounds.SubtitleFontSize == 12 && fontBounds.DirectionFontSize == 48,
    "font size bounds normalize when reading external settings");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"SubtitleFontSize\":100,\"DirectionFontSize\":0}");
fontBounds = fontStore.Load();
Check(fontBounds.SubtitleFontSize == 48 && fontBounds.DirectionFontSize == 12,
    "font size bounds apply independently to both settings");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"Hotkeys\":{\"PlayPause\":\"Ctrl+Alt+Shift+V\"}}");
var collision = new SettingsStore(oldDir).Load();
Check(collision.Hotkeys["PlayPause"] == "Ctrl+Alt+Shift+V" && collision.Hotkeys["TemporaryRate"] != "Ctrl+Alt+Shift+V",
    "new hotkey defaults do not replace colliding existing user bindings");
Check(migrated.Hotkeys["FullscreenDanmaku"] == "Ctrl+Alt+D", "legacy profiles gain the fullscreen danmaku shortcut");
File.WriteAllText(Path.Combine(oldDir, "settings.json"), "{\"Hotkeys\":{\"PlayPause\":\"Ctrl+Alt+D\"}}");
var danmakuCollision = new SettingsStore(oldDir).Load();
Check(danmakuCollision.Hotkeys["PlayPause"] == "Ctrl+Alt+D" && danmakuCollision.Hotkeys["FullscreenDanmaku"] != "Ctrl+Alt+D"
    && danmakuCollision.Hotkeys.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == danmakuCollision.Hotkeys.Count,
    "new danmaku default chooses a free fallback without changing a colliding user binding");
danmakuCollision.Hotkeys["FullscreenDanmaku"] = "Ctrl+Shift+Mouse4"; danmakuCollision.FullscreenDanmaku = false;
new SettingsStore(oldDir).Save(danmakuCollision);
var customDanmaku = new SettingsStore(oldDir).Load();
Check(customDanmaku.Hotkeys["FullscreenDanmaku"] == "Ctrl+Shift+Mouse4" && !customDanmaku.FullscreenDanmaku
    && customDanmaku.Hotkeys["PlayPause"] == "Ctrl+Alt+D", "custom danmaku side-key and toggle persist without changing other bindings");
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
var resizeOrigin = new WindowPlacement(-1200, -100, 640, 360);
foreach (var ratio in new[] { 16d / 9, 4d / 3, 9d / 16 })
{
    foreach (var edges in new[] { ResizeEdges.Left, ResizeEdges.Right, ResizeEdges.Top, ResizeEdges.Bottom,
        ResizeEdges.Left | ResizeEdges.Top, ResizeEdges.Right | ResizeEdges.Top,
        ResizeEdges.Left | ResizeEdges.Bottom, ResizeEdges.Right | ResizeEdges.Bottom })
    {
        var fitted = new WindowPlacement(-1200, -100, 640, 640 / ratio);
        var resized = WindowResizing.Calculate(fitted, edges, 150, 90, 320, 180, ratio);
        Check(Math.Abs(resized.Width / resized.Height - ratio) < 0.00001 && resized.Width >= 320 && resized.Height >= 180,
            $"aspect resize preserves {ratio:0.###} ratio and minimums at {edges}");
        var anchors = ((edges & ResizeEdges.Left) == 0 || Math.Abs(resized.Left + resized.Width - fitted.Left - fitted.Width) < 0.00001)
            && ((edges & ResizeEdges.Top) == 0 || Math.Abs(resized.Top + resized.Height - fitted.Top - fitted.Height) < 0.00001);
        Check(anchors, $"aspect resize keeps opposite anchors at negative screen coordinates for {edges}");
    }
    var minimum = WindowResizing.Calculate(resizeOrigin, ResizeEdges.Right | ResizeEdges.Bottom, -2000, -2000, 320, 180, ratio);
    Check(minimum.Width >= 320 && minimum.Height >= 180 && Math.Abs(minimum.Width / minimum.Height - ratio) < 0.00001,
        $"inverted drag respects joint minimums for {ratio:0.###}");
}
var freeResize = WindowResizing.Calculate(resizeOrigin, ResizeEdges.Left | ResizeEdges.Top, 100, 40, 100, 100);
Check(freeResize == new WindowPlacement(-1100, -60, 540, 320), "normal window retains free resizing and opposite corner");
var verticalCorner = WindowResizing.Calculate(resizeOrigin, ResizeEdges.Right | ResizeEdges.Bottom, 0, 100, 320, 180, 16d / 9);
Check(verticalCorner.Width > resizeOrigin.Width && verticalCorner.Height > resizeOrigin.Height,
    "vertical-only corner gesture still resizes fixed-ratio window");
Console.WriteLine($"{checks} checks passed.");
