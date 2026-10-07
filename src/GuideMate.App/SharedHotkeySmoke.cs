using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifySharedHotkeysAsync(NativeHotkeys keys, string keyboardBinding, List<string> checks)
    {
        async Task Drain() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        var actions = new List<string>(); var releases = 0;
        void Released() => releases++;
        keys.Pressed += actions.Add; keys.TemporaryRateReleased += Released;
        try
        {
            var error = keys.Apply(new() { ["SeekForward"] = "Mouse5", ["TemporaryRate"] = "XButton2" });
            if (error != null) throw new Exception("Shared side button rejected: " + error);
            checks.Add("one normal action and temporary hold may share a side button including alias");
            var foreground = NativeHotkeys.CurrentForegroundWindow;
            var time = Environment.TickCount64;
            keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
            if (actions.Count != 0) throw new Exception("Shared binding fired before release");
            keys.ProcessMouseButton(6, false, 0, foreground, time + 150); await Drain();
            if (!actions.SequenceEqual(new[] { "SeekForward" }) || releases != 0) throw new Exception("Shared short press did not select tap");
            checks.Add("shared mouse short press emits only normal action on release");
            actions.Clear(); time = Environment.TickCount64;
            keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
            keys.PollHeldBinding(time + 400, true, foreground);
            keys.PollHeldBinding(time + 700, true, foreground);
            keys.ProcessMouseButton(6, false, 0, foreground, time + 750); await Drain();
            if (!actions.SequenceEqual(new[] { "TemporaryRate" }) || releases != 1) throw new Exception("Shared long press also emitted tap");
            checks.Add("shared mouse long press emits temporary rate once and restores without tap");

            actions.Clear(); time = Environment.TickCount64;
            keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
            keys.PollHeldBinding(time + 100, true, foreground + 1);
            keys.ProcessMouseButton(6, false, 0, foreground, time + 150); await Drain();
            if (actions.Count != 0) throw new Exception("Foreground change replayed a short action");
            checks.Add("foreground change cancels pending shared press without replaying tap");
            time = Environment.TickCount64;
            keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
            keys.PollHeldBinding(time + 400, true, foreground);
            keys.PollHeldBinding(time + 500, true, foreground + 1);
            keys.ProcessMouseButton(6, false, 0, foreground, time + 600); await Drain();
            if (!actions.SequenceEqual(new[] { "TemporaryRate" }) || releases != 2) throw new Exception("Foreground change did not restore shared hold");
            checks.Add("foreground change restores active shared hold without replaying tap");
            actions.Clear(); time = Environment.TickCount64;
            keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
            keys.SuspendOrdinaryBindings(); keys.ProcessMouseButton(6, false, 0, foreground, time + 150); await Drain();
            if (actions.Count != 0 || keys.ResumeOrdinaryBindings() != null) throw new Exception("Recording replayed pending shared tap");
            checks.Add("recording cancels pending shared gesture and restores shared bindings");

            error = keys.Apply(new() { ["SeekForward"] = keyboardBinding, ["TemporaryRate"] = keyboardBinding }, 650);
            if (error != null) throw new Exception("Shared keyboard registration failed: " + error);
            var input = NativeHotkeys.Parse(keyboardBinding); time = Environment.TickCount64;
            keys.ProcessKeyboardPress(input, foreground, time);
            keys.PollHeldBinding(time + 200, false, foreground);
            if (!actions.SequenceEqual(new[] { "SeekForward" })) throw new Exception("Shared keyboard tap failed");
            actions.Clear(); time = Environment.TickCount64;
            keys.ProcessKeyboardPress(input, foreground, time);
            keys.PollHeldBinding(time + 649, true, foreground);
            if (actions.Count != 0) throw new Exception("Custom hold time ignored");
            keys.PollHeldBinding(time + 650, true, foreground);
            keys.PollHeldBinding(time + 900, false, foreground);
            if (!actions.SequenceEqual(new[] { "TemporaryRate" }) || releases != 3) throw new Exception("Shared keyboard hold failed");
            keys.SuspendOrdinaryBindings();
            if (keys.ResumeOrdinaryBindings() != null || keys.HoldMilliseconds != 650) throw new Exception("Recording lost custom hold time");
            checks.Add("shared keyboard registers once, dispatches tap or hold, and preserves custom threshold during recording");

            error = keys.Apply(new() { ["SeekForward"] = "Mouse5", ["TemporaryRate"] = "Mouse5" });
            if (error != null) throw new Exception(error);
            keys.Pressed += OnHotkey;
            void RestoreVideoRate() => Fire(EndTemporaryRateAsync);
            keys.TemporaryRateReleased += RestoreVideoRate;
            try
            {
                async Task Wait(Func<bool> condition)
                {
                    var end = DateTime.UtcNow.AddSeconds(10);
                    while (!condition()) { if (DateTime.UtcNow > end) throw new Exception("Shared video gesture timeout"); await Task.Delay(50); }
                }
                await CommandAsync("pause"); await CommandAsync("position", 10);
                await SetRateAsync(1.5); _settings.TemporaryRate = 3;
                await Wait(() => _paused && Math.Abs(_position - 10) < 0.2);
                actions.Clear(); foreground = NativeHotkeys.CurrentForegroundWindow; time = Environment.TickCount64;
                keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
                keys.ProcessMouseButton(6, false, 0, foreground, time + 100); await Drain();
                await Wait(() => Math.Abs(_position - (10 + _settings.SeekSeconds)) < 0.2);
                if (!actions.SequenceEqual(new[] { "SeekForward" })) throw new Exception("Real video tap also boosted rate");
                checks.Add("shared side button short press seeks real WebView2 video without temporary rate");
                var position = _position; actions.Clear(); time = Environment.TickCount64;
                keys.ProcessMouseButton(6, true, 0, foreground, time); await Drain();
                keys.PollHeldBinding(time + 400, true, foreground);
                await Wait(() => _temporaryRateActive && _rate.SelectedItem is double rate && rate == 3);
                keys.ProcessMouseButton(6, false, 0, foreground, time + 800); await Drain();
                await Wait(() => !_temporaryRateActive && _rate.SelectedItem is double rate && rate == 1.5);
                if (!actions.SequenceEqual(new[] { "TemporaryRate" }) || Math.Abs(_position - position) > 0.2 || _settings.Rate != 1.5)
                    throw new Exception("Real video hold also sought or changed saved base rate");
                checks.Add("shared side button hold boosts real video, restores base rate on release, and never seeks");
            }
            finally { keys.Pressed -= OnHotkey; keys.TemporaryRateReleased -= RestoreVideoRate; keys.CancelTemporaryRate(); await EndTemporaryRateAsync(); }
        }
        finally { keys.Pressed -= actions.Add; keys.TemporaryRateReleased -= Released; }
    }
}
