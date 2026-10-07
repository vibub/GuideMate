using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task RunOnlineVisionProbeAsync()
    {
        if (_onlineVisionLiveUrl != null) { await RunOnlineVisionLiveProbeAsync(); return; }
        var checks = new List<string>();
        async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Timeout: " + label + "; method=" + _onlineMethod
                    + "; direction=" + _direction.Text + "; position=" + _position + "; seeking=" + _videoSeeking
                    + "; " + _visionSummary.Text + "; " + _status.Text);
                await Task.Delay(100);
            }
            checks.Add(label);
        }
        var hotkeys = JsonSerializer.Serialize(_settings.Hotkeys);
        var region = new ArrowRegion(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d);
        try
        {
            await VerifySubtitleLayoutAsync(checks);
            var savedProfile = new OnlineVisionProfile(1280, 720, region, true, 0);
            foreach (var variant in new[] { "", "?cross=1", "?frame=1", "?nested=1", "?small=1" })
            {
                var version = _navigationVersion;
                Navigate("http://127.0.0.1:18768/online-vision-test.html" + variant);
                var expectedWidth = variant == "?small=1" ? 640 : 1280;
                await Wait(() => _navigationVersion > version && _videoWidth == expectedWidth && _duration > 5,
                    "HTML5 fixture loaded: " + variant);
                await SetRateAsync(1); await CommandAsync("pause"); await CommandAsync("position", 0.5);
                await Wait(() => Math.Abs(_position - 0.5) < 0.1 && !_videoSeeking, "fixture seek completes: " + variant);
                var frame = await CaptureOnlineFrameAsync();
                if (frame == null) throw new Exception("No calibration preview for " + variant);
                File.WriteAllBytes(Path.Combine(_dataPath, "online-preview" + variant.Replace("?", "-").Replace("=", "-") + ".png"), frame.Image);
                checks.Add("calibration preview captured from " + frame.Method + ": " + variant);
                var profile = savedProfile with { Width = frame.Width, Height = frame.Height };
                if (variant.Length == 0)
                {
                    var calibration = new VisionCalibrationWindow(this, "", "synthetic online video", frame.Time, frame, profile);
                    try
                    {
                        calibration.Show();
                        await Wait(() => calibration.PreviewReady && calibration.PreviewAngle is < 10 or > 350,
                            "online calibration detects the synthetic north arrow");
                        calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "online-calibration-large.png"));
                        calibration.Width = 680; calibration.Height = 520; await Task.Delay(150);
                        calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "online-calibration-minimum.png"));
                        checks.Add("online calibration fits normal and minimum sizes");
                    }
                    finally { calibration.Close(); }
                }
                if (variant.Length == 0) ApplyOnlineCalibration(profile);
                else await Wait(() => _onlineProfile?.Region == region && _onlineProfile.Width == frame.Width,
                    "new page or resolution automatically reuses saved region: " + variant);
                await Wait(() => _direction.Text.StartsWith("北 ") && _onlineMethod == (variant.Length == 0 || variant == "?small=1" ? "canvas" : "preview"),
                    "north arrow and expected capture method: " + variant);
                var label = _direction.Text; await Task.Delay(600);
                if (!_paused || _direction.Text != label) throw new Exception("Paused direction changed");
                checks.Add("paused online direction remains synchronized: " + variant);
                await CommandAsync("position", 2.5);
                if (_onlineHint != null) throw new Exception("Seek kept an obsolete direction");
                await Wait(() => _direction.Text.StartsWith("东 "), "seek refreshes east direction: " + variant);
                await CommandAsync("position", 4.5);
                await Wait(() => _onlineSampleTick > 0 && _onlineHint == null && _direction.Text == "—",
                    "blank frame clears old direction: " + variant);
                await CommandAsync("position", 0.5);
                await Wait(() => _direction.Text.StartsWith("北 "), "returning to arrow recovers direction: " + variant);
                _visionToggle.IsChecked = false;
                if (_onlineHint != null || _direction.Text != "—") throw new Exception("Visual toggle kept online hint");
                _visionToggle.IsChecked = true;
                await Wait(() => _direction.Text.StartsWith("北 "), "visual toggle restores online source: " + variant);
                if (variant == "?cross=1")
                {
                    ToggleImmersive(); Width = 640; Height = 380; await Task.Delay(400);
                    await Wait(() => _direction.Text.StartsWith("北 "), "cross-origin native capture follows immersive video geometry");
                    HideToTray();
                    if (_onlineHint != null) throw new Exception("Hidden online video kept direction");
                    await Task.Delay(600); RestoreMainWindow();
                    await Wait(() => _direction.Text.StartsWith("北 "), "restore resumes online direction sampling");
                    ToggleImmersive();
                }
                var restored = new SettingsStore(_dataPath).Load();
                if (restored.OnlineVisionCalibration != savedProfile || JsonSerializer.Serialize(restored.Hotkeys) != hotkeys)
                    throw new Exception("Profile persistence changed calibration or hotkeys");
                checks.Add("online calibration persists without changing hotkeys: " + variant);
            }
            var navigation = _navigationVersion;
            OpenDemo();
            if (_onlineHint != null) throw new Exception("Navigation retained direction");
            await Wait(() => _navigationVersion > navigation && _onlineProfile?.Region == region
                && _onlineSampleTick > 0 && _onlineHint == null,
                "navigation preserves selection while clearing stale direction");
            WriteSmokeResult(true, "", checks);
        }
        catch (Exception ex) { WriteSmokeResult(false, ex.ToString(), checks); }
        finally { Close(); }
    }
}
