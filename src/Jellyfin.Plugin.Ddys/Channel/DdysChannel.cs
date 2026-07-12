using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ddys.Api;
using Jellyfin.Plugin.Ddys.Configuration;
using Jellyfin.Plugin.Ddys.Providers;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Ddys.Channel;

public sealed partial class DdysChannel : IChannel
{
    private static readonly IReadOnlyList<CategoryDefinition> Categories =
    [
        new("movie", "电影"),
        new("series", "剧集"),
        new("anime", "动漫"),
        new("variety", "综艺"),
        new("documentary", "纪录片")
    ];

    public string Name => Plugin.PluginName;

    public string Description => Plugin.PluginDescription;

    public string DataVersion => "0.1.0";

    public string HomePageUrl => "https://ddys.io";

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures()
    {
        return new InternalChannelFeatures
        {
            MediaTypes = [ChannelMediaType.Video],
            ContentTypes = [ChannelMediaContentType.Movie, ChannelMediaContentType.Episode],
            MaxPageSize = 80,
            SupportsSortOrderToggle = false,
            SupportsContentDownloading = false,
            AutoRefreshLevels = 1
        };
    }

    public bool IsEnabledFor(string userId)
    {
        return true;
    }

    public Task<DynamicImageResponse?> GetChannelImage(ImageType type, CancellationToken cancellationToken)
    {
        if (type is ImageType.Primary or ImageType.Thumb)
        {
            var stream = typeof(Plugin).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".ThumbImage.png");
            if (stream is null)
            {
                return Task.FromResult<DynamicImageResponse?>(null);
            }

            return Task.FromResult<DynamicImageResponse?>(new DynamicImageResponse
            {
                Format = ImageFormat.Png,
                Stream = stream,
                HasImage = true
            });
        }

