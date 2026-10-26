using System.ComponentModel.DataAnnotations;
using BaseWebServer.Api.Common.Caching;
using BaseWebServer.Api.Data;
using BaseWebServer.Api.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace BaseWebServer.Api.Features.Sales;

public static class CreateCustomer
{
    public sealed record Request(
        [property: Required, MaxLength(200)] string Name,
        [property: Required, MaxLength(320)] string Email);

    public sealed record Response(int Id, string Name, string Email);

    public sealed class Handler(AppDbContext db, TimeProvider time, HybridCache cache)
    {
        public async Task<Response> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            var customer = Customer.Create(request.Name, request.Email, time.GetUtcNow());
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);

            await cache.RemoveByTagAsync(CacheTags.Customers, cancellationToken);
            return new Response(customer.Id, customer.Name, customer.Email);
        }
    }
}
