using GuideMate.Vision;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyEndfieldVisionAsync(List<string> checks)
    {
        async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Timeout: " + label + "; " + _status.Text);
                await Task.Delay(100);
            }
            checks.Add(label);
        }
        NavigateMedia(_initialMedia!);
        await Wait(() => _localFile == _initialMedia && _duration is > 416 and < 418 && _visualTrack?.Game == VisionGame.Endfield,
            "actual Endfield HEVC guide loads its matching visual cache");
        await CommandAsync("pause"); await CommandAsync("position", 60);
        await Wait(() => Math.Abs(_position - 60) < 0.2 && _direction.Text.StartsWith("西 "), "Endfield local seek synchronizes west heading");
        await CommandAsync("position", 300);
        await Wait(() => Math.Abs(_position - 300) < 0.2 && _direction.Text.StartsWith("北 "), "Endfield local seek synchronizes north heading");
        var label = _direction.Text; await Task.Delay(400);
        if (!_paused || _direction.Text != label) throw new Exception("Endfield paused direction changed");
        checks.Add("paused Endfield guide retains time-bound heading");
        await CommandAsync("position", 5);
        await Wait(() => Math.Abs(_position - 5) < 0.2 && _direction.Text == "—", "Endfield map frame clears old heading");
        await CommandAsync("position", 60);
        await Wait(() => Math.Abs(_position - 60) < 0.2 && _direction.Text.StartsWith("西 "), "Endfield local return restores heading");
        var originalTrack = _visualTrack;
        var calibration = new VisionCalibrationWindow(this, VideoAnalysis.FindFfmpeg()!, _initialMedia!, 60, game: VisionGame.Endfield);
        try
        {
            calibration.Show();
            await Wait(() => calibration.PreviewReady && calibration.PreviewAngle is > 260 and < 280,
                "Endfield calibration previews actual marker with its enlarged-map preset");
            calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "endfield-calibration-large.png"));
            calibration.Width = 680; calibration.Height = 520; await Task.Delay(200);
            calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "endfield-calibration-minimum.png"));
            checks.Add("Endfield calibration game selector and controls fit both window sizes");
            calibration.SelectGame(VisionGame.Genshin);
            if (calibration.PreviewAngle != null) throw new Exception("Genshin mode accepted Endfield marker");
            calibration.SelectGame(VisionGame.Endfield);
            if (calibration.PreviewAngle is not (> 260 and < 280)) throw new Exception("Endfield mode did not restore marker detection");
            checks.Add("calibration game choice controls the detector without saving settings");
        }
        finally { calibration.Close(); }
        if (_visualTrack != originalTrack || VisualDirectionCache.Load(_dataPath, _initialMedia!)?.Game != VisionGame.Endfield)
            throw new Exception("Cancelling calibration replaced the local track");
        checks.Add("cancelling Endfield calibration preserves the existing cache");
        // Feed a captured real guide frame through the online calibration and
        // sampling path in this isolated profile; it is not live Bilibili proof.
        OnlineVideoFrame? frame = null;
        await Wait(() => !_videoSeeking, "Endfield video seek finishes before browser frame capture");
        for (var attempt = 0; attempt < 30 && frame == null; attempt++)
        { frame = await CaptureOnlineFrameAsync(); if (frame == null) await Task.Delay(200); }
        if (frame == null)
        {
            var state = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify([...document.querySelectorAll('video')].map(v=>({ready:v.readyState,width:v.videoWidth,height:v.videoHeight,seeking:v.seeking,time:v.currentTime,error:v.error?.message,rect:v.getBoundingClientRect().toJSON()})))");
            await using var screenshot = File.Create(Path.Combine(_dataPath, "endfield-capture-failure.png"));
            await _browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            throw new Exception("Endfield browser frame capture failed; " + state + "; visible=" + IsVisible + "; window=" + WindowState + "; browserVisible=" + _browser.IsVisible);
        }
        var onlineProfile = new OnlineVisionProfile(frame.Width, frame.Height, originalTrack!.Region, true, 0, VisionGame.Endfield);
        var onlineCalibration = new VisionCalibrationWindow(this, "", "终末地在线画面样例", frame.Time, frame, onlineProfile);
        try
        {
            onlineCalibration.Show();
            await Wait(() => onlineCalibration.PreviewReady && onlineCalibration.PreviewAngle is > 250 and < 290,
                "Endfield online calibration detects a browser-captured actual guide frame");
            onlineCalibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "endfield-online-calibration.png"));
        }
        finally { onlineCalibration.Close(); }
        OpenDemo();
        await Wait(() => _localFile == null && _visualTrack == null && _duration is > 35 and < 50,
            "leaving Endfield guide clears its local visual track");
    }
}
