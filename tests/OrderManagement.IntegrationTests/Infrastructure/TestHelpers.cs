using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderManagement.Api.Features.Orders;

namespace OrderManagement.IntegrationTests.Infrastructure;

public static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static CreateOrderRequest Order(string customerId, params (Guid ProductId, int Quantity)[] items) => new()
    {
        CustomerId = customerId,
        ShippingAddress = "Jl. Asia Afrika No. 1, Bandung",
        Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };

    public static Task<HttpResponseMessage> PostOrderAsync(this HttpClient client, CreateOrderRequest request, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }

    public static async Task<OrderResponse> CreateOrderAsync(this HttpClient client, CreateOrderRequest request)
    {
        var response = await client.PostOrderAsync(request);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create order failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
    }

    public static Task<HttpResponseMessage> PatchStatusAsync(this HttpClient client, Guid orderId, string status, int? expectedVersion = null) =>
        client.PatchAsJsonAsync($"/api/orders/{orderId}/status", new { status, expectedVersion }, Json);

    public static Task<HttpResponseMessage> CancelAsync(this HttpClient client, Guid orderId, int? expectedVersion = null) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { expectedVersion }, Json);

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadProblemAsync()).GetProperty("errorCode").GetString()!;

    /// <summary>
    /// Runs <paramref name="count"/> operations that are all released at the same instant, maximizing
    /// the chance that their database work overlaps.
    /// </summary>
    public static async Task<T[]> RunConcurrentlyAsync<T>(int count, Func<int, Task<T>> operation)
    {
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(async () =>
            {
                await startGate.Task;
                return await operation(i);
            }))
            .ToArray();

        startGate.SetResult();
        return await Task.WhenAll(tasks);
    }
}
