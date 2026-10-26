using Microsoft.Extensions.Caching.Hybrid;

namespace BaseWebServer.Api.Common.Caching;

public static class CachingExtensions
{
    public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration config)
    {
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }

        services.AddHybridCache(o =>
        {
            o.MaximumPayloadBytes = 1024 * 1024;
            o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(1),
                Expiration = TimeSpan.FromMinutes(10),
            };
        });
        return services;
    }
}
