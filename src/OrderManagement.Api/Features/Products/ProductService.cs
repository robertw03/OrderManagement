using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Products;

public sealed class ProductService(OrderDbContext db, ICurrentUser currentUser, TimeProvider clock, ILogger<ProductService> logger)
{
    public async Task<IReadOnlyList<ProductResponse>> ListAsync(CancellationToken ct) =>
        (await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct))
        .Select(ProductResponse.From)
        .ToList();

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct) =>
        ProductResponse.From(await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound(id));

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product
        {
            Name = request.Name.Trim(),
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            IsActive = request.IsActive
        };
        db.Products.Add(product);
        await SaveUniqueNameAsync(product.Name, ct);

        logger.LogInformation("Product {ProductId} '{ProductName}' created with stock {Stock}", product.Id, product.Name, product.StockQuantity);
        return ProductResponse.From(product);
    }

    /// <summary>
    /// Updates descriptive fields only. EF writes just the changed columns, so this never overwrites
    /// a stock value that orders changed concurrently.
    /// </summary>
    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound(id);
        product.Name = request.Name.Trim();
        product.Price = request.Price;
        product.IsActive = request.IsActive;
        await SaveUniqueNameAsync(product.Name, ct);

        logger.LogInformation("Product {ProductId} updated: name '{ProductName}', price {Price}, active {IsActive}",
            id, product.Name, product.Price, product.IsActive);
        return ProductResponse.From(product);
    }

    /// <summary>
    /// Adds or removes stock with one atomic statement: concurrent adjustments and order deductions
    /// all apply on top of each other instead of overwriting, and stock can never drop below zero.
    /// </summary>
    public async Task<ProductResponse> AdjustStockAsync(Guid id, AdjustStockRequest request, CancellationToken ct)
    {
        // ======================================================================================
        // DEMO BRANCH — RACE CONDITION #8 LEFT UNFIXED ON PURPOSE. Do not merge.
        // Read stock → compute the new absolute value in C# → write it back. Any order or adjustment that
        // commits between the read and the write is silently overwritten (lost update), and the
        // "not below zero" check runs on stale data.
        // Fix on `main`: one atomic UPDATE ... SET stock = stock + @delta WHERE stock + @delta >= 0.
        // ======================================================================================
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound(id);
        var newStock = product.StockQuantity + request.Quantity;
        if (newStock < 0)
        {
            throw new ConflictException(ErrorCodes.InsufficientStock, "Insufficient stock",
                    $"Cannot remove {-request.Quantity} unit(s): only {product.StockQuantity} in stock.")
                .With("productId", id)
                .With("available", product.StockQuantity);
        }

        await Task.Delay(50, ct); // stands in for real-world latency between the read and the write
        product.StockQuantity = newStock;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Stock of product {ProductId} adjusted by {Quantity} ({Reason})", id, request.Quantity, request.Reason ?? "-");
        return await GetAsync(id, ct);
    }

    /// <summary>Soft delete: the row stays for order history, but disappears from every query.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var deleted = await db.Products
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsDeleted, true)
                .SetProperty(p => p.IsActive, false)
                .SetProperty(p => p.UpdatedAt, clock.GetUtcNow())
                .SetProperty(p => p.UpdatedBy, currentUser.UserName), ct);

        if (deleted == 0) throw NotFound(id);
        logger.LogInformation("Product {ProductId} soft-deleted", id);
    }

    // The partial unique index on the name is the source of truth; checking first and then inserting
    // would let two concurrent creates with the same name both pass the check.
    private async Task SaveUniqueNameAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(ErrorCodes.ProductNameTaken, "Product name taken",
                $"A product named '{name}' already exists.");
        }
    }

    private static NotFoundException NotFound(Guid id) => new(ErrorCodes.ProductNotFound, $"Product {id} was not found.");
}
