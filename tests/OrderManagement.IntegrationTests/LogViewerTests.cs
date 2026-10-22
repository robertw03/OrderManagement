using System.Net;
using System.Net.Http.Json;
using OrderManagement.Api.Features.Logs;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Infrastructure;
using static OrderManagement.IntegrationTests.Infrastructure.TestHelpers;

namespace OrderManagement.IntegrationTests;

/// <summary>The IT workflow: a user reports a code from an error toast, IT finds that request's logs.</summary>
[Collection(ApiCollection.Name)]
public sealed class LogViewerTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _admin = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Logs_of_a_failed_request_can_be_found_by_its_report_code()
    {
        await fixture.SetStockAsync(SeedData.ProductXId, 1);
        var code = $"T{Guid.NewGuid():N}"[..12];

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(Order("CUST-LOG", (SeedData.ProductXId, 5)), options: Json)
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Correlation-ID", code);
        (await fixture.CreateClient("sales").SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var logs = await _admin.GetFromJsonAsync<LogPageResponse>($"/api/logs?code={code}", Json);

        logs!.Items.Should().NotBeEmpty();
        logs.Items.Should().OnlyContain(e => e.CorrelationId == code && e.UserName == "sales");
        logs.Items.Should().Contain(e => e.Level == "Warning" && e.Message.Contains("requested 5, available 1"));
        logs.Items.Should().BeInAscendingOrder(e => e.Timestamp, "a report code reads as the story of one request");
    }

    [Fact]
    public async Task Only_admins_can_read_logs()
    {
        (await fixture.CreateClient("sales").GetAsync("/api/logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateAnonymousClient().GetAsync("/api/logs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_level_is_a_validation_error()
    {
        var response = await _admin.GetAsync("/api/logs?level=Loud");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_day_without_a_log_file_returns_an_empty_page()
    {
        var logs = await _admin.GetFromJsonAsync<LogPageResponse>("/api/logs?date=2000-01-01", Json);

        logs!.TotalCount.Should().Be(0);
        logs.Items.Should().BeEmpty();
    }
}
