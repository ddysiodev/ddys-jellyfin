using System.Collections.Generic;

namespace Jellyfin.Plugin.Ddys.Api;

internal sealed class DdysMovie
{
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Poster { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Director { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public float? Score { get; set; }

    public List<string> Tags { get; set; } = [];
}

internal sealed class DdysResource
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool IsDirect { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();
}

internal sealed class DdysSourceGroup
{
    public string Name { get; set; } = string.Empty;

    public List<DdysResource> Items { get; set; } = [];
}

internal sealed class DdysPagedResult
{
    public List<DdysMovie> Data { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PerPage { get; set; }

    public int TotalPages { get; set; }
}

internal sealed class DdysDetailBundle
{
    public DdysMovie Movie { get; set; } = new();

    public List<DdysSourceGroup> SourceGroups { get; set; } = [];

    public List<DdysMovie> Related { get; set; } = [];
}
