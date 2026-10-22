using OrderManagement.Domain;
using static OrderManagement.Domain.OrderStatus;

namespace OrderManagement.UnitTests;

public sealed class OrderStatusTransitionsTests
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Valid =
    [
        (Pending, Confirmed), (Pending, Cancelled),
        (Confirmed, Shipped), (Confirmed, Cancelled),
        (Shipped, Delivered)
    ];

    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        foreach (var to in Enum.GetValues<OrderStatus>())
            data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_the_specified_transitions_are_allowed(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().Be(Valid.Contains((from, to)));
    }

    [Theory]
    [InlineData(Delivered)]
    [InlineData(Cancelled)]
    public void Delivered_and_cancelled_are_terminal(OrderStatus status)
    {
        OrderStatusTransitions.IsTerminal(status).Should().BeTrue();
        OrderStatusTransitions.NextStatuses(status).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Pending, true)]
    [InlineData(Confirmed, true)]
    [InlineData(Shipped, false)]
    [InlineData(Delivered, false)]
    [InlineData(Cancelled, false)]
    public void Only_pending_and_confirmed_orders_are_cancellable(OrderStatus status, bool expected)
    {
        OrderStatusTransitions.IsCancellable(status).Should().Be(expected);
    }
}
