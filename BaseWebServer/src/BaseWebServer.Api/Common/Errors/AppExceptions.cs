namespace BaseWebServer.Api.Common.Errors;

public abstract class AppException(string message, int status, string title) : Exception(message)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
}

public sealed class BadRequestException(string message)
    : AppException(message, StatusCodes.Status400BadRequest, "Bad request");

public sealed class NotFoundException(string entity, object key)
    : AppException($"{entity} '{key}' was not found.", StatusCodes.Status404NotFound, "Not found");

public sealed class ConflictException(string message)
    : AppException(message, StatusCodes.Status409Conflict, "Conflict");

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException(message, StatusCodes.Status403Forbidden, "Forbidden");

public sealed class DomainRuleException(string message)
    : AppException(message, StatusCodes.Status422UnprocessableEntity, "Business rule violated");
