using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ddys.Configuration;

namespace Jellyfin.Plugin.Ddys.Api;

internal sealed partial class DdysClient
{
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    private readonly PluginConfiguration options;

    public DdysClient(PluginConfiguration? options)
    {
        this.options = options ?? new PluginConfiguration();
        this.options.Normalize();
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    public async Task<List<DdysMovie>> LatestAsync(int? limit, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync("/latest", new Dictionary<string, string>
        {
            ["limit"] = Convert.ToString(Clamp(limit ?? options.HomeLimit, options.HomeLimit, 1, 80), CultureInfo.InvariantCulture)
        }, cancellationToken).ConfigureAwait(false);

        return ReadMovieList(root);
    }

    public async Task<List<DdysMovie>> HotAsync(int? limit, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync("/hot", new Dictionary<string, string>
        {
            ["limit"] = Convert.ToString(Clamp(limit ?? options.HomeLimit, options.HomeLimit, 1, 80), CultureInfo.InvariantCulture)
        }, cancellationToken).ConfigureAwait(false);

        return ReadMovieList(root);
    }

    public async Task<DdysPagedResult> MoviesAsync(string? mediaType, int page, int perPage, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync("/movies", new Dictionary<string, string>
        {
            ["type"] = mediaType ?? "movie",
            ["page"] = Convert.ToString(Math.Max(1, page), CultureInfo.InvariantCulture),
            ["per_page"] = Convert.ToString(Clamp(perPage, options.PageSize, 1, 80), CultureInfo.InvariantCulture)
        }, cancellationToken).ConfigureAwait(false);

        return ReadPagedMovies(root);
    }

    public async Task<DdysPagedResult> SearchAsync(string? query, int page, int perPage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DdysPagedResult
            {
                Page = Math.Max(1, page),
                PerPage = Clamp(perPage, options.PageSize, 1, 80),
                TotalPages = 1
            };
        }

        var root = await GetJsonAsync("/search", new Dictionary<string, string>
        {
            ["q"] = query,
            ["page"] = Convert.ToString(Math.Max(1, page), CultureInfo.InvariantCulture),
            ["per_page"] = Convert.ToString(Clamp(perPage, options.PageSize, 1, 80), CultureInfo.InvariantCulture)
        }, cancellationToken).ConfigureAwait(false);

        return ReadPagedMovies(root);
    }

    public async Task<DdysDetailBundle> DetailBundleAsync(string? slug, CancellationToken cancellationToken)
    {
        var encodedSlug = Uri.EscapeDataString(slug ?? string.Empty);
        var detailRoot = await GetJsonAsync("/movies/" + encodedSlug, null, cancellationToken).ConfigureAwait(false);
        var sourcesRoot = await GetJsonOrFallbackAsync("/movies/" + encodedSlug + "/sources", cancellationToken).ConfigureAwait(false);
        var relatedRoot = await GetJsonOrFallbackAsync("/movies/" + encodedSlug + "/related", cancellationToken).ConfigureAwait(false);

        return new DdysDetailBundle
        {
            Movie = ReadMovie(UnwrapData(detailRoot)),
            SourceGroups = ReadSourceGroups(UnwrapData(sourcesRoot)),
            Related = ReadRelated(UnwrapData(relatedRoot))
        };
    }

    public async Task<Dictionary<string, object>> DiagnosticsAsync(CancellationToken cancellationToken)
    {
        var latest = await LatestAsync(1, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object>
        {
            ["ok"] = true,
            ["apiBase"] = options.ApiBase,
            ["siteBase"] = options.SiteBase,
            ["apiKeyConfigured"] = !string.IsNullOrWhiteSpace(options.ApiKey),
            ["sampleCount"] = latest.Count,
            ["cacheEnabled"] = options.EnableCache,
            ["cacheMinutes"] = options.CacheMinutes
        };
    }

    private async Task<JsonElement> GetJsonOrFallbackAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetJsonAsync(path, null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }

    private async Task<JsonElement> GetJsonAsync(string path, IDictionary<string, string>? query, CancellationToken cancellationToken)
    {
        var url = BuildUrl(options.ApiBase, path, query);
        var cacheKey = url + "|auth:" + (!string.IsNullOrWhiteSpace(options.ApiKey) ? "1" : "0");

        if (options.EnableCache && Cache.TryGetValue(cacheKey, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return entry.Element.Clone();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + options.ApiKey);
        }

        using var response = await SharedHttpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("DDYS API returned non-JSON response: HTTP " + (int)response.StatusCode, ex);
        }

        using (document)
        {
            var root = document.RootElement.Clone();
            if (!response.IsSuccessStatusCode || IsEnvelopeFailure(root))
            {
                throw new InvalidOperationException(ReadMessage(root) ?? ("DDYS API HTTP " + (int)response.StatusCode));
            }

            if (options.EnableCache)
            {
                Cache[cacheKey] = new CacheEntry
                {
                    Element = root.Clone(),
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(options.CacheMinutes)
                };
            }

            return root;
        }
    }

