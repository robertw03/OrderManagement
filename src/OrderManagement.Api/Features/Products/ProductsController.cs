using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Products;

public sealed record ProductResponse(Guid Id, string Name, int StockQuantity, decimal Price, bool IsActive);

/// <summary>Read-only product endpoints, mainly to observe stock while testing orders.</summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController(OrderDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ProductResponse>> List(CancellationToken ct) =>
        await db.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(p.Id, p.Name, p.StockQuantity, p.Price, p.IsActive))
            .ToListAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ProductResponse> GetById(Guid id, CancellationToken ct) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponse(p.Id, p.Name, p.StockQuantity, p.Price, p.IsActive))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(ErrorCodes.ProductNotFound, $"Product {id} was not found.");
}
