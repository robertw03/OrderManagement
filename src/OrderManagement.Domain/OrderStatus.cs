namespace OrderManagement.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled
}

/// <summary>
/// Single source of truth for which status transitions are allowed.
/// Pending → Confirmed | Cancelled, Confirmed → Shipped | Cancelled, Shipped → Delivered.
/// Delivered and Cancelled are terminal.
/// </summary>
public static class OrderStatusTransitions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    public static bool IsTerminal(OrderStatus status) => NextStatuses(status).Count == 0;

    public static bool IsCancellable(OrderStatus status) => CanTransition(status, OrderStatus.Cancelled);
}
