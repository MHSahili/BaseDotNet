using BaseWebServer.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BaseWebServer.Api.Features.Sales;

public static class GetCustomer
{
    public sealed record Response(int Id, string Name, string Email, DateTimeOffset CreatedAt);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Response> HandleAsync(int id, CancellationToken cancellationToken)
        {
            var customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new Response(c.Id, c.Name, c.Email, c.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new Common.Errors.NotFoundException("Customer", id);
            return customer;
        }
    }
}
