using System.Text.Json;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task RunOnlineVisionLiveProbeAsync()
    {
        var checks = new List<string>();
        var samples = new List<object>();
        async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Timeout: " + label + "; " + _status.Text + "; " + _visionSummary.Text);
                await Task.Delay(200);
            }
            checks.Add(label);
        }
        try
        {
            Navigate(_onlineVisionLiveUrl!);
            await Wait(() => _videoWidth > 0 && _videoHeight > 0 && _duration > 300,
                "live Bilibili guide loaded using isolated browser data");
            await SetRateAsync(1); await CommandAsync("pause");
            foreach (var time in new[] { 60d, 300d })
            {
                await CommandAsync("position", time);
                await Wait(() => Math.Abs(_position - time) < 0.2 && !_videoSeeking,
                    "live guide seek completes at " + time);
                await CommandAsync("pause");
                var frame = await CaptureOnlineFrameAsync();
                if (frame == null) throw new Exception("Live frame is unavailable");
                File.WriteAllBytes(Path.Combine(_dataPath, $"live-online-frame-{time:0}.png"), frame.Image);
                var profile = new OnlineVisionProfile(frame.Width, frame.Height,
                    new ArrowRegion(210 / 1920d, 205 / 1080d, 140 / 1920d, 140 / 1080d), true, 0);
                ApplyOnlineCalibration(profile);
                await Wait(() => _onlineHint != null && Math.Abs(_onlineTime - _position) < 0.5,
                    "live guide minimap arrow detected at " + time);
                var crop = await CaptureOnlineFrameAsync(profile.Region);
                if (crop != null) File.WriteAllBytes(Path.Combine(_dataPath, $"live-online-arrow-{time:0}.png"), crop.Image);
                samples.Add(new { url = _url, time = _position, width = frame.Width, height = frame.Height,
                    method = _onlineMethod, direction = _onlineHint });
            }
            await CommandAsync("play");
            await Wait(() => !_paused && _position > 301 && _onlineHint != null,
                "playing live guide continues updating time-bound direction");
            await CommandAsync("pause");
            WriteSmokeResult(true, "", checks);
        }
        catch (Exception ex)
        {
            samples.Add(new { error = ex.Message, position = _position, sampleTime = _onlineTime,
                method = _onlineMethod, direction = _onlineHint, width = _videoWidth, height = _videoHeight });
            WriteSmokeResult(false, ex.ToString(), checks);
        }
        finally
        {
            File.WriteAllText(Path.Combine(_dataPath, "live-online-samples.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
