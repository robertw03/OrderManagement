namespace OrderManagement.Domain;

public class Order : AuditableEntity
{
    public string CustomerId { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }

    /// <summary>Optimistic concurrency token, incremented on every status change.</summary>
    public int Version { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem : AuditableEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Snapshot of the product name and price at the time the order was placed.</summary>
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
