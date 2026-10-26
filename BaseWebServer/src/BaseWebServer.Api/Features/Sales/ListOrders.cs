using BaseWebServer.Api.Common.Caching;
using BaseWebServer.Api.Common.Paging;
using BaseWebServer.Api.Data;
using BaseWebServer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BaseWebServer.Api.Features.Sales;

public static class ListOrders
{
    public sealed class Request : PagedRequest
    {
        public string? Status { get; init; }
        public int? CustomerId { get; init; }
    }

    public sealed record Item(int Id, int CustomerId, decimal Total, string Status);

    public sealed class Handler(AppDbContext db, HybridCache cache)
    {
        public ValueTask<PagedResponse<Item>> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            if (!IsCacheable(request))
            {
                return new ValueTask<PagedResponse<Item>>(QueryAsync(request, cancellationToken));
            }

            var key = CacheKeys.OrderList(
                request.Page,
                request.PageSize,
                (request.Sort ?? "-created").ToLowerInvariant(),
                (request.Status ?? "any").ToLowerInvariant());

            return cache.GetOrCreateAsync(
                key,
                token => new ValueTask<PagedResponse<Item>>(QueryAsync(request, token)),
                tags: [CacheTags.Orders],
                cancellationToken: cancellationToken);
        }

        private static bool IsCacheable(Request request) => request.Page <= 3 && request.CustomerId is null;

        private async Task<PagedResponse<Item>> QueryAsync(Request request, CancellationToken cancellationToken)
        {
            var query = db.Orders.AsNoTracking();

            if (request.CustomerId is { } customerId)
            {
                query = query.Where(o => o.CustomerId == customerId);
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var status))
                {
                    throw new Common.Errors.BadRequestException($"Unknown status '{request.Status}'.");
                }

                query = query.Where(o => o.Status == status);
            }

            return await ApplySort(query, request.Sort)
                .Select(o => new Item(o.Id, o.CustomerId, o.Total, o.Status.ToString()))
                .ToPagedAsync(request, cancellationToken);
        }

        private static IQueryable<Order> ApplySort(IQueryable<Order> query, string? sort) =>
            sort?.ToLowerInvariant() switch
            {
                "total" => query.OrderBy(o => o.Total).ThenBy(o => o.Id),
                "-total" => query.OrderByDescending(o => o.Total).ThenByDescending(o => o.Id),
                "created" => query.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id),
                null or "-created" => query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id),
                _ => throw new Common.Errors.BadRequestException($"Unknown sort '{sort}'."),
            };
    }
}
