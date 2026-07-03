namespace SraRms.Api.Contracts;

/// <summary>Pagination metadata (OpenAPI PageMeta).</summary>
public record PageMeta(int Page, int PageSize, int TotalItems, int TotalPages);

/// <summary>A page of results: { items, meta } (OpenAPI *Page schemas).</summary>
public record Page<T>(IReadOnlyList<T> Items, PageMeta Meta)
{
    public static Page<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        var totalPages = pageSize > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 0;
        return new Page<T>(items, new PageMeta(page, pageSize, totalItems, totalPages));
    }
}

/// <summary>Common list query parameters: q, page, pageSize, sort.</summary>
public class ListQuery
{
    private const int MaxPageSize = 200;
    private int _page = 1;
    private int _pageSize = 25;

    /// <summary>Free-text search term.</summary>
    public string? Q { get; set; }

    /// <summary>1-based page number.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Items per page (max 200).</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : value > MaxPageSize ? MaxPageSize : value;
    }

    /// <summary>Sort expression, e.g. "name" or "-startDate" (prefix - for descending).</summary>
    public string? Sort { get; set; }

    public int Skip => (Page - 1) * PageSize;

    /// <summary>Parses the sort expression into (field, descending).</summary>
    public (string Field, bool Desc)? ParseSort()
    {
        if (string.IsNullOrWhiteSpace(Sort)) return null;
        var s = Sort.Trim();
        var desc = s.StartsWith('-');
        if (desc || s.StartsWith('+')) s = s[1..];
        return (s, desc);
    }
}
