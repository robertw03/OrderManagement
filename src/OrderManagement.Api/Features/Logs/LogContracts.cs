using System.Text.Json;
using FluentValidation;

namespace OrderManagement.Api.Features.Logs;

public sealed class LogViewerOptions
{
    public const string Section = "LogViewer";

    /// <summary>Folder of the Serilog JSON file sink, relative to the working directory (same as the sink's path).</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>File name prefix; files are named {prefix}{yyyyMMdd}.json by the rolling file sink.</summary>
    public string FilePrefix { get; set; } = "order-management-";
}

public sealed class LogQuery
{
    /// <summary>Day to read (server local date, matching the daily log file). Defaults to today.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Report code / correlation id, e.g. K7M2Q. Results are then shown oldest first, as a request trace.</summary>
    public string? Code { get; init; }

    public string? User { get; init; }

    /// <summary>Minimum level: Verbose, Debug, Information, Warning, Error, Fatal.</summary>
    public string? Level { get; init; }

    /// <summary>Free text searched in the message and the exception.</summary>
    public string? Q { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record LogEntryResponse(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? CorrelationId,
    string? UserName,
    string? RequestPath,
    int? StatusCode,
    string? SourceContext,
    string? TraceId,
    string? Exception,
    IReadOnlyDictionary<string, JsonElement> Properties);

public sealed record LogPageResponse(
    DateOnly Date,
    IReadOnlyList<LogEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed class LogQueryValidator : AbstractValidator<LogQuery>
{
    public const int MaxPageSize = 200;

    public LogQueryValidator()
    {
        RuleFor(x => x.Code).MaximumLength(64);
        RuleFor(x => x.User).MaximumLength(100);
        RuleFor(x => x.Q).MaximumLength(200);
        RuleFor(x => x.Level)
            .Must(level => level is null || LogLevels.Rank(level) >= 0)
            .WithMessage("'Level' must be one of: " + string.Join(", ", LogLevels.Names) + ".");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public static class LogLevels
{
    public static readonly string[] Names = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>Position of a Serilog level name (case-insensitive), or -1 when unknown.</summary>
    public static int Rank(string level) =>
        Array.FindIndex(Names, n => n.Equals(level, StringComparison.OrdinalIgnoreCase));
}
