using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Features.Orders;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// Second layer behind the Idempotency-Key: identical orders sent with <em>different</em> keys
/// (a client generating a key per click, or a click after the first order succeeded).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DuplicateOrderTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid Indomie = SeedData.IndomieId;
    private readonly HttpClient _client = fixture.CreateClient("sales");

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static CreateOrderRequest Confirmed(CreateOrderRequest request) => new()
    {
        CustomerId = request.CustomerId,
        ShippingAddress = request.ShippingAddress,
        Items = request.Items,
        ConfirmDuplicate = true
    };

    [Fact]
    public async Task Identical_order_with_a_new_key_is_flagged_and_can_be_confirmed()
    {
        var first = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 3)));

        var second = await _client.PostOrderAsync(Order("CUST-1", (Indomie, 3)));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.ReadProblemAsync();
        problem.GetProperty("errorCode").GetString().Should().Be("POSSIBLE_DUPLICATE_ORDER");
        problem.GetProperty("duplicateOfOrderId").GetGuid().Should().Be(first.Id);
        problem.GetProperty("windowSeconds").GetInt32().Should().Be(60);
        (await fixture.GetStockAsync(Indomie)).Should().Be(497, "the flagged order must not deduct stock");

        // The customer really wants a second, identical order.
        var confirmed = await _client.PostOrderAsync(Confirmed(Order("CUST-1", (Indomie, 3))));

        confirmed.StatusCode.Should().Be(HttpStatusCode.Created);
        (await confirmed.Content.ReadFromJsonAsync<OrderResponse>(Json))!.Id.Should().NotBe(first.Id);
        (await fixture.GetStockAsync(Indomie)).Should().Be(494);
    }

    [Fact]
    public async Task Retrying_the_flagged_request_with_the_same_key_and_confirmation_succeeds()
    {
        await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));
        var key = Guid.NewGuid().ToString();

        (await _client.PostOrderAsync(Order("CUST-1", (Indomie, 1)), key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var retry = await _client.PostOrderAsync(Confirmed(Order("CUST-1", (Indomie, 1))), key);

        retry.StatusCode.Should().Be(HttpStatusCode.Created, "the flagged attempt rolled back and did not keep the key");
    }

    [Fact]
    public async Task Orders_that_differ_or_belong_to_another_customer_are_not_flagged()
    {
        await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));

        (await _client.PostOrderAsync(Order("CUST-1", (Indomie, 2)))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostOrderAsync(Order("CUST-2", (Indomie, 1)))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_cancelled_order_does_not_block_ordering_the_same_again()
    {
        var first = await _client.CreateOrderAsync(Order("CUST-1", (Indomie, 1)));
        (await _client.CancelAsync(first.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostOrderAsync(Order("CUST-1", (Indomie, 1)))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Identical_orders_with_different_keys_sent_concurrently_create_exactly_one_order()
    {
        // Ten clicks of a client that (wrongly) generates a new key per click, all in flight at once.
        var responses = await RunConcurrentlyAsync(10, _ => _client.PostOrderAsync(Order("CUST-1", (Indomie, 2))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await rejected.ErrorCodeAsync()).Should().Be("POSSIBLE_DUPLICATE_ORDER");
        }

        await using var db = fixture.CreateDbContext();
        (await db.Orders.CountAsync()).Should().Be(1);
        (await fixture.GetStockAsync(Indomie)).Should().Be(498);
    }
}
