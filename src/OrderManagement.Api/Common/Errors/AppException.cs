namespace OrderManagement.Api.Common.Errors;

/// <summary>
/// Base type for expected, client-facing errors. The global exception handler turns these
/// into RFC 7807 ProblemDetails using <see cref="StatusCode"/> and <see cref="ErrorCode"/>.
/// </summary>
public abstract class AppException(int statusCode, string errorCode, string title, string detail)
    : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;

    /// <summary>Extra machine-readable fields added to the ProblemDetails body.</summary>
    public Dictionary<string, object?> Extensions { get; } = new();

    public AppException With(string key, object? value)
    {
        Extensions[key] = value;
        return this;
    }
}

/// <summary>400 — the request itself is malformed (outside of model validation).</summary>
public sealed class BadRequestException(string errorCode, string detail)
    : AppException(StatusCodes.Status400BadRequest, errorCode, "Bad request", detail);

/// <summary>404 — the addressed resource does not exist.</summary>
public sealed class NotFoundException(string errorCode, string detail)
    : AppException(StatusCodes.Status404NotFound, errorCode, "Resource not found", detail);

/// <summary>409 — the request conflicts with the current state of the resource (concurrency, stock).</summary>
public sealed class ConflictException(string errorCode, string title, string detail)
    : AppException(StatusCodes.Status409Conflict, errorCode, title, detail);

/// <summary>422 — the request is well-formed but violates a business rule.</summary>
public sealed class BusinessRuleException(string errorCode, string title, string detail)
    : AppException(StatusCodes.Status422UnprocessableEntity, errorCode, title, detail);
