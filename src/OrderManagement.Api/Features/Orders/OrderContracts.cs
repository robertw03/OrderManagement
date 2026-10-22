using OrderManagement.Domain;

namespace OrderManagement.Api.Features.Orders;

// ---- Requests ----

public sealed class CreateOrderRequest
{
    public string CustomerId { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; init; } = [];

    /// <summary>
    /// Set to true to place an order identical to one placed moments ago (otherwise 409 POSSIBLE_DUPLICATE_ORDER).
    /// Not part of the idempotency hash, so a retry with the same key may add it.
    /// </summary>
    public bool ConfirmDuplicate { get; init; }
}

public sealed class OrderOptions
{
    public const string Section = "Orders";

    /// <summary>How long an identical order (same customer and content, different key) is treated as a likely duplicate. 0 disables.</summary>
    public int DuplicateWindowSeconds { get; set; } = 60;
}

public sealed class CreateOrderItemRequest
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}

public sealed class UpdateOrderStatusRequest
{
    public OrderStatus? Status { get; init; }

    /// <summary>Version the client last saw. Optional; the If-Match header can be used instead.</summary>
    public int? ExpectedVersion { get; init; }
}

public sealed class CancelOrderRequest
{
    public int? ExpectedVersion { get; init; }
}

public sealed class ListOrdersQuery
{
    public OrderStatus? Status { get; init; }
    public string? CustomerId { get; init; }

    /// <summary>Inclusive lower bound on CreatedAt.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound on CreatedAt.</summary>
    public DateTimeOffset? To { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

// ---- Responses ----

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string ShippingAddress,
    OrderStatus Status,
    decimal TotalAmount,
    int Version,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    IReadOnlyList<OrderStatus> AllowedNextStatuses,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.ShippingAddress,
        order.Status,
        order.TotalAmount,
        order.Version,
        order.InsertedAt,
        order.InsertedBy,
        order.UpdatedAt,
        order.UpdatedBy,
        OrderStatusTransitions.NextStatuses(order.Status),
        order.Items
            .OrderBy(i => i.ProductId)
            .Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.LineTotal))
            .ToList());
}

public sealed record OrderSummaryResponse(
    Guid Id,
    string CustomerId,
    OrderStatus Status,
    decimal TotalAmount,
    int ItemCount,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<OrderStatus> AllowedNextStatuses);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CreateOrderResult(OrderResponse Order, bool Replayed);
