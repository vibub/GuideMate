namespace GuideMate.Core;

public enum HoldOutcome { None, Tap, Start, Stop }

public sealed class HoldGesture(long startedAt, int thresholdMilliseconds, bool hasTap)
{
    private bool _started, _finished;

    public HoldOutcome Advance(long now)
    {
        if (_finished || _started || now - startedAt < thresholdMilliseconds) return HoldOutcome.None;
        _started = true;
        return HoldOutcome.Start;
    }

    public HoldOutcome Release(long now)
    {
        if (_finished) return HoldOutcome.None;
        _finished = true;
        if (_started) return HoldOutcome.Stop;
        return hasTap && now - startedAt < thresholdMilliseconds ? HoldOutcome.Tap : HoldOutcome.None;
    }

    public HoldOutcome Cancel()
    {
        if (_finished) return HoldOutcome.None;
        _finished = true;
        return _started ? HoldOutcome.Stop : HoldOutcome.None;
    }
}
