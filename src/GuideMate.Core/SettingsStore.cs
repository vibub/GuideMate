using System.Text.Json;

namespace GuideMate.Core;

public sealed class SettingsStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "settings.json");
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _options) ?? new();
            if (settings.ImmersiveBounds is { IsValid: false }) settings.ImmersiveBounds = null;
            settings.OverlayWidth = double.IsFinite(settings.OverlayWidth) ? Math.Max(260, settings.OverlayWidth) : 520;
            settings.OverlayLeft = double.IsFinite(settings.OverlayLeft) ? settings.OverlayLeft : 140;
            settings.OverlayTop = double.IsFinite(settings.OverlayTop) ? settings.OverlayTop : 860;
            if (settings.TopmostMode.HasValue && !Enum.IsDefined(settings.TopmostMode.Value)) settings.TopmostMode = null;
            settings.Opacity = Math.Clamp(settings.Opacity, 0.2, 1);
            if (settings.ImmersiveOpacity is { } opacity)
                settings.ImmersiveOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.2, 1) : null;
            settings.DanmakuDisplayArea = double.IsFinite(settings.DanmakuDisplayArea) ? Math.Clamp(settings.DanmakuDisplayArea, 0.1, 1) : 1;
            settings.DanmakuOpacity = double.IsFinite(settings.DanmakuOpacity) ? Math.Clamp(settings.DanmakuOpacity, 0, 1) : 1;
            settings.DanmakuFontScale = double.IsFinite(settings.DanmakuFontScale) ? Math.Clamp(settings.DanmakuFontScale, 0.5, 2) : 1;
            settings.DanmakuSpeed = double.IsFinite(settings.DanmakuSpeed) ? Math.Clamp(settings.DanmakuSpeed, 0.5, 2) : 1;
            settings.SubtitleOpacity = double.IsFinite(settings.SubtitleOpacity) ? Math.Clamp(settings.SubtitleOpacity, 0.2, 1) : 1;
            settings.SubtitleFontSize = double.IsFinite(settings.SubtitleFontSize) ? Math.Clamp(settings.SubtitleFontSize, 12, 48) : 16;
            settings.DirectionFontSize = double.IsFinite(settings.DirectionFontSize) ? Math.Clamp(settings.DirectionFontSize, 12, 48) : 21;
            settings.XRayRadius = double.IsFinite(settings.XRayRadius) ? Math.Clamp(settings.XRayRadius, 20, 200) : 70;
            settings.Rate = Math.Clamp(settings.Rate, 0.25, 4);
            settings.SeekSeconds = Math.Clamp(settings.SeekSeconds, 1, 120);
            settings.TemporaryRate = double.IsFinite(settings.TemporaryRate) ? Math.Clamp(settings.TemporaryRate, 0.25, 4) : 2;
            settings.TemporaryHoldMilliseconds = Math.Clamp(settings.TemporaryHoldMilliseconds, 100, 2000);
            settings.HoleRatio = Math.Clamp(settings.HoleRatio, 0.1, 0.65);
            settings.Bookmarks ??= [];
            settings.History ??= [];
            settings.OnlineVisionProfiles ??= [];
            // Preserve the recently used selection from older per-page profiles; keep the old table intact.
            static bool Usable(OnlineVisionProfile? profile) => profile != null
                && profile.Region is { IsValid: true } && double.IsFinite(profile.NorthAngle);
            settings.OnlineVisionCalibration ??= settings.History.OrderByDescending(video => video.UpdatedAt)
                .Select(video => settings.OnlineVisionProfiles.GetValueOrDefault(video.Url)).FirstOrDefault(Usable)
                ?? settings.OnlineVisionProfiles.Values.LastOrDefault(Usable);
            settings.Hotkeys ??= AppSettings.DefaultHotkeys();
            foreach (var pair in AppSettings.DefaultHotkeys())
            {
                if (settings.Hotkeys.ContainsKey(pair.Key)) continue;
                var binding = pair.Value;
                if (settings.Hotkeys.Values.Contains(binding, StringComparer.OrdinalIgnoreCase))
                    binding = Enumerable.Range(1, 11).Where(n => n != 10).Select(n => "Ctrl+Alt+Shift+F" + n)
                        .FirstOrDefault(value => !settings.Hotkeys.Values.Contains(value, StringComparer.OrdinalIgnoreCase)) ?? binding;
                settings.Hotkeys[pair.Key] = binding;
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadWarning = "设置无法读取，已使用默认值。原设置文件保留在 " + _path;
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        // Keep the damaged original available if a previous load failed.
        if (LoadWarning != null && File.Exists(_path))
        {
            File.Copy(_path, _path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false);
            LoadWarning = null;
        }
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, _options));
        File.Move(temporary, _path, true);
    }
}
