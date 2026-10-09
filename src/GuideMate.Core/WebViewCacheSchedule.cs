namespace GuideMate.Core;

public static class WebViewCacheSchedule
{
    public static void Configure(AppSettings settings, bool enabled, int days, DateTimeOffset now)
    {
        var changed = settings.AutoCleanWebViewCache != enabled || settings.WebViewCacheCleanupDays != days;
        settings.AutoCleanWebViewCache = enabled;
        settings.WebViewCacheCleanupDays = days;
        if (!enabled) settings.NextWebViewCacheCleanupUtc = null;
        else if (changed || settings.NextWebViewCacheCleanupUtc == null)
            settings.NextWebViewCacheCleanupUtc = now.AddDays(days);
    }

    public static bool IsDue(AppSettings settings, DateTimeOffset now) => settings.AutoCleanWebViewCache
        && settings.NextWebViewCacheCleanupUtc is { } next && now >= next;

    public static void Completed(AppSettings settings, DateTimeOffset now)
    {
        settings.LastWebViewCacheCleanupUtc = now;
        settings.NextWebViewCacheCleanupUtc = settings.AutoCleanWebViewCache
            ? now.AddDays(settings.WebViewCacheCleanupDays) : null;
    }
}
