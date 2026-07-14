using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Ddys.Api;
using Jellyfin.Plugin.Ddys.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Ddys.Controllers;

[ApiController]
[Authorize]
[Route("DDYS")]
public sealed class DdysController : ControllerBase
{
    [HttpGet("Status")]
    public async Task<ActionResult<DdysStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var diagnostics = await Client.DiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        return new DdysStatusResponse
        {
            Ok = true,
            Values = diagnostics
        };
    }

    [HttpGet("Search")]
    public async Task<ActionResult<DdysSearchResponse>> Search([FromQuery] string? query, [FromQuery] int page = 1, [FromQuery] int perPage = 24, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DdysSearchResponse
            {
                Page = 1,
                PerPage = Options.PageSize,
                TotalPages = 1
            };
        }

        var result = await Client.SearchAsync(query, page <= 0 ? 1 : page, perPage <= 0 ? Options.PageSize : perPage, cancellationToken).ConfigureAwait(false);
        return DdysSearchResponse.From(result);
    }

    [HttpGet("Movies/{slug}")]
    public async Task<ActionResult<DdysMovieResponse>> GetMovie([FromRoute] string? slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return new DdysMovieResponse();
        }

        var bundle = await Client.DetailBundleAsync(slug, cancellationToken).ConfigureAwait(false);
        return DdysMovieResponse.From(bundle);
    }

    [HttpPost("Cache/Clear")]
    public ActionResult<DdysStatusResponse> ClearCache()
    {
        DdysClient.ClearCache();
        return new DdysStatusResponse
        {
            Ok = true,
            Values = new Dictionary<string, object>
            {
                ["cacheCleared"] = true
            }
        };
    }

    private PluginConfiguration Options => Plugin.Instance?.Options ?? new PluginConfiguration();

    private DdysClient Client => new(Options);
}

public sealed class DdysStatusResponse
{
    public bool Ok { get; set; }

    public Dictionary<string, object> Values { get; set; } = new();
}

public sealed class DdysSearchResponse
{
    public List<DdysMovieDto> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PerPage { get; set; }

    public int TotalPages { get; set; }

    internal static DdysSearchResponse From(DdysPagedResult result)
    {
        return new DdysSearchResponse
        {
            Items = result.Data.Select(DdysMovieDto.From).ToList(),
            Total = result.Total,
            Page = result.Page,
            PerPage = result.PerPage,
            TotalPages = result.TotalPages
        };
    }
}

public sealed class DdysMovieResponse
{
    public DdysMovieDto Movie { get; set; } = new();

    public List<DdysSourceGroupDto> Sources { get; set; } = [];

    public List<DdysMovieDto> Related { get; set; } = [];

    internal static DdysMovieResponse From(DdysDetailBundle bundle)
    {
        return new DdysMovieResponse
        {
            Movie = DdysMovieDto.From(bundle.Movie),
            Sources = bundle.SourceGroups.Select(DdysSourceGroupDto.From).ToList(),
            Related = bundle.Related.Select(DdysMovieDto.From).ToList()
        };
    }
}

public sealed class DdysMovieDto
{
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Poster { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    internal static DdysMovieDto From(DdysMovie movie)
    {
        return new DdysMovieDto
        {
            Slug = movie.Slug,
            Title = movie.Title,
            Poster = movie.Poster,
            Year = movie.Year,
            Region = movie.Region,
            TypeName = movie.TypeName,
            Remarks = movie.Remarks,
            Url = movie.Url
        };
    }
}

public sealed class DdysSourceGroupDto
{
    public string Name { get; set; } = string.Empty;

    public List<DdysResourceDto> Items { get; set; } = [];

    internal static DdysSourceGroupDto From(DdysSourceGroup group)
    {
        return new DdysSourceGroupDto
        {
            Name = group.Name,
            Items = group.Items.Select(DdysResourceDto.From).ToList()
        };
    }
}

public sealed class DdysResourceDto
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool IsDirect { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();

    internal static DdysResourceDto From(DdysResource resource)
    {
        return new DdysResourceDto
        {
            Name = resource.Name,
            Url = resource.Url,
            IsDirect = resource.IsDirect,
            Headers = resource.Headers
        };
    }
}
