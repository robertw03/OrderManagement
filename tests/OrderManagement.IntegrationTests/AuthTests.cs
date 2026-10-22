using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _anonymous = fixture.CreateAnonymousClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_returns_a_token_and_the_user()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("user").GetProperty("role").GetString().Should().Be("Admin");

        var me = await fixture.CreateClient("sales").GetFromJsonAsync<JsonElement>("/api/auth/me");
        me.GetProperty("username").GetString().Should().Be("sales");
    }

    [Theory]
    [InlineData("admin", "wrong-password")]
    [InlineData("nobody", "Admin123!")]
    public async Task Wrong_credentials_are_401_without_revealing_which_part_was_wrong(string username, string password)
    {
        var response = await _anonymous.PostAsJsonAsync("/api/auth/login", new { username, password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("errorCode").GetString().Should().Be("INVALID_CREDENTIALS");
        problem.GetProperty("detail").GetString().Should().Be("Username atau password salah.");
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        await using (var db = fixture.CreateDbContext())
            await db.Users.Where(u => u.Username == "admin2").ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

        var response = await _anonymous.PostAsJsonAsync("/api/auth/login", new { username = "admin2", password = "Admin123!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/orders")]
    [InlineData("GET", "/api/products")]
    [InlineData("POST", "/api/orders")]
    public async Task Api_requires_a_token(string method, string path)
    {
        var response = await _anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ErrorCodeAsync()).Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Sales_can_create_and_cancel_orders_but_not_move_them_through_fulfilment()
    {
        var sales = fixture.CreateClient("sales");
        var order = await sales.CreateOrderAsync(Order("CUST-1", (SeedData.IndomieId, 1)));

        var confirm = await sales.PatchStatusAsync(order.Id, "Confirmed");
        confirm.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await confirm.ErrorCodeAsync()).Should().Be("FORBIDDEN");

        (await sales.CancelAsync(order.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sales_cannot_manage_products()
    {
        var response = await fixture.CreateClient("sales").PostAsJsonAsync("/api/products",
            new { name = "Gula 1kg", price = 15000, stockQuantity = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
