namespace BaseWebServer.Api.Common.Caching;

public static class CacheKeys
{
    private const string V = "v1";
    public static string Customer(int id) => $"{V}:sales:customer:{id}";
    public static string Order(int id) => $"{V}:sales:order:{id}";
    public static string OrderList(int page, int pageSize, string sort, string status) =>
        $"{V}:sales:orders:{page}:{pageSize}:{sort}:{status}";
}

public static class CacheTags
{
    public static string Orders => "sales:orders";
    public static string Order(int id) => $"sales:order:{id}";
    public static string Customers => "sales:customers";
}
