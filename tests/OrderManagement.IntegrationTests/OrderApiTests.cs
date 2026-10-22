using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Features.Orders;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

/// <summary>Functional behaviour and the error contract of the order endpoints.</summary>
[Collection(ApiCollection.Name)]
public sealed class OrderApiTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid Indomie = SeedData.IndomieId;
    private static readonly Guid Aqua = SeedData.AquaId;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_merges_duplicate_lines_snapshots_prices_and_deducts_stock()
    {
        var response = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 2), (Aqua, 1), (Indomie, 1)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be("\"1\"");

        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
        order.Status.Should().Be(OrderStatus.Pending);
        order.Version.Should().Be(1);
        order.Items.Should().HaveCount(2);
        order.Items.Single(i => i.ProductId == Indomie).Quantity.Should().Be(3);
        order.TotalAmount.Should().Be(3 * 115_000m + 52_000m);

        (await fixture.GetStockAsync(Indomie)).Should().Be(497);
        (await fixture.GetStockAsync(Aqua)).Should().Be(299);
    }

    [Fact]
    public async Task Retry_with_same_key_returns_original_order_without_deducting_again()
    {
        var key = Guid.NewGuid().ToString();
        var first = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 5)), key);
        var retry = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 5)), key);

        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await retry.Content.ReadFromJsonAsync<OrderResponse>(Json))!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<OrderResponse>(Json))!.Id);
        (await fixture.GetStockAsync(Indomie)).Should().Be(495);
    }

    [Fact]
    public async Task Same_key_from_different_customers_creates_separate_orders()
    {
        var key = Guid.NewGuid().ToString();
        (await _client.PostOrderAsync(Order("CUST-1", (Indomie, 1)), key)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostOrderAsync(Order("CUST-2", (Indomie, 1)), key)).StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Failed_multi_item_order_rolls_back_everything()
    {
        await fixture.SetStockAsync(Aqua, 1);

        // Items are processed by ascending product id: product 2 is deducted successfully, then product 3
        // fails. The deduction of product 2 must be rolled back with the rest of the transaction.
        var response = await _client.PostOrderAsync(Order("CUST-RB", (Aqua, 2), (Indomie, 5)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("INSUFFICIENT_STOCK");
        (await fixture.GetStockAsync(Indomie)).Should().Be(500);
        (await fixture.GetStockAsync(Aqua)).Should().Be(1);

        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(0);
        (await db.IdempotencyRecords.CountAsync()).Should().Be(0, "a failed request must not burn its idempotency key");
    }

    [Fact]
    public async Task Status_follows_the_lifecycle_and_rejects_invalid_transitions()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));

        var invalid = await _client.PatchStatusAsync(order.Id, "Delivered");
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await invalid.ReadProblemAsync();
        problem.GetProperty("errorCode").GetString().Should().Be("INVALID_STATUS_TRANSITION");
        problem.GetProperty("allowedStatuses").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Confirmed", "Cancelled");

        foreach (var status in new[] { "Confirmed", "Shipped", "Delivered" })
            (await _client.PatchStatusAsync(order.Id, status)).StatusCode.Should().Be(HttpStatusCode.OK);

        var terminal = await _client.PatchStatusAsync(order.Id, "Cancelled");
        terminal.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cancelling_via_status_endpoint_also_restores_stock()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 7)));

        (await _client.PatchStatusAsync(order.Id, "Cancelled")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await fixture.GetStockAsync(Indomie)).Should().Be(500);
    }

    [Fact]
    public async Task Cancel_of_shipped_order_is_rejected()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));
        await _client.PatchStatusAsync(order.Id, "Confirmed");
        await _client.PatchStatusAsync(order.Id, "Shipped");

        var response = await _client.CancelAsync(order.Id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("ORDER_NOT_CANCELLABLE");
        (await fixture.GetStockAsync(Indomie)).Should().Be(499);
    }

    [Fact]
    public async Task Stale_version_in_if_match_header_is_rejected_with_409()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));
        await _client.PatchStatusAsync(order.Id, "Confirmed");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{order.Id}/status")
        {
            Content = JsonContent.Create(new { status = "Shipped" })
        };
        request.Headers.IfMatch.ParseAdd("\"1\"");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("errorCode").GetString().Should().Be("VERSION_MISMATCH");
        problem.GetProperty("currentVersion").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task List_filters_by_status_customer_and_date_and_paginates()
    {
        for (var i = 0; i < 5; i++)
            await _client.CreateOrderAsync(Order("CUST-L", (Indomie, 1)));
        var other = await _client.CreateOrderAsync(Order("CUST-OTHER", (Indomie, 1)));
        await _client.CancelAsync(other.Id);

        var page = await _client.GetFromJsonAsync<PagedResponse<OrderSummaryResponse>>(
            "/api/orders?customerId=CUST-L&status=Pending&page=2&pageSize=2", Json);

        page!.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
        page.Items.Should().HaveCount(2).And.OnlyContain(o => o.CustomerId == "CUST-L");
        page.Items.Should().BeInDescendingOrder(o => o.CreatedAt);

        var cancelled = await _client.GetFromJsonAsync<PagedResponse<OrderSummaryResponse>>(
            "/api/orders?status=Cancelled", Json);
        cancelled!.Items.Should().ContainSingle(o => o.Id == other.Id);

        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var none = await _client.GetFromJsonAsync<PagedResponse<OrderSummaryResponse>>($"/api/orders?from={future}", Json);
        none!.TotalCount.Should().Be(0);
    }

    // ---------------- Standard columns: audit, active, soft delete ----------------

    [Fact]
    public async Task Audit_columns_record_the_signed_in_user()
    {
        var sales = fixture.CreateClient("sales");
        var order = await sales.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));

        order.CreatedBy.Should().Be("sales");
        order.UpdatedAt.Should().BeNull();

        (await _client.PatchStatusAsync(order.Id, "Confirmed")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = fixture.CreateDbContext();
        var row = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        row.InsertedBy.Should().Be("sales");
        row.UpdatedBy.Should().Be("admin");
        row.UpdatedAt.Should().NotBeNull();
        row.IsActive.Should().BeTrue();
        row.IsDeleted.Should().BeFalse();
        row.Items.Should().OnlyContain(i => i.InsertedBy == "sales" && i.Id != Guid.Empty);

        var product = await db.Products.SingleAsync(p => p.Id == Indomie);
        product.UpdatedBy.Should().Be("sales", "the stock deduction is audited on the product too");
    }

    [Fact]
    public async Task Inactive_product_cannot_be_ordered()
    {
        await fixture.SetProductFlagsAsync(Indomie, isActive: false, isDeleted: false);

        var response = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 1)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("PRODUCT_INACTIVE");
        (await fixture.GetStockAsync(Indomie)).Should().Be(500);
    }

    [Fact]
    public async Task Soft_deleted_product_is_treated_as_unknown()
    {
        await fixture.SetProductFlagsAsync(Indomie, isActive: true, isDeleted: true);

        var response = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 1)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("UNKNOWN_PRODUCT");
        (await _client.GetAsync($"/api/products/{Indomie}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_returns_stock_even_if_product_was_deactivated_meanwhile()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 4)));
        await fixture.SetProductFlagsAsync(Indomie, isActive: false, isDeleted: false);

        (await _client.CancelAsync(order.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await fixture.GetStockAsync(Indomie)).Should().Be(500);
    }

    // ---------------- Demo UI ----------------

    [Fact]
    public async Task Root_serves_the_demo_ui()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Order Management");
    }

    [Fact]
    public async Task Demo_reset_endpoint_is_not_exposed_outside_development()
    {
        var response = await _client.PostAsync("/api/demo/reset", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NoContent);
        await using var db = fixture.CreateDbContext();
        (await db.Products.CountAsync()).Should().Be(SeedData.Products.Length);
    }

    [Fact]
    public async Task Order_responses_list_the_statuses_it_can_move_to()
    {
        var order = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));
        order.AllowedNextStatuses.Should().Equal(OrderStatus.Confirmed, OrderStatus.Cancelled);

        var page = await _client.GetFromJsonAsync<PagedResponse<OrderSummaryResponse>>("/api/orders", Json);
        page!.Items.Single().AllowedNextStatuses.Should().Equal(OrderStatus.Confirmed, OrderStatus.Cancelled);
    }

    // ---------------- Error contract ----------------

    [Theory]
    [InlineData(null, "IDEMPOTENCY_KEY_MISSING")]
    [InlineData("   ", "IDEMPOTENCY_KEY_MISSING")]
    public async Task Missing_idempotency_key_is_400(string? key, string errorCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(Order("CUST-1", (Indomie, 1)), options: Json)
        };
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(errorCode);
    }

    [Fact]
    public async Task Validation_errors_are_400_with_field_level_details()
    {
        var response = await _client.PostOrderAsync(new CreateOrderRequest
        {
            CustomerId = "",
            ShippingAddress = "",
            Items = [new CreateOrderItemRequest { ProductId = Guid.Empty, Quantity = 0 }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("errorCode").GetString().Should().Be("VALIDATION_FAILED");
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("customerId", out _).Should().BeTrue();
        errors.TryGetProperty("shippingAddress", out _).Should().BeTrue();
        errors.TryGetProperty("items[0].productId", out _).Should().BeTrue();
        errors.TryGetProperty("items[0].quantity", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_product_is_422()
    {
        var response = await _client.PostOrderAsync(Order("CUST-1", (Guid.NewGuid(), 1)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("UNKNOWN_PRODUCT");
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ErrorCodeAsync()).Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Every_error_carries_the_request_correlation_id()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-ID", "test-correlation-123");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-ID").Should().Equal("test-correlation-123");
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("correlationId").GetString().Should().Be("test-correlation-123");
        problem.GetProperty("status").GetInt32().Should().Be(404);
        problem.GetProperty("title").ValueKind.Should().Be(JsonValueKind.String);
    }
}
