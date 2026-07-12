using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Ddys.Providers;

public sealed class DdysExternalId : IExternalId
{
    public const string ProviderKey = "DDYS";

    public string ProviderName => "DDYS";

    public string Key => ProviderKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public string UrlFormatString => "https://ddys.io/movie/{0}";

    public bool Supports(IHasProviderIds item)
    {
        return true;
    }
}
