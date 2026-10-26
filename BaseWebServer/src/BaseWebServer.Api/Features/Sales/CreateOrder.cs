using System.ComponentModel.DataAnnotations;
using BaseWebServer.Api.Common.Caching;
using BaseWebServer.Api.Data;
using BaseWebServer.Api.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace BaseWebServer.Api.Features.Sales;

public static class CreateOrder
{
    public sealed record Request(
        [property: Range(1, int.MaxValue)] int CustomerId,
        [property: Range(0.01, 1_000_000)] decimal Total);

    public sealed record Response(int Id, int CustomerId, decimal Total, string Status);

    public sealed class Handler(AppDbContext db, TimeProvider time, HybridCache cache)
    {
        public async Task<Response> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var customerExists = await db.Customers.FindAsync([request.CustomerId], cancellationToken) is not null;
            if (!customerExists)
            {
                throw new Common.Errors.NotFoundException("Customer", request.CustomerId);
            }

            var order = Order.Create(request.CustomerId, request.Total, time.GetUtcNow());
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            await cache.RemoveByTagAsync(CacheTags.Orders, cancellationToken);
            return new Response(order.Id, order.CustomerId, order.Total, order.Status.ToString());
        }
    }
}
