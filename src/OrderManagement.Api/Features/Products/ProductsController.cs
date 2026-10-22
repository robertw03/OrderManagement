using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Features.Auth;

namespace OrderManagement.Api.Features.Products;

/// <summary>Product master data. Anyone signed in can read; only admins can change products.</summary>
[ApiController]
[Route("api/products")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProductResponse>> List(CancellationToken ct) => products.ListAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ProductResponse> GetById(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<ProductResponse> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct) =>
        products.UpdateAsync(id, request, ct);

    /// <summary>Add (positive) or remove (negative) stock atomically.</summary>
    [HttpPost("{id:guid}/stock-adjustments")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<ProductResponse> AdjustStock(Guid id, [FromBody] AdjustStockRequest request, CancellationToken ct) =>
        products.AdjustStockAsync(id, request, ct);

    /// <summary>Soft-delete a product. Existing orders keep their snapshot of it.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }
}
