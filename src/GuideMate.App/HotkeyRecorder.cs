namespace GuideMate.App;

internal sealed class HotkeyRecorder : TextBox
{
    private string _beforeRecording = "";
    private readonly Action<string> _showError;
    private Window? _recordingWindow;

    public HotkeyRecorder(string binding, Action<bool> recordingChanged, Action<string> showError)
    {
        Text = binding; IsReadOnly = true; IsUndoEnabled = false;
        SetResourceReference(StyleProperty, typeof(TextBox));
        ToolTip = "点击后按下组合键或鼠标侧键（Mouse4 / Mouse5）；Esc 取消本次录制";
        _showError = showError;
        InputMethod.SetIsInputMethodEnabled(this, false);
        GotKeyboardFocus += (_, _) =>
        {
            _beforeRecording = Text;
            recordingChanged(true);
            _recordingWindow = Window.GetWindow(this);
            if (_recordingWindow != null) _recordingWindow.PreviewMouseDown += RecordMouse;
            SelectAll();
        };
        LostKeyboardFocus += (_, _) =>
        {
            if (_recordingWindow != null) _recordingWindow.PreviewMouseDown -= RecordMouse;
            _recordingWindow = null;
            recordingChanged(false);
        };
        PreviewTextInput += (_, e) => e.Handled = true;
        PreviewKeyDown += RecordKey;
        PreviewMouseDown += RecordMouse;
    }

    private void RecordMouse(object sender, MouseButtonEventArgs e)
    {
        if (!IsKeyboardFocusWithin || e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
        e.Handled = true;
        try { Text = NativeHotkeys.Format(e.ChangedButton, Keyboard.Modifiers); SelectAll(); _showError(""); }
        catch (FormatException ex) { _showError(ex.Message); }
    }

    private void RecordKey(object sender, KeyEventArgs e)
    {
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift) return;
        e.Handled = true;
        if (e.IsRepeat || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            Text = _beforeRecording; _showError(""); Keyboard.ClearFocus(); return;
        }
        try { Text = NativeHotkeys.Format(key, modifiers); SelectAll(); _showError(""); }
        catch (FormatException ex) { _showError(ex.Message); }
    }
}
