using BaseWebServer.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BaseWebServer.Api.Features.Sales;

public static class GetOrder
{
    public sealed record Response(int Id, int CustomerId, decimal Total, string Status, DateTimeOffset CreatedAt);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Response> HandleAsync(int id, CancellationToken cancellationToken)
        {
            var order = await db.Orders.AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new Response(o.Id, o.CustomerId, o.Total, o.Status.ToString(), o.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new Common.Errors.NotFoundException("Order", id);
            return order;
        }
    }
}
