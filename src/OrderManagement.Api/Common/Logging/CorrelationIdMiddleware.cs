using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace OrderManagement.Api.Common.Logging;

/// <summary>
/// Accepts an incoming X-Correlation-ID (e.g. from a gateway or another service) or generates a short
/// report code, echoes it back in the response, and pushes it into the Serilog LogContext so every log
/// line of the request carries it.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogProperty = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(correlationId) || !SafeCorrelationId().IsMatch(correlationId))
            correlationId = ReportCode.New();

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

/// <summary>
/// Short code a user can read out or type when reporting a problem, e.g. "K7M2Q".
/// 31 symbols without look-alikes (no 0/O, 1/I/L) → ~28.6 million combinations. It is a human handle,
/// not a unique key: every log line also carries the W3C trace id (<c>@tr</c>), which is unique.
/// </summary>
public static class ReportCode
{
    public const int Length = 5;
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);
}
