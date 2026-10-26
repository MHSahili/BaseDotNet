using BaseWebServer.Api.Common.Errors;

namespace BaseWebServer.Api.Domain;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2,
}

public sealed class Order
{
    private Order()
    {
    }

    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Order Create(int customerId, decimal total, DateTimeOffset now)
    {
        if (customerId <= 0)
        {
            throw new DomainRuleException("An order needs a customer.");
        }

        if (total <= 0)
        {
            throw new DomainRuleException("An order total must be positive.");
        }

        return new Order { CustomerId = customerId, Total = total, Status = OrderStatus.Pending, CreatedAt = now };
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainRuleException("Only pending orders can be confirmed.");
        }

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainRuleException("This order is already cancelled.");
        }

        if (Status == OrderStatus.Confirmed)
        {
            throw new DomainRuleException("Confirmed orders cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
    }
}
