namespace BaseWebServer.Api.Common.Errors;

public static class ErrorExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
            c.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString());
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
