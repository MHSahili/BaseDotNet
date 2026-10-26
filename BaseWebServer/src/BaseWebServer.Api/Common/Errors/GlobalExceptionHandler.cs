using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace BaseWebServer.Api.Common.Errors;

public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problems,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (status, title, detail) = exception switch
        {
            AppException app => (app.Status, app.Title, app.Message),
            DbUpdateConcurrencyException => (409, "Conflict", "The resource was modified by someone else. Reload and retry."),
            BadHttpRequestException => (400, "Bad request", "The request could not be read."),
            _ => (500, "Server error", "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            Log.Unhandled(logger, exception);
        }
        else
        {
            Log.RequestFailed(logger, status, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Error, "Unhandled exception")]
        public static partial void Unhandled(ILogger logger, Exception exception);

        [LoggerMessage(LogLevel.Information, "Request failed with {Status}: {Message}")]
        public static partial void RequestFailed(ILogger logger, int status, string? message);
    }
}
