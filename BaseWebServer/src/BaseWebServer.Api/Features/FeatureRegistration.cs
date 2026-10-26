namespace BaseWebServer.Api.Features;

// Explicit handler registration: list every Handler here so missing
// registrations are easy to spot and architecture tests can verify them.
public static class FeatureRegistration
{
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        services.AddScoped<Sales.CreateCustomer.Handler>();
        services.AddScoped<Sales.GetCustomer.Handler>();
        services.AddScoped<Sales.CreateOrder.Handler>();
        services.AddScoped<Sales.GetOrder.Handler>();
        services.AddScoped<Sales.ListOrders.Handler>();
        services.AddScoped<Sales.ConfirmOrder.Handler>();
        services.AddScoped<Sales.CancelOrder.Handler>();
        return services;
    }
}
