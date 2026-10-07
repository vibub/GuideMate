using System.Windows.Interop;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyCursorAsync(List<string> checks)
    {
        async Task Wait(Func<bool> condition, string description)
        {
            var end = DateTime.UtcNow.AddSeconds(15);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception(description); await Task.Delay(100); }
        }
        void AssertCursor(Cursor? requested, Cursor expected, string description)
        {
            _browser.SetCurrentValue(FrameworkElement.CursorProperty, requested);
            if (_browser.Cursor != expected) throw new Exception(description);
            var query = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent };
            _browser.RaiseEvent(query);
            if (query.Cursor != expected) throw new Exception("WPF query returned unexpected cursor: " + description);
            checks.Add(description);
        }

        Navigate("https://guidemate.local/cursor-test.html");
        await Wait(() => _url.Contains("cursor-test") && _duration > 35, "Cursor fixture did not load");
        if (_immersive || _workspace.ForceCursor) throw new Exception("Normal cursor test requires non-forced normal mode");
        var styles = await _videoRouter!.ExecuteAsync("""
            (() => {
              const player = document.querySelector('#player').getBoundingClientRect();
              const at = (x,y) => getComputedStyle(document.elementFromPoint(x,y)).cursor;
              return at(player.right + 10, player.top + player.height / 2) === 'auto'
                && at(player.right - 10, player.top + player.height / 2) === 'none'
                && at(player.left + player.width / 2, player.bottom - 10) === 'default';
            })()
            """);
        if (styles != "true") throw new Exception("Directional cursor fixture did not match hidden plane and visible controls: " + styles);
        checks.Add("fixture reproduces right-entry hidden video layer versus bottom-entry visible controls");
        AssertCursor(Cursors.None, Cursors.Arrow, "normal browser rejects hidden cursor from composition controller");
        AssertCursor(null, Cursors.Arrow, "normal browser replaces null cursor with visible arrow");
        using (var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(0, false))
        {
            using var cursor = CursorInteropHelper.Create(handle);
            if (ReferenceEquals(cursor, Cursors.None)) throw new Exception("Native hidden cursor test must use a distinct wrapper");
            AssertCursor(cursor, Cursors.Arrow, "zero native cursor handle is also kept visible");
        }
        AssertCursor(Cursors.Hand, Cursors.Hand, "normal browser preserves link hand cursor");
        AssertCursor(Cursors.IBeam, Cursors.IBeam, "normal browser preserves text I-beam cursor");
        AssertCursor(Cursors.SizeWE, Cursors.SizeWE, "normal browser preserves resize cursor");
        foreach (var (native, name) in new[] {
            (System.Windows.Forms.Cursors.Hand, "hand"),
            (System.Windows.Forms.Cursors.IBeam, "text"),
            (System.Windows.Forms.Cursors.SizeWE, "resize") })
        {
            using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(native.Handle, false);
            using var cursor = CursorInteropHelper.Create(handle);
            AssertCursor(cursor, cursor, "browser preserves native wrapped " + name + " cursor");
        }
        _browser.Cursor = Cursors.Hand;
        _browser.ClearValue(FrameworkElement.CursorProperty);
        if (_browser.Cursor != Cursors.Arrow) throw new Exception("Cursor clear removed visible fallback");
        checks.Add("clearing page cursor retains visible default without global cursor override");
        for (var i = 0; i < 30; i++)
        {
            _browser.SetCurrentValue(FrameworkElement.CursorProperty, Cursors.Hand);
            _browser.SetCurrentValue(FrameworkElement.CursorProperty, Cursors.None);
            if (_browser.Cursor != Cursors.Arrow) throw new Exception("Repeated hidden cursor bypassed coercion");
        }
        checks.Add("repeated page cursor transitions cannot leave browser cursor hidden");
        var state = WindowState;
        try
        {
            WindowState = WindowState.Maximized; await Task.Delay(200);
            AssertCursor(Cursors.None, Cursors.Arrow, "maximized normal browser also rejects hidden cursor");
        }
        finally { WindowState = state; }
        Navigate("https://guidemate.local/iframe-test.html");
        await Wait(() => _url.Contains("iframe-test") && _duration > 35 && _videoRouter!.ActiveFrame != 0, "Cursor iframe fixture did not load");
        await _videoRouter!.ExecuteAsync("document.querySelector('video').style.cursor = 'none'");
        AssertCursor(Cursors.None, Cursors.Arrow, "iframe page hidden cursor is guarded at the native browser boundary");
        if (await _videoRouter.ExecuteAsync("getComputedStyle(document.querySelector('video')).cursor") != "\"none\"")
            throw new Exception("Native protection unexpectedly rewrote iframe CSS");
        checks.Add("cursor protection does not rewrite page or iframe cursor styles");
        OpenDemo();
        await Wait(() => _url.Contains("demo.html") && _duration > 35 && _cues.Count > 0, "Cursor test did not restore demo");
        _browser.ClearValue(FrameworkElement.CursorProperty);
    }
}
