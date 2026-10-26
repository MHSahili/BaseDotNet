using BaseWebServer.Api.Common.Caching;
using BaseWebServer.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BaseWebServer.Api.Features.Sales;

public static class CancelOrder
{
    public sealed class Handler(AppDbContext db, HybridCache cache)
    {
        public async Task HandleAsync(int id, CancellationToken cancellationToken)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                ?? throw new Common.Errors.NotFoundException("Order", id);

            order.Cancel();
            await db.SaveChangesAsync(cancellationToken);

            await cache.RemoveByTagAsync([CacheTags.Orders, CacheTags.Order(id)], cancellationToken);
        }
    }
}
