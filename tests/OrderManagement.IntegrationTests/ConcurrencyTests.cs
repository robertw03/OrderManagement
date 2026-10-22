using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Features.Orders;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid ProductX = SeedData.ProductXId;
    private static readonly Guid Indomie = SeedData.IndomieId;
    private static readonly Guid Aqua = SeedData.AquaId;
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------- Scenario A: concurrent stock deduction ----------------

    [Fact]
    public async Task ScenarioA_two_orders_for_10_units_with_stock_15_only_one_succeeds()
    {
        await fixture.SetStockAsync(ProductX, 15);

        var responses = await RunConcurrentlyAsync(2, i =>
            _client.PostOrderAsync(Order($"CUST-A{i}", (ProductX, 10))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var rejected = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.ErrorCodeAsync()).Should().Be("INSUFFICIENT_STOCK");

        (await fixture.GetStockAsync(ProductX)).Should().Be(5);
        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ScenarioA_under_heavy_contention_stock_is_never_oversold()
    {
        await fixture.SetStockAsync(ProductX, 15);

        // 40 buyers fight over 15 units, 1 unit each.
        var responses = await RunConcurrentlyAsync(40, i =>
            _client.PostOrderAsync(Order($"CUST-H{i}", (ProductX, 1))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(15);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(25);
        (await fixture.GetStockAsync(ProductX)).Should().Be(0);

        await using var db = fixture.CreateDbContext();
        (await db.OrderItems.SumAsync(i => i.Quantity)).Should().Be(15);
    }

    [Fact]
    public async Task Multi_item_orders_locking_products_in_opposite_order_do_not_deadlock()
    {
        // Half the requests list product 2 first, the other half product 3 first. Without sorting
        // product ids inside the transaction these would deadlock and surface as errors.
        var responses = await RunConcurrentlyAsync(30, i => _client.PostOrderAsync(i % 2 == 0
            ? Order($"CUST-D{i}", (Indomie, 1), (Aqua, 1))
            : Order($"CUST-D{i}", (Aqua, 1), (Indomie, 1))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await fixture.GetStockAsync(Indomie)).Should().Be(500 - 30);
        (await fixture.GetStockAsync(Aqua)).Should().Be(300 - 30);
    }

    // ---------------- Scenario B: concurrent status update ----------------

    [Fact]
    public async Task ScenarioB_ship_and_cancel_at_the_same_time_exactly_one_wins()
    {
        // Repeat the race to catch both interleavings (Shipped first / Cancelled first).
        for (var round = 0; round < 10; round++)
        {
            await fixture.SetStockAsync(Indomie, 100);
            var order = await _client.CreateOrderAsync(Order($"CUST-B{round}", (Indomie, 10)));
            var confirm = await _client.PatchStatusAsync(order.Id, "Confirmed", expectedVersion: 1);
            confirm.StatusCode.Should().Be(HttpStatusCode.OK);

            var responses = await RunConcurrentlyAsync(2, i => i == 0
                ? _client.PatchStatusAsync(order.Id, "Shipped", expectedVersion: 2)
                : _client.CancelAsync(order.Id, expectedVersion: 2));

            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "only one admin may win");
            var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
            loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await loser.ErrorCodeAsync()).Should().BeOneOf("CONCURRENT_MODIFICATION", "VERSION_MISMATCH");

            var final = (await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}", Json))!;
            final.Version.Should().Be(3);

            // Stock must agree with whoever won: restored only if the cancel won.
            var expectedStock = final.Status == OrderManagement.Domain.OrderStatus.Cancelled ? 100 : 90;
            (await fixture.GetStockAsync(Indomie)).Should().Be(expectedStock);
        }
    }

    [Fact]
    public async Task Concurrent_cancels_restore_stock_only_once()
    {
        await fixture.SetStockAsync(Indomie, 100);
        var order = await _client.CreateOrderAsync(Order("CUST-CC", (Indomie, 10)));
        (await fixture.GetStockAsync(Indomie)).Should().Be(90);

        var responses = await RunConcurrentlyAsync(10, _ => _client.CancelAsync(order.Id));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict
                                       || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await fixture.GetStockAsync(Indomie)).Should().Be(100);
    }

    // ---------------- Scenario C: idempotent create under race ----------------

    [Fact]
    public async Task ScenarioC_same_idempotency_key_sent_concurrently_creates_exactly_one_order()
    {
        await fixture.SetStockAsync(Indomie, 100);
        var key = Guid.NewGuid().ToString();
        var request = Order("CUST-C", (Indomie, 3));

        var responses = await RunConcurrentlyAsync(10, _ => _client.PostOrderAsync(request, key));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var orders = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<OrderResponse>(Json)));
        orders.Select(o => o!.Id).Distinct().Should().ContainSingle("every retry must get the same order back");
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).Should().Be(1);

        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(1);
        (await fixture.GetStockAsync(Indomie)).Should().Be(97, "stock is deducted once, not once per retry");
    }

    [Fact]
    public async Task ScenarioC_same_key_with_different_payloads_concurrently_creates_one_order_and_rejects_the_other()
    {
        var key = Guid.NewGuid().ToString();

        var responses = await RunConcurrentlyAsync(2, i =>
            _client.PostOrderAsync(Order("CUST-C2", (Indomie, i + 1)), key));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var rejected = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await rejected.ErrorCodeAsync()).Should().Be("IDEMPOTENCY_KEY_REUSED");

        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(1);
    }
}
