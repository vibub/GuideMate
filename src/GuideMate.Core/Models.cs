namespace GuideMate.Core;

public record SubtitleCue(double Start, double End, string Text);
public record DirectionHint(string Label, double? Angle, string Category, string Source);
public enum WindowTopmostMode { Never, Immersive, Always }

public class SavedVideo
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? LocalPath { get; set; }
    public double Position { get; set; }
    public double Rate { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public override string ToString() => $"{Title}\n{TimeSpan.FromSeconds(Math.Max(0, Position)):hh\\:mm\\:ss}";
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

public class AppSettings
{
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 1160;
    public double Height { get; set; } = 760;
    public WindowPlacement? ImmersiveBounds { get; set; }
    public double OverlayLeft { get; set; } = 140;
    public double OverlayTop { get; set; } = 860;
    public double OverlayWidth { get; set; } = 520;
    public double SubtitleOpacity { get; set; } = 1;
    public double SubtitleFontSize { get; set; } = 16;
    public double DirectionFontSize { get; set; } = 21;
    public bool Topmost { get; set; } = true;
    public WindowTopmostMode? TopmostMode { get; set; }
    public double Opacity { get; set; } = 1;
    public double? ImmersiveOpacity { get; set; }
    public bool HideImmersiveFromAltTab { get; set; } = true;
    public bool FullscreenDanmaku { get; set; } = true;
    public double DanmakuDisplayArea { get; set; } = 1;
    public double DanmakuOpacity { get; set; } = 1;
    public double DanmakuFontScale { get; set; } = 1;
    public double DanmakuSpeed { get; set; } = 1;
    public bool XRayEnabled { get; set; }
    public double XRayRadius { get; set; } = 70;
    public double HoleRatio { get; set; } = 0.3;
    public double Rate { get; set; } = 1;
    public double SeekSeconds { get; set; } = 5;
    public double TemporaryRate { get; set; } = 2;
    public int TemporaryHoldMilliseconds { get; set; } = 400;
    public double SubtitleOffset { get; set; }
    public bool SubtitleOverlay { get; set; }
    public bool PauseOnCombat { get; set; }
    public bool FadeOnCombat { get; set; }
    public bool UseVisualDirection { get; set; } = true;
    public string? FfmpegPath { get; set; }
    public Dictionary<string, OnlineVisionProfile> OnlineVisionProfiles { get; set; } = [];
    public OnlineVisionProfile? OnlineVisionCalibration { get; set; }
    public List<SavedVideo> Bookmarks { get; set; } = [];
    public List<SavedVideo> History { get; set; } = [];
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();

    // Older profiles contain only the Topmost checkbox value.
    public WindowTopmostMode GetTopmostMode() => TopmostMode ?? (Topmost ? WindowTopmostMode.Always : WindowTopmostMode.Never);
    public bool ShouldBeTopmost(bool immersive) => GetTopmostMode() switch
    {
        WindowTopmostMode.Always => true,
        WindowTopmostMode.Immersive => immersive,
        _ => false
    };

    public double GetImmersiveOpacity() => ImmersiveOpacity ?? Opacity;

    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        ["PlayPause"] = "Ctrl+Alt+Space",
        ["SeekBack"] = "Ctrl+Alt+Left",
        ["SeekForward"] = "Ctrl+Alt+Right",
        ["RateUp"] = "Ctrl+Alt+Up",
        ["RateDown"] = "Ctrl+Alt+Down",
        ["TemporaryRate"] = "Ctrl+Alt+Shift+V",
        ["PreviousEpisode"] = "Ctrl+Alt+PageUp",
        ["NextEpisode"] = "Ctrl+Alt+PageDown",
        ["Immersive"] = "Ctrl+Alt+Shift+I",
        ["FullscreenDanmaku"] = "Ctrl+Alt+D",
        ["Hide"] = "Ctrl+Alt+H",
        ["ClickThrough"] = "Ctrl+Alt+P"
    };
}
