using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BaseWebServer.Api.Common.Observability;

public static class Telemetry
{
    public const string Name = "BaseWebServer";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("basewebserver.cache.hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("basewebserver.cache.misses");
}