    private List<DdysMovie> ReadMovieList(JsonElement root)
    {
        var data = UnwrapData(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(ReadMovie)
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug) && !string.IsNullOrWhiteSpace(item.Title))
            .ToList();
    }

    private DdysPagedResult ReadPagedMovies(JsonElement root)
    {
        var data = UnwrapData(root);
        var movies = data.ValueKind == JsonValueKind.Array ? ReadMovieList(root) : [];
        var meta = GetProperty(root, "meta");

        return new DdysPagedResult
        {
            Data = movies,
            Total = ReadInt(meta, movies.Count, "total", "count"),
            Page = ReadInt(meta, 1, "page"),
            PerPage = ReadInt(meta, movies.Count == 0 ? options.PageSize : movies.Count, "per_page", "perPage", "limit"),
            TotalPages = ReadInt(meta, 1, "total_pages", "last_page", "totalPages")
        };
    }

    private DdysMovie ReadMovie(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new DdysMovie();
        }

        var slug = FirstString(element, "slug", "vod_id", "id", "key", "code", "video_id");
        var title = FirstString(element, "title", "name", "vod_name", "title_cn");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = slug;
        }

        var category = ToStringList(GetFirst(element, "category", "vod_class", "genre", "genres", "tags"));

        return new DdysMovie
        {
            Slug = slug,
            Title = title,
            Poster = AbsoluteUrl(FirstString(element, "poster", "pic", "cover", "vod_pic", "image", "thumbnail"), options.SiteBase),
            Year = FirstString(element, "year", "release_year", "vod_year", "date", "release_date"),
            Region = JoinValues(GetFirst(element, "region", "area", "vod_area")),
            TypeName = JoinValues(GetFirst(element, "type_name", "type", "category", "vod_class")),
            Actor = JoinValues(GetFirst(element, "actor", "actors", "cast", "vod_actor")),
            Director = JoinValues(GetFirst(element, "director", "directors", "vod_director")),
            Overview = FirstString(element, "intro", "description", "summary", "content", "vod_content"),
            Remarks = JoinValues(GetFirst(element, "remarks", "vod_remarks", "episode", "episode_text", "score", "year")),
            Url = AbsoluteUrl(FirstNonEmpty(FirstString(element, "url", "link", "href"), string.IsNullOrWhiteSpace(slug) ? string.Empty : "/movie/" + slug), options.SiteBase),
            Date = FirstString(element, "date", "pubdate", "updated_at", "update_time", "vod_time", "created_at"),
            Score = ReadFloat(GetFirst(element, "score", "rating", "rate", "vod_score")),
            Tags = category
        };
    }

    private static List<DdysSourceGroup> ReadSourceGroups(JsonElement data)
    {
        var groups = new List<DdysSourceGroup>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            AddGroup(groups, "在线播放", data.EnumerateArray());
            return groups;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return groups;
        }

        AddGroup(groups, "在线播放", CollectArrays(data, "online", "play", "playlist", "episodes"));
        AddGroup(groups, "下载资源", CollectArrays(data, "download", "downloads"));
        AddGroup(groups, "网盘资源", CollectArrays(data, "cloud", "netdisk", "drive"));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "online",
            "play",
            "playlist",
            "episodes",
            "download",
            "downloads",
            "cloud",
            "netdisk",
            "drive"
        };

        foreach (var property in data.EnumerateObject())
        {
            if (!used.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Array)
            {
                AddGroup(groups, ReadableGroupName(property.Name), property.Value.EnumerateArray());
            }
        }

        return groups.Where(group => group.Items.Count > 0).ToList();
    }

    private List<DdysMovie> ReadRelated(JsonElement data)
    {
        var movies = new List<DdysMovie>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            movies.AddRange(data.EnumerateArray().Select(ReadMovie));
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            movies.AddRange(CollectArrays(data, "series", "related", "items").Select(ReadMovie));
        }

        return movies
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug) && !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => item.Slug)
            .Select(group => group.First())
            .Take(12)
            .ToList();
    }

    private static void AddGroup(List<DdysSourceGroup> groups, string name, IEnumerable<JsonElement> elements)
    {
        var items = elements.Select((element, index) => ReadResource(element, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .ToList();

        if (items.Count > 0)
        {
            groups.Add(new DdysSourceGroup { Name = name, Items = items });
        }
    }

    private static DdysResource ReadResource(JsonElement element, int index = 0)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? string.Empty;
            return new DdysResource
            {
                Name = "资源" + (index + 1),
                Url = text,
                IsDirect = IsDirectMedia(text)
            };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new DdysResource();
        }

        var url = FirstString(element, "url", "link", "href", "play_url", "download_url", "magnet", "ed2k");
        var label = JoinValues(GetFirst(element, "name", "title", "label", "episode", "quality", "format"));
        if (string.IsNullOrWhiteSpace(label))
        {
            label = "资源" + (index + 1);
        }

        var code = FirstString(element, "extract_code", "code", "password", "passcode");
        if (!string.IsNullOrWhiteSpace(code))
        {
            label += " 提取码 " + code;
        }

        return new DdysResource
        {
            Name = label,
            Url = url,
            IsDirect = IsDirectMedia(url),
            Headers = ReadHeaders(GetFirst(element, "headers", "header"))
        };
    }

    private static IEnumerable<JsonElement> CollectArrays(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetProperty(element, key);
            if (value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in value.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static JsonElement UnwrapData(JsonElement root)
    {
        var data = GetProperty(root, "data");
        return data.ValueKind == JsonValueKind.Undefined ? root : data;
    }

    private static bool IsEnvelopeFailure(JsonElement root)
    {
        var success = GetProperty(root, "success");
        return success.ValueKind == JsonValueKind.False;
    }

    private static string? ReadMessage(JsonElement root)
    {
        return FirstString(root, "message", "error", "msg");
    }

    private static JsonElement GetFirst(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetProperty(element, name);
            if (value.ValueKind != JsonValueKind.Undefined && value.ValueKind != JsonValueKind.Null)
            {
                return value;
            }
        }

        return default;
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (element.TryGetProperty(name, out var value))
        {
            return value;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return default;
    }

    private static string FirstString(JsonElement element, params string[] names)
    {
        return ElementText(GetFirst(element, names));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ElementText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => JoinValues(element),
            _ => string.Empty
        };
    }

    private static string JoinValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return string.Join(" / ", element.EnumerateArray().Select(ElementText).Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return ElementText(element);
    }

    private static List<string> ToStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(ElementText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var text = ElementText(element);
        return string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(['/', ',', '，', '|', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static float? ReadFloat(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var number))
        {
            return number;
        }

        return float.TryParse(ElementText(element), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int ReadInt(JsonElement element, int fallback, params string[] keys)
    {
        var value = GetFirst(element, keys);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(ElementText(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement element)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return headers;
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = ElementText(property.Value);
            if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
            {
                headers[property.Name] = value;
            }
        }

        return headers;
    }

    private static bool IsDirectMedia(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && DirectMediaRegex().IsMatch(url);
    }

    private static string AbsoluteUrl(string value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + text;
        }

        if (text.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizeBaseUrl(baseUrl) + text;
        }

        return text;
    }

    private static string BuildUrl(string baseUrl, string path, IDictionary<string, string>? query)
    {
        var builder = new StringBuilder();
        builder.Append(NormalizeBaseUrl(baseUrl));
        builder.Append(path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);

        if (query is { Count: > 0 })
        {
            var pairs = query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
            var suffix = string.Join("&", pairs);
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                builder.Append('?').Append(suffix);
            }
        }

        return builder.ToString();
    }

    private static string NormalizeBaseUrl(string value)
    {
        return (string.IsNullOrWhiteSpace(value) ? "https://ddys.io/api/v1" : value.Trim()).TrimEnd('/');
    }

    private static string ReadableGroupName(string key)
    {
        return (key ?? string.Empty).ToLowerInvariant() switch
        {
            "quark" => "夸克资源",
            "aliyun" => "阿里云盘",
            "baidu" => "百度网盘",
            "magnet" => "磁力资源",
            "other" => "其他资源",
            _ => string.IsNullOrWhiteSpace(key) ? "其他资源" : key
        };
    }

    private static int Clamp(int value, int fallback, int min, int max)
    {
        if (value == 0)
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    [GeneratedRegex(@"\.(m3u8|mp4|m4v|mkv|mov|flv|avi|ts|webm|mpd)(\?|#|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DirectMediaRegex();

    private sealed class CacheEntry
    {
        public JsonElement Element { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
    }
}
