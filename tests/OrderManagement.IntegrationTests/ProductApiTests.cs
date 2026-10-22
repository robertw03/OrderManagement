using System.Net;
using System.Net.Http.Json;
using OrderManagement.Api.Features.Products;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProductApiTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _admin = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ProductResponse> CreateProductAsync(string name, int stock = 10, decimal price = 25_000m)
    {
        var response = await _admin.PostAsJsonAsync("/api/products", new { name, price, stockQuantity = stock }, Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(Json))!;
    }

    [Fact]
    public async Task Admin_can_add_a_product_and_it_can_be_ordered()
    {
        var product = await CreateProductAsync("Gula Pasir 1kg", stock: 40, price: 17_500m);

        product.CreatedBy.Should().Be("admin");
        product.IsActive.Should().BeTrue();

        var order = await _admin.CreateOrderAsync(Order("CUST-1", (product.Id, 4)));
        order.TotalAmount.Should().Be(70_000m);
        (await fixture.GetStockAsync(product.Id)).Should().Be(36);
    }

    [Fact]
    public async Task Update_changes_name_and_price_without_touching_stock()
    {
        var product = await CreateProductAsync("Teh Celup", stock: 10);
        await _admin.CreateOrderAsync(Order("CUST-1", (product.Id, 3)));

        var response = await _admin.PutAsJsonAsync($"/api/products/{product.Id}",
            new { name = "Teh Celup 25s", price = 9_000m, isActive = true }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<ProductResponse>(Json))!;
        updated.Name.Should().Be("Teh Celup 25s");
        updated.StockQuantity.Should().Be(7, "the edit must not overwrite the stock deducted by the order");
        updated.UpdatedBy.Should().Be("admin");
    }

    [Fact]
    public async Task Duplicate_product_name_is_409()
    {
        await CreateProductAsync("Kopi Bubuk");

        var response = await _admin.PostAsJsonAsync("/api/products", new { name = "Kopi Bubuk", price = 1, stockQuantity = 1 }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("PRODUCT_NAME_TAKEN");
    }

    [Fact]
    public async Task Deleted_product_disappears_and_its_name_can_be_reused()
    {
        var product = await CreateProductAsync("Susu UHT");

        (await _admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _admin.GetAsync($"/api/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var order = await _admin.PostOrderAsync(Order("CUST-1", (product.Id, 1)));
        (await order.ErrorCodeAsync()).Should().Be("UNKNOWN_PRODUCT");
        await CreateProductAsync("Susu UHT");
    }

    [Fact]
    public async Task Stock_adjustment_cannot_take_stock_below_zero()
    {
        var product = await CreateProductAsync("Sabun Mandi", stock: 5);

        var add = await _admin.PostAsJsonAsync($"/api/products/{product.Id}/stock-adjustments", new { quantity = 20, reason = "Barang masuk" });
        (await add.Content.ReadFromJsonAsync<ProductResponse>(Json))!.StockQuantity.Should().Be(25);

        var remove = await _admin.PostAsJsonAsync($"/api/products/{product.Id}/stock-adjustments", new { quantity = -30 });
        remove.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await remove.ErrorCodeAsync()).Should().Be("INSUFFICIENT_STOCK");
        (await fixture.GetStockAsync(product.Id)).Should().Be(25);
    }

    [Fact]
    public async Task Invalid_product_is_400()
    {
        var response = await _admin.PostAsJsonAsync("/api/products", new { name = " ", price = -1, stockQuantity = -5 }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.ReadProblemAsync()).GetProperty("errors");
        errors.TryGetProperty("name", out _).Should().BeTrue();
        errors.TryGetProperty("price", out _).Should().BeTrue();
        errors.TryGetProperty("stockQuantity", out _).Should().BeTrue();
    }

    // ---------------- concurrency on product master data ----------------

    [Fact]
    public async Task Concurrent_creates_with_the_same_name_produce_exactly_one_product()
    {
        var responses = await RunConcurrentlyAsync(8, _ =>
            _admin.PostAsJsonAsync("/api/products", new { name = "Mie Instan Cup", price = 6_000, stockQuantity = 50 }, Json));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_restock_and_orders_never_lose_an_update()
    {
        var product = await CreateProductAsync("Kecap Manis", stock: 100);

        // 20 orders take 1 unit each while 20 restocks add 1 unit each, all at the same time.
        var responses = await RunConcurrentlyAsync(40, i => i % 2 == 0
            ? _admin.PostOrderAsync(Order($"CUST-{i}", (product.Id, 1)))
            : _admin.PostAsJsonAsync($"/api/products/{product.Id}/stock-adjustments", new { quantity = 1 }));

        responses.Should().OnlyContain(r => r.IsSuccessStatusCode);
        (await fixture.GetStockAsync(product.Id)).Should().Be(100);
    }

    [Fact]
    public async Task Concurrent_stock_removals_never_go_negative()
    {
        var product = await CreateProductAsync("Tepung Terigu", stock: 10);

        var responses = await RunConcurrentlyAsync(25, _ =>
            _admin.PostAsJsonAsync($"/api/products/{product.Id}/stock-adjustments", new { quantity = -1 }));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(10);
        (await fixture.GetStockAsync(product.Id)).Should().Be(0);
    }
}
