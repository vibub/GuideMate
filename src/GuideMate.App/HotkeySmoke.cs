using System.Windows.Interop;
using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyMouseHotkeysAsync(List<string> checks)
    {
        if (NativeHotkeys.Format(MouseButton.XButton1, ModifierKeys.None) != "Mouse4"
            || NativeHotkeys.Format(MouseButton.XButton2, ModifierKeys.Control | ModifierKeys.Shift) != "Ctrl+Shift+Mouse5"
            || NativeHotkeys.Parse("Alt+XButton1") != (1u, 5u)
            || NativeHotkeys.Parse("Mouse5") != (0u, 6u)) throw new Exception("Side button format failed");
        checks.Add("side buttons and modifier combinations roundtrip with XButton aliases");

        var probeWindow = new Window { Owner = this, Width = 350, Height = 180, Title = "随引侧键回归" };
        _keys!.SuspendOrdinaryBindings();
        try
        {
            new WindowInteropHelper(probeWindow).EnsureHandle();
            using var keys = new NativeHotkeys(probeWindow);
            var actions = new List<string>();
            keys.Pressed += actions.Add;
            var bindings = new Dictionary<string, string> { ["SmokeMouse4"] = "Mouse4", ["SmokeMouse5"] = "Ctrl+Shift+Mouse5" };
            var error = keys.Apply(bindings);
            if (error != null || !keys.MouseHookAvailable) throw new Exception("Mouse hook registration failed: " + error);
            checks.Add("actual Windows side button hook installs");
            async Task Drain() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            void Cycle(uint key, uint modifiers)
            {
                if (!keys.ProcessMouseButton(key, true, modifiers, 0) || !keys.ProcessMouseButton(key, false, 0, 0))
                    throw new Exception("Configured side button was not consumed as a pair");
            }
            Cycle(5, 0); await Drain();
            if (!actions.SequenceEqual(new[] { "SmokeMouse4" })) throw new Exception("Mouse4 action routing failed");
            checks.Add("side button dispatcher routes down and up once");
            if (keys.ProcessMouseButton(6, true, 0, 0) || keys.ProcessMouseButton(6, true, 2, 0)
                || keys.ProcessMouseButton(5, true, 8, 0) || keys.ProcessMouseButton(1, true, 0, 0)
                || keys.ProcessMouseButton(6, false, 0, 0)) throw new Exception("Unrelated input was intercepted");
            checks.Add("unbound buttons, wrong modifiers, Windows key and unmatched releases pass through");
            if (!keys.ProcessMouseButton(6, true, 6, 0) || !keys.ProcessMouseButton(6, true, 6, 0)
                || !keys.ProcessMouseButton(6, false, 0, 0)) throw new Exception("Modified side button routing failed");
            await Drain();
            if (actions.Count != 2 || actions[1] != "SmokeMouse5") throw new Exception("Repeated down fired more than once");
            checks.Add("modified side button suppresses repeated down and consumes up after modifiers release");

            keys.ProcessMouseButton(5, true, 0, 0);
            keys.SuspendOrdinaryBindings();
            keys.ProcessMouseButton(5, false, 0, 0);
            if (keys.ProcessMouseButton(6, true, 6, 0)) throw new Exception("Recording intercepted side button");
            await Drain();
            if (actions.Count != 2 || keys.ResumeOrdinaryBindings() != null) throw new Exception("Stale action ran during recording");
            checks.Add("recording suspends mouse bindings and invalidates already queued actions");
            if (keys.Apply(new() { ["A"] = "Mouse4", ["B"] = "XButton1" }) == null) throw new Exception("Duplicate mouse alias accepted");
            Cycle(5, 0); await Drain();
            if (actions.Count != 3) throw new Exception("Duplicate changed old bindings");
            checks.Add("duplicate side button aliases rejected without losing bindings");

            var keyboardBinding = "";
            var blockerWindow = new Window();
            try
            {
                new WindowInteropHelper(blockerWindow).EnsureHandle();
                using var blocker = new NativeHotkeys(blockerWindow);
                var conflict = "";
                for (var number = 13; number <= 24; number++)
                {
                    var candidate = "Ctrl+Alt+Shift+F" + number;
                    if (blocker.Apply(new() { ["SmokeConflict"] = candidate }) == null) { conflict = candidate; break; }
                }
                if (conflict.Length == 0) throw new Exception("No keyboard binding available for conflict test");
                keyboardBinding = conflict;
                error = keys.Apply(new() { ["Replacement"] = "Mouse5", ["Conflict"] = conflict });
                if (error == null || !error.Contains("已恢复原热键") || keys.ProcessMouseButton(6, true, 0, 0))
                    throw new Exception("Failed registration did not roll back mouse mapping: " + error);
                Cycle(5, 0); await Drain();
                if (actions.Count != 4) throw new Exception("Old mouse binding missing after rollback");
                checks.Add("keyboard registration conflict rolls back mouse mappings and old bindings remain usable");
            }
            finally { blockerWindow.Close(); }

            error = keys.Apply(new() { ["TemporaryRate"] = "Mouse4" });
            if (error != null) throw new Exception(error);
            var releases = 0;
            var foreground = NativeHotkeys.CurrentForegroundWindow;
            var pressedAt = Environment.TickCount64;
            keys.TemporaryRateReleased += () => releases++;
            void ReleaseOnPress(string action)
            {
                if (action == "TemporaryRate" && !keys.ProcessMouseButton(5, false, 0, foreground, pressedAt + 450)) throw new Exception("Temporary mouse release not consumed");
            }
            keys.Pressed += ReleaseOnPress;
            if (!keys.ProcessMouseButton(5, true, 0, foreground, pressedAt)) throw new Exception("Temporary mouse down not consumed");
            await Drain();
            keys.PollHeldBinding(pressedAt + 399, true, foreground);
            if (releases != 0 || actions.Count != 4) throw new Exception("Hold-only binding activated before threshold");
            checks.Add("hold-only side button waits until hold threshold instead of firing on press");
            keys.PollHeldBinding(pressedAt + 400, true, foreground);
            await Drain();
            keys.Pressed -= ReleaseOnPress;
            if (releases != 1 || actions.Last() != "TemporaryRate") throw new Exception("Temporary mouse binding did not release");
            checks.Add("side button temporary rate ends on paired release even when modifiers differ");
            await VerifySharedHotkeysAsync(keys, keyboardBinding, checks);

            var recordingError = "";
            var recorder = new HotkeyRecorder("Ctrl+Alt+K", recording =>
            {
                if (recording) keys.SuspendOrdinaryBindings();
                else recordingError = keys.ResumeOrdinaryBindings() ?? "";
            }, message => recordingError = message);
            var exit = new Button { Content = "完成", Margin = new(0, 10, 0, 0) };
            var panel = new StackPanel { Margin = new(20) }; panel.Children.Add(recorder); panel.Children.Add(exit);
            probeWindow.Content = panel; probeWindow.Show(); probeWindow.Activate(); recorder.Focus(); await Drain();
            if (!recorder.IsKeyboardFocusWithin || !keys.OrdinaryBindingsSuspended) throw new Exception("Recorder did not focus and suspend bindings");
            recorder.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton1)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            if (recorder.Text != "Mouse4" || recordingError != "") throw new Exception("Recorder did not capture Mouse4: " + recordingError);
            exit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton2)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            if (recorder.Text != "Mouse5" || recordingError != "") throw new Exception("Recorder did not capture Mouse5");
            exit.Focus(); await Drain();
            if (keys.OrdinaryBindingsSuspended || recordingError != "") throw new Exception("Leaving recorder did not restore bindings");
            checks.Add("WPF recorder captures both side button events and restores global bindings on focus exit");

            recorder.Focus(); await Drain();
            exit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton1)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            recorder.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(recorder), Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Drain();
            if (recorder.Text != "Mouse5" || keys.OrdinaryBindingsSuspended || recordingError != "") throw new Exception("Escape did not cancel side button recording");
            checks.Add("side button recording works elsewhere in settings window and Escape restores previous binding");

            var settings = new AppSettings(); settings.Hotkeys["PlayPause"] = "Mouse4"; settings.Hotkeys["SeekForward"] = "Ctrl+Mouse5";
            var store = new SettingsStore(Path.Combine(_dataPath, "mouse-settings")); store.Save(settings);
            var loaded = store.Load();
            if (loaded.Hotkeys["PlayPause"] != "Mouse4" || loaded.Hotkeys["SeekForward"] != "Ctrl+Mouse5") throw new Exception("Mouse settings did not persist");
            checks.Add("side button bindings persist and reload in isolated settings");
            keys.Dispose();
            if (keys.MouseHookAvailable || keys.ProcessMouseButton(5, true, 0, 0)) throw new Exception("Disposed mouse hook still active");
            checks.Add("disposing hotkeys removes native mouse hook and disables dispatch");
        }
        finally
        {
            probeWindow.Close();
            var restoreError = _keys.ResumeOrdinaryBindings();
            if (restoreError != null) throw new Exception("Smoke could not restore original bindings: " + restoreError);
            Activate();
        }
    }
}
