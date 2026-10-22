using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog.Context;

namespace OrderManagement.Api.Common.Errors;

/// <summary>
/// Converts every exception that escapes a controller into the same ProblemDetails shape,
/// so clients only have to understand one error format.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The exception handler runs outside the middleware that adds the user to the log context,
        // so add it here — otherwise the most important line of a failed request has no user.
        ProblemDetails problem;
        using (LogContext.PushProperty("UserName", httpContext.User.Identity?.Name ?? "anonymous"))
        {
            problem = Map(httpContext, exception);
        }
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private ProblemDetails Map(HttpContext httpContext, Exception exception)
    {
        switch (exception)
        {
            case AppException app:
            {
                var level = app.StatusCode == StatusCodes.Status409Conflict ? LogLevel.Warning : LogLevel.Information;
                logger.Log(level, "Request rejected with {StatusCode} {ErrorCode}: {Detail}",
                    app.StatusCode, app.ErrorCode, app.Message);

                var problem = Create(app.StatusCode, app.Title, app.Message, app.ErrorCode);
                foreach (var (key, value) in app.Extensions)
                    problem.Extensions[key] = value;
                return problem;
            }

            case DbUpdateConcurrencyException:
                logger.LogWarning(exception, "Optimistic concurrency conflict");
                return Create(StatusCodes.Status409Conflict, "Concurrent modification",
                    "The resource was modified by another request. Reload it and try again.",
                    ErrorCodes.ConcurrentModification);

            // SaveChanges wraps database errors in DbUpdateException; ExecuteUpdate/raw SQL throws them directly.
            case not null when FindPostgresException(exception) is
                { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation } pg:
                // Constraints are the safety net behind the application checks; reaching here means
                // two requests raced past those checks and the database settled it.
                logger.LogWarning(exception, "Database constraint {Constraint} rejected the write", pg.ConstraintName);
                return Create(StatusCodes.Status409Conflict, "Conflict",
                    "The request conflicts with the current state of the resource. Please retry.",
                    ErrorCodes.Conflict);

            case not null when FindPostgresException(exception) is
                { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } pg:
                logger.LogWarning(exception, "Transaction aborted by the database ({SqlState})", pg.SqlState);
                return Create(StatusCodes.Status409Conflict, "Concurrent modification",
                    "The request collided with a concurrent request. Please retry.",
                    ErrorCodes.ConcurrentModification);

            case BadHttpRequestException bad:
                logger.LogInformation(exception, "Malformed HTTP request");
                return Create(bad.StatusCode, "Bad request", bad.Message, ErrorCodes.BadRequest);

            default:
                logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                    httpContext.Request.Method, httpContext.Request.Path);
                return Create(StatusCodes.Status500InternalServerError, "Internal server error",
                    "An unexpected error occurred. Quote the correlationId when contacting support.",
                    ErrorCodes.InternalError);
        }
    }

    private static PostgresException? FindPostgresException(Exception exception) =>
        exception as PostgresException ?? exception.InnerException as PostgresException;

    private static ProblemDetails Create(int status, string title, string detail, string errorCode) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["errorCode"] = errorCode }
    };
}
