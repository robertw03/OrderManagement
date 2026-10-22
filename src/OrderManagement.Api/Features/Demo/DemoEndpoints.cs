using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Features.Auth;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Demo;

/// <summary>
/// Development-only helpers for the demo UI, so concurrency scenarios can be replayed from a known state.
/// Never mapped outside the Development environment.
/// </summary>
public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var demo = endpoints.MapGroup("/api/demo").RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        demo.MapPost("/reset", ResetAsync)
            .WithTags("Demo")
            .WithSummary("Delete all orders and restore seeded product stock (Development only).");

        demo.MapPut("/products/{id:guid}/stock", SetStockAsync)
            .WithTags("Demo")
            .WithSummary("Set a product's stock, e.g. back to 15 before replaying Scenario A (Development only).");
    }

    public sealed record SetStockRequest(int StockQuantity);

    private static async Task<IResult> SetStockAsync(
        Guid id, SetStockRequest request, OrderDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (request.StockQuantity < 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["stockQuantity"] = ["Stock must not be negative."] },
                extensions: new Dictionary<string, object?> { ["errorCode"] = "VALIDATION_FAILED" });
        }

        var updated = await db.Products
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, request.StockQuantity)
                .SetProperty(p => p.UpdatedAt, clock.GetUtcNow())
                .SetProperty(p => p.UpdatedBy, currentUser.UserName), ct);

        return updated == 0
            ? throw new NotFoundException(ErrorCodes.ProductNotFound, $"Product {id} was not found.")
            : Results.NoContent();
    }

    private static async Task<IResult> ResetAsync(OrderDbContext db, ILoggerFactory loggers, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "trOrderItem", "trOrder", "trIdempotencyKey" """, ct);
        foreach (var product in SeedData.Products)
        {
            await db.Products.IgnoreQueryFilters()
                .Where(p => p.Id == product.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, product.StockQuantity)
                    .SetProperty(p => p.IsActive, true)
                    .SetProperty(p => p.IsDeleted, false), ct);
        }
        await tx.CommitAsync(ct);

        loggers.CreateLogger(nameof(DemoEndpoints)).LogWarning("Demo data reset: all orders deleted, product stock restored");
        return Results.NoContent();
    }
}
