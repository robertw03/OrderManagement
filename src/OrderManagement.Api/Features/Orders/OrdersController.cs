using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OrderManagement.Api.Features.Auth;
using Microsoft.Net.Http.Headers;
using OrderManagement.Api.Common.Errors;

namespace OrderManagement.Api.Features.Orders;

[ApiController]
[Route("api/orders")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";
    private const int MaxIdempotencyKeyLength = 100;

    /// <summary>Create an order. Requires an Idempotency-Key header; retries with the same key return the original order.</summary>
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var key = ValidateIdempotencyKey(idempotencyKey);
        var result = await orders.CreateAsync(key, request, ct);

        if (result.Replayed)
            Response.Headers[IdempotentReplayedHeader] = "true";
        SetETag(result.Order.Version);

        return CreatedAtAction(nameof(GetById), new { id = result.Order.Id }, result.Order);
    }

    /// <summary>Get an order with its items. The ETag header carries the order version.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        SetETag(order.Version);
        return Ok(order);
    }

    /// <summary>List orders filtered by status, customer and creation date range [from, to), newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<OrderSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<OrderSummaryResponse>> List([FromQuery] ListOrdersQuery query, CancellationToken ct) =>
        orders.ListAsync(query, ct);

    /// <summary>
    /// Move an order to a new status. Send the version you last saw as <c>expectedVersion</c> or
    /// as an <c>If-Match</c> header to get a 409 instead of overwriting someone else's change.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var expectedVersion = request.ExpectedVersion ?? ReadIfMatchVersion();
        var order = await orders.UpdateStatusAsync(id, request.Status!.Value, expectedVersion, ct);
        SetETag(order.Version);
        return Ok(order);
    }

    /// <summary>Cancel a Pending or Confirmed order and return its stock.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<OrderResponse>> Cancel(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelOrderRequest? request,
        CancellationToken ct)
    {
        var expectedVersion = request?.ExpectedVersion ?? ReadIfMatchVersion();
        var order = await orders.CancelAsync(id, expectedVersion, ct);
        SetETag(order.Version);
        return Ok(order);
    }

    private static string ValidateIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BadRequestException(ErrorCodes.IdempotencyKeyMissing,
                $"The {IdempotencyKeyHeader} header is required. Generate a unique value (e.g. a UUID) per order and reuse it on retries.");

        key = key.Trim();
        if (key.Length > MaxIdempotencyKeyLength || key.Any(char.IsControl))
            throw new BadRequestException(ErrorCodes.IdempotencyKeyInvalid,
                $"The {IdempotencyKeyHeader} header must be at most {MaxIdempotencyKeyLength} printable characters.");

        return key;
    }

    private int? ReadIfMatchVersion()
    {
        var raw = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (EntityTagHeaderValue.TryParse(raw, out var tag) && int.TryParse(tag.Tag.Value?.Trim('"'), out var version))
            return version;

        throw new BadRequestException(ErrorCodes.BadRequest, "The If-Match header must contain an order version, e.g. If-Match: \"3\".");
    }

    private void SetETag(int version) => Response.Headers.ETag = $"\"{version}\"";
}
