using System.Windows.Threading;

namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyImmersiveControlsAsync(List<string> checks)
    {
        async Task AssertScript(string expression, string description)
        {
            if (await _videoRouter!.ExecuteAsync(expression) != "true") throw new Exception(description);
            checks.Add(description);
        }

        await _videoRouter!.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointermove'))");
        await AssertScript("document.querySelector('video').controls", "immersive mouse activity shows original native controls");
        await Task.Delay(2100);
        await AssertScript("!document.querySelector('video').controls && getComputedStyle(document.querySelector('video')).cursor === 'default'",
            "immersive idle hides progress controls without hiding cursor, including paused video");
        await CommandAsync("focusOn");
        await _videoRouter.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointermove'))");
        await AssertScript("document.querySelector('video').controls", "repeated focus preserves original control policy and movement restores controls");
        await _videoRouter.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointerdown'))");
        await Task.Delay(2100);
        await AssertScript("document.querySelector('video').controls", "immersive does not hide controls while pointer is held for seeking");
        await _videoRouter.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointerup'))");
        await Task.Delay(2100);
        await AssertScript("!document.querySelector('video').controls", "immersive controls hide again after pointer release");
        await CommandAsync("focusOff");
        await AssertScript("document.querySelector('video').controls", "focus off immediately restores native controls");
        await Task.Delay(2100);
        await AssertScript("document.querySelector('video').controls", "normal mode has no leftover control hiding timer");
        await _videoRouter.ExecuteAsync("document.querySelector('video').controls = false");
        await CommandAsync("focusOn");
        await _videoRouter.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointermove'))");
        await AssertScript("!document.querySelector('video').controls", "custom web players do not gain unwanted native controls on movement");
        await CommandAsync("focusOff");
        await AssertScript("!document.querySelector('video').controls", "focus off preserves originally disabled controls");
        await _videoRouter.ExecuteAsync("document.querySelector('video').controls = true");
        await CommandAsync("focusOn");
        await _videoRouter.ExecuteAsync("window.dispatchEvent(new PointerEvent('pointermove'))");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