        return Task.FromResult<DynamicImageResponse?>(null);
    }

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        if (query is null)
        {
            return GetRootItems();
        }

        var node = DdysNodeId.Parse(query.FolderId);
        if (string.IsNullOrWhiteSpace(node.Kind))
        {
            return GetRootItems();
        }

        try
        {
            return node.Kind switch
            {
                "latest" => await GetLatestItems(query, cancellationToken).ConfigureAwait(false),
                "hot" => await GetHotItems(query, cancellationToken).ConfigureAwait(false),
                "category" => await GetCategoryItems(node.Value, query, cancellationToken).ConfigureAwait(false),
                "search" => await GetSearchItems(node.Value, query, cancellationToken).ConfigureAwait(false),
                "movie" => await GetMovieItems(node.Value, cancellationToken).ConfigureAwait(false),
                "diagnostics" => await GetDiagnosticsItems(cancellationToken).ConfigureAwait(false),
                _ => EmptyResult()
            };
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return ErrorResult("DDYS API 请求超时。");
        }
        catch (Exception ex)
        {
            return ErrorResult(ex.Message);
        }
    }

    public IEnumerable<ImageType> GetSupportedChannelImages()
    {
        return [ImageType.Primary, ImageType.Thumb];
    }

    private ChannelItemResult GetRootItems()
    {
        var items = new List<ChannelItemInfo>
        {
            CreateFolder("latest", string.Empty, "最新更新", "DDYS 最新更新内容。"),
            CreateFolder("hot", string.Empty, "热门内容", "DDYS 热门内容。")
        };

        foreach (var category in Categories)
        {
            items.Add(CreateFolder("category", category.Id, category.Name, "按 " + category.Name + " 浏览 DDYS 内容。"));
        }

        foreach (var keyword in ParseSavedSearches(Options.SavedSearches))
        {
            items.Add(CreateFolder("search", keyword, "搜索: " + keyword, "常用搜索词。"));
        }

        items.Add(CreateFolder("diagnostics", string.Empty, "插件诊断", "检查 DDYS API、缓存和配置状态。"));
        return Result(items, items.Count);
    }

    private async Task<ChannelItemResult> GetLatestItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var paging = GetPaging(query, Options.HomeLimit);
        var movies = await Client.LatestAsync(paging.StartIndex + paging.Limit, cancellationToken).ConfigureAwait(false);
        var page = movies.Skip(paging.StartIndex).Take(paging.Limit).ToList();
        return Result(page.Select(movie => CreateMovieFolder(movie)).ToList(), movies.Count);
    }

    private async Task<ChannelItemResult> GetHotItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var paging = GetPaging(query, Options.HomeLimit);
        var movies = await Client.HotAsync(paging.StartIndex + paging.Limit, cancellationToken).ConfigureAwait(false);
        var page = movies.Skip(paging.StartIndex).Take(paging.Limit).ToList();
        return Result(page.Select(movie => CreateMovieFolder(movie)).ToList(), movies.Count);
    }

    private async Task<ChannelItemResult> GetCategoryItems(string category, InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var paging = GetPaging(query, Options.PageSize);
        var result = await Client.MoviesAsync(category, paging.Page, paging.Limit, cancellationToken).ConfigureAwait(false);
        return Result(result.Data.Select(movie => CreateMovieFolder(movie)).ToList(), result.Total);
    }

    private async Task<ChannelItemResult> GetSearchItems(string keyword, InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return EmptyResult();
        }

        var paging = GetPaging(query, Options.PageSize);
        var result = await Client.SearchAsync(keyword, paging.Page, paging.Limit, cancellationToken).ConfigureAwait(false);
        return Result(result.Data.Select(movie => CreateMovieFolder(movie)).ToList(), result.Total);
    }

    private async Task<ChannelItemResult> GetMovieItems(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return EmptyResult();
        }

        var bundle = await Client.DetailBundleAsync(slug, cancellationToken).ConfigureAwait(false);
        var items = new List<ChannelItemInfo>();
        var movie = string.IsNullOrWhiteSpace(bundle.Movie.Slug) ? new DdysMovie { Slug = slug, Title = slug } : bundle.Movie;

        foreach (var group in bundle.SourceGroups)
        {
            foreach (var resource in group.Items)
            {
                if (resource.IsDirect && Options.EnableDirectPlay)
                {
                    items.Add(CreatePlayableItem(movie, group, resource));
                }
                else if (Options.ShowExternalResources)
                {
                    items.Add(CreateExternalResourceItem(movie, group, resource));
                }
            }
        }

        if (Options.IncludeRelatedItems)
        {
            foreach (var related in bundle.Related)
            {
                items.Add(CreateMovieFolder(related, "相关: "));
            }
        }

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(movie.Url))
        {
            items.Add(CreateExternalResourceItem(movie, new DdysSourceGroup { Name = "源站" }, new DdysResource { Name = "打开源站", Url = movie.Url }));
        }

        return Result(items, items.Count);
    }

    private async Task<ChannelItemResult> GetDiagnosticsItems(CancellationToken cancellationToken)
    {
        var diagnostics = await Client.DiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var overview = string.Join("\n", diagnostics.Select(pair => pair.Key + ": " + Convert.ToString(pair.Value, CultureInfo.InvariantCulture)));
        var item = new ChannelItemInfo
        {
            Id = DdysNodeId.Create("diagnostics-result", "ok"),
            Name = "DDYS API 正常",
            Overview = overview,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            DateModified = DateTime.UtcNow
        };

        return Result([item], 1);
    }

    private PluginConfiguration Options => Plugin.Instance?.Options ?? new PluginConfiguration();

    private DdysClient Client => new(Options);

    private static ChannelItemInfo CreateFolder(string kind, string value, string name, string overview)
    {
        return new ChannelItemInfo
        {
            Id = DdysNodeId.Create(kind, value),
            Name = name,
            Overview = overview,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            DateModified = DateTime.UtcNow
        };
    }

    private static ChannelItemInfo CreateMovieFolder(DdysMovie movie, string prefix = "")
    {
        return new ChannelItemInfo
        {
            Id = DdysNodeId.Create("movie", movie.Slug),
            Name = prefix + MovieLabel(movie),
            OriginalTitle = movie.Title,
            Overview = BuildMovieOverview(movie),
            ImageUrl = movie.Poster,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            ContentType = ResolveContentType(movie),
            MediaType = ChannelMediaType.Video,
            DateCreated = ParseDate(movie.Date),
            DateModified = DateTime.UtcNow,
            ProductionYear = ParseYear(movie.Year),
            Genres = movie.Tags,
            Tags = movie.Tags,
            CommunityRating = movie.Score,
            ProviderIds = ProviderIds(movie.Slug),
            HomePageUrl = movie.Url
        };
    }

    private static ChannelItemInfo CreatePlayableItem(DdysMovie movie, DdysSourceGroup group, DdysResource resource)
    {
        var id = movie.Slug + "|" + group.Name + "|" + resource.Name + "|" + resource.Url;
        return new ChannelItemInfo
        {
            Id = DdysNodeId.Create("resource", id),
            Name = group.Name + " - " + resource.Name,
            OriginalTitle = movie.Title,
            Overview = BuildMovieOverview(movie),
            ImageUrl = movie.Poster,
            Type = ChannelItemType.Media,
            ContentType = ResolveContentType(movie),
            MediaType = ChannelMediaType.Video,
            DateCreated = ParseDate(movie.Date),
            DateModified = DateTime.UtcNow,
            ProductionYear = ParseYear(movie.Year),
            Genres = movie.Tags,
            Tags = movie.Tags,
            CommunityRating = movie.Score,
            ProviderIds = ProviderIds(movie.Slug),
            HomePageUrl = movie.Url,
            MediaSources =
            [
                new MediaSourceInfo
                {
                    Id = DdysNodeId.Create("source", id),
                    Name = resource.Name,
                    Path = resource.Url,
                    Protocol = ResolveProtocol(resource.Url),
                    Type = MediaSourceType.Default,
                    IsRemote = true,
                    SupportsDirectPlay = true,
                    SupportsDirectStream = true,
                    SupportsTranscoding = false,
                    Container = GuessContainer(resource.Url),
                    RequiredHttpHeaders = resource.Headers
                }
            ]
        };
    }

    private static ChannelItemInfo CreateExternalResourceItem(DdysMovie movie, DdysSourceGroup group, DdysResource resource)
    {
        var overview = new StringBuilder();
        overview.AppendLine(BuildMovieOverview(movie));
        overview.AppendLine();
        overview.AppendLine(group.Name + " - " + resource.Name);
        overview.AppendLine(resource.Url);

        return new ChannelItemInfo
        {
            Id = DdysNodeId.Create("external", movie.Slug + "|" + resource.Url),
            Name = group.Name + " - " + resource.Name,
            OriginalTitle = movie.Title,
            Overview = overview.ToString().Trim(),
            ImageUrl = movie.Poster,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            ContentType = ResolveContentType(movie),
            MediaType = ChannelMediaType.Video,
            DateCreated = ParseDate(movie.Date),
            DateModified = DateTime.UtcNow,
            ProductionYear = ParseYear(movie.Year),
            Genres = movie.Tags,
            Tags = movie.Tags,
            ProviderIds = ProviderIds(movie.Slug),
            HomePageUrl = movie.Url
        };
    }

    private static Dictionary<string, string> ProviderIds(string slug)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            ids[DdysExternalId.ProviderKey] = slug;
        }

        return ids;
    }

    private static string BuildMovieOverview(DdysMovie movie)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(movie.Overview))
        {
            lines.Add(movie.Overview);
        }

        AddDetail(lines, "年份", movie.Year);
        AddDetail(lines, "地区", movie.Region);
        AddDetail(lines, "类型", movie.TypeName);
        AddDetail(lines, "导演", movie.Director);
        AddDetail(lines, "演员", movie.Actor);
        AddDetail(lines, "源站", movie.Url);
        return string.Join("\n", lines);
    }

    private static void AddDetail(List<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(label + ": " + value);
        }
    }

    private static string MovieLabel(DdysMovie movie)
    {
        var suffix = string.IsNullOrWhiteSpace(movie.Remarks) ? movie.Year : movie.Remarks;
        return string.IsNullOrWhiteSpace(suffix) ? movie.Title : movie.Title + " [" + suffix + "]";
    }

    private static ChannelMediaContentType ResolveContentType(DdysMovie movie)
    {
        var text = ((movie.TypeName ?? string.Empty) + " " + (movie.Remarks ?? string.Empty)).ToLowerInvariant();
        if (text.Contains("series", StringComparison.Ordinal)
            || text.Contains("episode", StringComparison.Ordinal)
            || text.Contains("anime", StringComparison.Ordinal)
            || text.Contains("variety", StringComparison.Ordinal)
            || text.Contains('剧', StringComparison.Ordinal)
            || text.Contains('番', StringComparison.Ordinal)
            || text.Contains("综艺", StringComparison.Ordinal))
        {
            return ChannelMediaContentType.Episode;
        }

        return ChannelMediaContentType.Movie;
    }

    private static MediaProtocol ResolveProtocol(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return MediaProtocol.Http;
        }

        if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProtocol.Ftp;
        }

        if (url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProtocol.Rtmp;
        }

        if (url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProtocol.Rtsp;
        }

        return MediaProtocol.Http;
    }

    private static string GuessContainer(string url)
    {
        var path = (url ?? string.Empty).Split(['?', '#'], 2)[0];
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var container = extension.TrimStart('.').ToLowerInvariant();
        return container switch
        {
            "m3u8" => "hls",
            "mpd" => "dash",
            _ => container
        };
    }

    private static DateTime? ParseDate(string date)
    {
        return DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value.ToUniversalTime() : null;
    }

    private static int? ParseYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = YearRegex().Match(value);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static IReadOnlyList<string> ParseSavedSearches(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(['\r', '\n', ',', '，', ';', '；', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }

    private static Paging GetPaging(InternalChannelItemQuery query, int fallbackLimit)
    {
        var limit = Clamp(query.Limit ?? fallbackLimit, fallbackLimit, 1, 80);
        var startIndex = Math.Max(0, query.StartIndex ?? 0);
        return new Paging
        {
            Limit = limit,
            StartIndex = startIndex,
            Page = (startIndex / limit) + 1
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

    private static ChannelItemResult Result(IReadOnlyList<ChannelItemInfo> items, int? total)
    {
        return new ChannelItemResult
        {
            Items = items,
            TotalRecordCount = total ?? items.Count
        };
    }

    private static ChannelItemResult EmptyResult()
    {
        return Result([], 0);
    }

    private static ChannelItemResult ErrorResult(string message)
    {
        var item = new ChannelItemInfo
        {
            Id = DdysNodeId.Create("error", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
            Name = "DDYS API 暂不可用",
            Overview = string.IsNullOrWhiteSpace(message) ? "请稍后重试，或检查插件设置中的 API Base、API Key 和网络连接。" : message,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            DateModified = DateTime.UtcNow
        };

        return Result([item], 1);
    }

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex YearRegex();

    private sealed class CategoryDefinition(string id, string name)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;
    }

    private sealed class Paging
    {
        public int Limit { get; set; }

        public int StartIndex { get; set; }

        public int Page { get; set; }
    }
}
