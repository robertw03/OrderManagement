using System.Text.RegularExpressions;
using Serilog.Context;

namespace OrderManagement.Api.Common.Logging;

/// <summary>
/// Accepts an incoming X-Correlation-ID (or generates one), echoes it back in the response,
/// and pushes it into the Serilog LogContext so every log line of the request carries it.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogProperty = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(correlationId) || !SafeCorrelationId().IsMatch(correlationId))
            correlationId = Guid.NewGuid().ToString("N");

        // TraceIdentifier is what ASP.NET Core and ProblemDetails use as the request id.
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogProperty, correlationId))
        {
            await next(context);
        }
    }

    // Reject arbitrary client input (log injection, oversized headers) and fall back to a generated id.
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeCorrelationId();
}
