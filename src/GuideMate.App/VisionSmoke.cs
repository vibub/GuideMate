using GuideMate.Vision;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyVisionAsync(List<string> checks)
    {
        async Task Wait(Func<bool> condition, string label)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Timeout: " + label);
                await Task.Delay(100);
            }
            checks.Add(label);
        }
        NavigateMedia(_initialMedia!);
        await Wait(() => _localFile == _initialMedia && _duration > 1193 && _visualTrack != null, "actual AV1 guide loads its matching visual cache");
        await CommandAsync("pause"); await CommandAsync("position", 60);
        await Wait(() => Math.Abs(_position - 60) < 0.2 && _direction.Text.StartsWith("东北") && _directionKind.Text.Contains("视觉"), "real guide seek synchronizes visual northeast");
        await CommandAsync("position", 300);
        await Wait(() => Math.Abs(_position - 300) < 0.2 && _direction.Text.StartsWith("西北"), "real guide seek synchronizes visual northwest");
        var label = _direction.Text; await Task.Delay(400);
        if (!_paused || _direction.Text != label) throw new Exception("Visual direction changed while paused");
        checks.Add("paused guide keeps a time-bound visual direction");
        _visionToggle.IsChecked = false;
        if (_direction.Text != "—") throw new Exception("Disabling visual direction kept the old visual hint");
        _visionToggle.IsChecked = true;
        if (!_direction.Text.StartsWith("西北")) throw new Exception("Visual direction did not restore");
        checks.Add("visual toggle clears and restores the correct source");
        var calibration = new VisionCalibrationWindow(this, VideoAnalysis.FindFfmpeg()!, _initialMedia!, 300);
        try
        {
            calibration.Show();
            await Wait(() => calibration.PreviewReady && calibration.PreviewAngle is > 310 and < 340, "calibration previews a decoded frame and detects its arrow");
            calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "calibration-large.png"));
            calibration.Width = 680; calibration.Height = 520; await Task.Delay(200);
            calibration.VerifyLayoutAndCapture(Path.Combine(_dataPath, "calibration-minimum.png"));
            checks.Add("calibration controls fit both normal and minimum window sizes");
        }
        finally { calibration.Close(); }
        OpenDemo();
        await Wait(() => _localFile == null && _visualTrack == null && _duration is > 35 and < 50, "leaving a local guide clears its visual track");
    }
}
