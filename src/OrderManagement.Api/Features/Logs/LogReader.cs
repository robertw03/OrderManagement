using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace OrderManagement.Api.Features.Logs;

/// <summary>
/// Reads the daily Serilog compact-JSON (CLEF) files for the in-app log viewer. Meant for a single-instance
/// prototype; with several instances or real volume, logs go to Seq/ELK and are searched there.
/// </summary>
public sealed partial class LogReader(IOptions<LogViewerOptions> options, TimeProvider clock)
{
    private string DirectoryPath => Path.GetFullPath(options.Value.Directory);

    public IReadOnlyList<DateOnly> AvailableDates()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];

        return Directory.EnumerateFiles(DirectoryPath, $"{options.Value.FilePrefix}*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)[options.Value.FilePrefix.Length..])
            .Select(stamp => DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : (DateOnly?)null)
            .OfType<DateOnly>()
            .OrderDescending()
            .ToList();
    }

    public async Task<LogPageResponse> SearchAsync(LogQuery query, CancellationToken ct)
    {
        var date = query.Date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var minLevel = query.Level is null ? 0 : LogLevels.Rank(query.Level);
        var matches = new List<LogEntryResponse>();

        // The file name is built from a parsed date, never from user text, so no path traversal is possible.
        var path = Path.Combine(DirectoryPath, $"{options.Value.FilePrefix}{date:yyyyMMdd}.json");
        if (File.Exists(path))
        {
            // Serilog keeps the file open for writing; share it so reading never blocks or breaks logging.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = TryParse(line);
                if (entry is not null && Matches(entry, query, minLevel))
                    matches.Add(entry);
            }
        }

        // A report code is read as a story (oldest first); everything else newest first.
        IEnumerable<LogEntryResponse> ordered = string.IsNullOrWhiteSpace(query.Code)
            ? matches.OrderByDescending(e => e.Timestamp)
            : matches.OrderBy(e => e.Timestamp);

        var page = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        var totalPages = (int)Math.Ceiling(matches.Count / (double)query.PageSize);
        return new LogPageResponse(date, page, query.Page, query.PageSize, matches.Count, totalPages);
    }

    private static bool Matches(LogEntryResponse entry, LogQuery query, int minLevel)
    {
        if (LogLevels.Rank(entry.Level) < minLevel) return false;
        if (!string.IsNullOrWhiteSpace(query.Code) && !string.Equals(entry.CorrelationId, query.Code.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(query.User) && !string.Equals(entry.UserName, query.User.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            return entry.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                   || (entry.Exception?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        return true;
    }

    /// <summary>Parses one CLEF line; malformed lines (e.g. a half-written last line) are skipped.</summary>
    public static LogEntryResponse? TryParse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var properties = new Dictionary<string, JsonElement>();
            foreach (var property in root.EnumerateObject())
            {
                // "@x" etc. are CLEF metadata; "@@name" is an escaped user property called "@name".
                if (property.Name.StartsWith('@') && !property.Name.StartsWith("@@")) continue;
                properties[property.Name.StartsWith("@@") ? property.Name[1..] : property.Name] = property.Value.Clone();
            }

            var template = String(root, "@mt") ?? String(root, "@m") ?? string.Empty;
            return new LogEntryResponse(
                Timestamp: root.TryGetProperty("@t", out var t) ? t.GetDateTimeOffset() : DateTimeOffset.MinValue,
                Level: String(root, "@l") ?? "Information", // CLEF omits the level when it is Information
                Message: Render(template, properties),
                CorrelationId: String(properties, "CorrelationId"),
                UserName: String(properties, "UserName"),
                RequestPath: String(properties, "RequestPath"),
                StatusCode: properties.TryGetValue("StatusCode", out var status) && status.TryGetInt32(out var code) ? code : null,
                SourceContext: String(properties, "SourceContext"),
                TraceId: String(root, "@tr"),
                Exception: String(root, "@x"),
                Properties: properties);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Fills a message template ("Order {OrderId} created") with the event's property values.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, JsonElement> properties) =>
        TemplateToken().Replace(template, match =>
        {
            if (match.Value is "{{") return "{";
            if (match.Value is "}}") return "}";
            var name = match.Groups["name"].Value;
            if (!properties.TryGetValue(name, out var value)) return match.Value;
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        });

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? String(IReadOnlyDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"\{\{|\}\}|\{[@$]?(?<name>[A-Za-z0-9_]+)(?:,[^:}]*)?(?::[^}]*)?\}")]
    private static partial Regex TemplateToken();
}
