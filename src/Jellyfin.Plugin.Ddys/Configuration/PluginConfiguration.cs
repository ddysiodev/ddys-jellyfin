using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Ddys.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string ApiBase { get; set; } = "https://ddys.io/api/v1";

    public string SiteBase { get; set; } = "https://ddys.io";

    public string ApiKey { get; set; } = string.Empty;

    public int HomeLimit { get; set; } = 24;

    public int PageSize { get; set; } = 24;

    public int TimeoutSeconds { get; set; } = 12;

    public bool EnableCache { get; set; } = true;

    public int CacheMinutes { get; set; } = 10;

    public bool EnableDirectPlay { get; set; } = true;

    public bool ShowExternalResources { get; set; } = true;

    public bool IncludeRelatedItems { get; set; } = true;

    public string SavedSearches { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "ddys-jellyfin/0.1.0";

    public void Normalize()
    {
        ApiBase = NormalizeBaseUrl(ApiBase, "https://ddys.io/api/v1");
        SiteBase = NormalizeBaseUrl(SiteBase, "https://ddys.io");
        ApiKey = (ApiKey ?? string.Empty).Trim();
        SavedSearches = (SavedSearches ?? string.Empty).Trim();
        UserAgent = string.IsNullOrWhiteSpace(UserAgent) ? "ddys-jellyfin/0.1.0" : UserAgent.Trim();
        HomeLimit = Clamp(HomeLimit, 24, 1, 80);
        PageSize = Clamp(PageSize, 24, 1, 80);
        TimeoutSeconds = Clamp(TimeoutSeconds, 12, 3, 60);
        CacheMinutes = Clamp(CacheMinutes, 10, 1, 120);
    }

    public bool IsValid()
    {
        return IsHttpUrl(ApiBase)
               && IsHttpUrl(SiteBase)
               && HomeLimit >= 1
               && HomeLimit <= 80
               && PageSize >= 1
               && PageSize <= 80
               && TimeoutSeconds >= 3
               && TimeoutSeconds <= 60
               && CacheMinutes >= 1
               && CacheMinutes <= 120;
    }

    private static int Clamp(int value, int fallback, int min, int max)
    {
        if (value == 0)
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    private static string NormalizeBaseUrl(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.TrimEnd('/');
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
