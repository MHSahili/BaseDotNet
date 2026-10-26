using BaseWebServer.Api.Common.Errors;

namespace BaseWebServer.Api.Domain;

public sealed class Customer
{
    private Customer()
    {
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Create(string name, string email, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("A customer needs a name.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainRuleException("A customer needs an email.");
        }

        return new Customer { Name = name.Trim(), Email = email.Trim(), CreatedAt = now };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("A customer needs a name.");
        }

        Name = name.Trim();
    }
}
