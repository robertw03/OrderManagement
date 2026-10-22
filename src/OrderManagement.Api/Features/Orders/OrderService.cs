using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Orders;

public sealed class OrderService(OrderDbContext db, ICurrentUser currentUser, TimeProvider clock, ILogger<OrderService> logger)
{
    /// <summary>
    /// Creates an order exactly once per (customerId, idempotencyKey).
    /// Everything — claiming the key, deducting stock, inserting the order — happens in one
    /// transaction, so a failure anywhere leaves no partial state behind.
    /// </summary>
    public async Task<CreateOrderResult> CreateAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken ct)
    {
        var customerId = request.CustomerId.Trim();
        var shippingAddress = request.ShippingAddress.Trim();
        var user = currentUser.UserName;

        // Merge duplicate product lines and sort by product id. Sorting gives every transaction the
        // same lock order on product rows, which rules out deadlocks between multi-item orders.
        var items = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .OrderBy(i => i.ProductId)
            .ToList();

        var requestHash = RequestHasher.Hash(customerId, shippingAddress, items);

        // Fast path for plain retries: no transaction, no locks.
        var existing = await FindIdempotencyRecordAsync(customerId, idempotencyKey, ct);
        if (existing is not null)
            return await ReplayAsync(existing, requestHash, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var orderId = Guid.CreateVersion7();
        var now = clock.GetUtcNow();

        // Claim the key first. If another transaction already inserted the same (customer, key) but has
        // not committed yet, PostgreSQL blocks this statement on the unique index until that transaction
        // finishes, then reports the conflict. That is what makes Scenario C safe.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "trIdempotencyKey"
                ("uidIdempotencyKeyUUID", "txtCustomerId", "txtIdempotencyKey", "txtRequestHash", "uidOrderUUID",
                 "bolActive", "bolDeleted", "dtInserted", "txtInsertedBy")
            VALUES ({Guid.CreateVersion7()}, {customerId}, {idempotencyKey}, {requestHash}, {orderId},
                 true, false, {now}, {user})
            ON CONFLICT ("txtCustomerId", "txtIdempotencyKey") DO NOTHING
            """, ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            existing = await FindIdempotencyRecordAsync(customerId, idempotencyKey, ct)
                ?? throw new ConflictException(ErrorCodes.IdempotencyRequestInProgress, "Request in progress",
                    "A request with the same Idempotency-Key is still being processed. Retry shortly.");
            return await ReplayAsync(existing, requestHash, ct);
        }

        // Check-and-deduct in a single atomic statement per product (Scenario A). The row lock taken by
        // the UPDATE serializes concurrent orders for the same product, and the WHERE clause is
        // re-evaluated against the latest committed stock, so stock can never go below zero.
        foreach (var item in items)
        {
            var deducted = await db.Products
                .Where(p => p.Id == item.ProductId && p.IsActive && p.StockQuantity >= item.Quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity - item.Quantity)
                    .SetProperty(p => p.UpdatedAt, now)
                    .SetProperty(p => p.UpdatedBy, user), ct);

            if (deducted == 0)
                throw await StockFailureAsync(item.ProductId, item.Quantity, ct);
        }

        // Product rows are locked by this transaction now, so name/price cannot change before commit.
        var productIds = items.Select(i => i.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var order = new Order
        {
            Id = orderId,
            CustomerId = customerId,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            Version = 1,
            Items = items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = products[i.ProductId].Name,
                UnitPrice = products[i.ProductId].Price,
                Quantity = i.Quantity,
                LineTotal = products[i.ProductId].Price * i.Quantity
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId} by {User} with {ItemCount} item(s), total {TotalAmount}",
            order.Id, order.CustomerId, user, order.Items.Count, order.TotalAmount);

        return new CreateOrderResult(OrderResponse.From(order), Replayed: false);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw OrderNotFound(id);

        return OrderResponse.From(order);
    }

    public async Task<PagedResponse<OrderSummaryResponse>> ListAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking();

        if (query.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(query.CustomerId))
            orders = orders.Where(o => o.CustomerId == query.CustomerId.Trim());
        if (query.From is { } from)
            orders = orders.Where(o => o.InsertedAt >= from.ToUniversalTime());
        if (query.To is { } to)
            orders = orders.Where(o => o.InsertedAt < to.ToUniversalTime());

        var totalCount = await orders.CountAsync(ct);

        var page = await orders
            .OrderByDescending(o => o.InsertedAt)
            .ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new OrderSummaryResponse(
                o.Id, o.CustomerId, o.Status, o.TotalAmount, o.Items.Count, o.Version, o.InsertedAt, o.UpdatedAt))
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new PagedResponse<OrderSummaryResponse>(page, query.Page, query.PageSize, totalCount, totalPages);
    }

    public Task<OrderResponse> UpdateStatusAsync(Guid id, OrderStatus newStatus, int? expectedVersion, CancellationToken ct) =>
        ChangeStatusAsync(id, newStatus, expectedVersion, viaCancelEndpoint: false, ct);

    public Task<OrderResponse> CancelAsync(Guid id, int? expectedVersion, CancellationToken ct) =>
        ChangeStatusAsync(id, OrderStatus.Cancelled, expectedVersion, viaCancelEndpoint: true, ct);

    /// <summary>
    /// The only code path that changes an order's status. Both PATCH /status and POST /cancel use it,
    /// so a cancellation always restores stock no matter which endpoint was called.
    /// </summary>
    private async Task<OrderResponse> ChangeStatusAsync(
        Guid id, OrderStatus newStatus, int? expectedVersion, bool viaCancelEndpoint, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw OrderNotFound(id);

        if (expectedVersion is { } expected && expected != order.Version)
        {
            throw new ConflictException(ErrorCodes.VersionMismatch, "Version mismatch",
                    $"Order {id} is at version {order.Version} but the request expected version {expected}. Reload the order and try again.")
                .With("currentStatus", order.Status.ToString())
                .With("currentVersion", order.Version);
        }

        if (!OrderStatusTransitions.CanTransition(order.Status, newStatus))
            throw InvalidTransition(order, newStatus, viaCancelEndpoint);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var user = currentUser.UserName;

        // Compare-and-swap on (status, version) — Scenario B. If another admin changed the order after we
        // read it, the row no longer matches and zero rows are updated. When two updates race, the second
        // one waits for the first one's row lock and then re-checks the WHERE clause against the new row.
        var updated = await db.Orders
            .Where(o => o.Id == id && o.Status == order.Status && o.Version == order.Version)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, newStatus)
                .SetProperty(o => o.Version, o => o.Version + 1)
                .SetProperty(o => o.UpdatedAt, now)
                .SetProperty(o => o.UpdatedBy, user), ct);

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            var current = await db.Orders.AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new { o.Status, o.Version, o.UpdatedBy })
                .FirstOrDefaultAsync(ct);

            logger.LogWarning("Status change {FromStatus} -> {ToStatus} on order {OrderId} by {User} lost the race; " +
                              "order is now {CurrentStatus} (version {CurrentVersion}, by {CurrentUpdatedBy})",
                order.Status, newStatus, id, user, current?.Status, current?.Version, current?.UpdatedBy);

            throw new ConflictException(ErrorCodes.ConcurrentModification, "Concurrent modification",
                    $"Order {id} was modified by another request (now {current?.Status}, version {current?.Version}). " +
                    "Reload the order and try again.")
                .With("currentStatus", current?.Status.ToString())
                .With("currentVersion", current?.Version);
        }

        // Stock is restored only by the transaction that actually won the status change, so two
        // concurrent cancels can never return the same stock twice. Query filters are ignored so stock
        // still goes back to a product that was deactivated or soft-deleted in the meantime.
        if (newStatus == OrderStatus.Cancelled)
        {
            foreach (var item in order.Items.OrderBy(i => i.ProductId))
            {
                await db.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == item.ProductId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity)
                        .SetProperty(p => p.UpdatedAt, now)
                        .SetProperty(p => p.UpdatedBy, user), ct);
            }
        }

        await tx.CommitAsync(ct);

        logger.LogInformation("Order {OrderId} status changed {FromStatus} -> {ToStatus} by {User} (version {Version})",
            id, order.Status, newStatus, user, order.Version + 1);
        if (newStatus == OrderStatus.Cancelled)
            logger.LogInformation("Restored stock for {ItemCount} item(s) of cancelled order {OrderId}", order.Items.Count, id);

        order.Status = newStatus;
        order.Version += 1;
        order.UpdatedAt = now;
        order.UpdatedBy = user;
        return OrderResponse.From(order);
    }

    private Task<IdempotencyRecord?> FindIdempotencyRecordAsync(string customerId, string key, CancellationToken ct) =>
        db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.IdempotencyKey == key, ct);

    private async Task<CreateOrderResult> ReplayAsync(IdempotencyRecord record, string requestHash, CancellationToken ct)
    {
        if (record.RequestHash != requestHash)
        {
            throw new BusinessRuleException(ErrorCodes.IdempotencyKeyReused, "Idempotency key reused",
                    "This Idempotency-Key was already used with a different request body. Use a new key for a new order.")
                .With("orderId", record.OrderId);
        }

        logger.LogInformation("Idempotent replay: key {IdempotencyKey} already created order {OrderId}",
            record.IdempotencyKey, record.OrderId);

        return new CreateOrderResult(await GetAsync(record.OrderId, ct), Replayed: true);
    }

    private async Task<AppException> StockFailureAsync(Guid productId, int requested, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.StockQuantity, p.IsActive })
            .FirstOrDefaultAsync(ct);

        if (product is null)
        {
            return new BusinessRuleException(ErrorCodes.UnknownProduct, "Unknown product",
                    $"Product {productId} does not exist.")
                .With("productId", productId);
        }

        if (!product.IsActive)
        {
            return new BusinessRuleException(ErrorCodes.ProductInactive, "Product inactive",
                    $"Product {productId} ({product.Name}) is not available for ordering.")
                .With("productId", productId);
        }

        logger.LogWarning("Insufficient stock for product {ProductId}: requested {Requested}, available {Available}",
            productId, requested, product.StockQuantity);

        return new ConflictException(ErrorCodes.InsufficientStock, "Insufficient stock",
                $"Product {productId} ({product.Name}) has {product.StockQuantity} unit(s) available but {requested} were requested.")
            .With("productId", productId)
            .With("requested", requested)
            .With("available", product.StockQuantity);
    }

    private static NotFoundException OrderNotFound(Guid id) =>
        new(ErrorCodes.OrderNotFound, $"Order {id} was not found.");

    private static AppException InvalidTransition(Order order, OrderStatus newStatus, bool viaCancelEndpoint)
    {
        var allowed = OrderStatusTransitions.NextStatuses(order.Status).Select(s => s.ToString()).ToList();

        AppException error = viaCancelEndpoint
            ? new BusinessRuleException(ErrorCodes.OrderNotCancellable, "Order cannot be cancelled",
                $"Order {order.Id} is {order.Status}; only Pending or Confirmed orders can be cancelled.")
            : new BusinessRuleException(ErrorCodes.InvalidStatusTransition, "Invalid status transition",
                $"Order {order.Id} cannot move from {order.Status} to {newStatus}.");

        return error
            .With("currentStatus", order.Status.ToString())
            .With("requestedStatus", newStatus.ToString())
            .With("allowedStatuses", allowed);
    }
}
