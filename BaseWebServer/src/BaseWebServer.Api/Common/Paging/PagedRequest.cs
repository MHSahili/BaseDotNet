using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BaseWebServer.Api.Common.Paging;

public class PagedRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public string? Sort { get; init; }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResponse<T>> ToPagedAsync<T>(
        this IQueryable<T> query, PagedRequest r, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToListAsync(ct);
        return new(items, r.Page, r.PageSize, total);
    }
}
