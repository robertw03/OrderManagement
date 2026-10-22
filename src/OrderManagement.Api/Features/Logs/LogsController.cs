using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Features.Auth;

namespace OrderManagement.Api.Features.Logs;

/// <summary>
/// Log viewer for IT / admins: find everything that happened in one request from the report code a user
/// sends in. Logs contain user names, IPs and request details, so only admins can read them.
/// </summary>
[ApiController]
[Route("api/logs")]
[Authorize(Roles = Roles.Admin)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class LogsController(LogReader logs) : ControllerBase
{
    /// <summary>Search one day of logs by report code, user, minimum level and free text.</summary>
    [HttpGet]
    public Task<LogPageResponse> Search([FromQuery] LogQuery query, CancellationToken ct) => logs.SearchAsync(query, ct);

    /// <summary>Days for which a log file exists, newest first.</summary>
    [HttpGet("dates")]
    public IReadOnlyList<DateOnly> Dates() => logs.AvailableDates();
}
