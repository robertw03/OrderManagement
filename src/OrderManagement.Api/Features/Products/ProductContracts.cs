using FluentValidation;
using OrderManagement.Domain;

namespace OrderManagement.Api.Features.Products;

public sealed class CreateProductRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>Name, price and active flag only — stock changes go through stock adjustments (see README §7).</summary>
public sealed class UpdateProductRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>Relative stock change: positive for incoming goods, negative for corrections.</summary>
public sealed class AdjustStockRequest
{
    public int Quantity { get; init; }
    public string? Reason { get; init; }
}

public sealed record ProductResponse(
    Guid Id,
    string Name,
    int StockQuantity,
    decimal Price,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy)
{
    public static ProductResponse From(Product p) =>
        new(p.Id, p.Name, p.StockQuantity, p.Price, p.IsActive, p.InsertedAt, p.InsertedBy, p.UpdatedAt, p.UpdatedBy);
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("'Name' must not be empty.").MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.StockQuantity).InclusiveBetween(0, 1_000_000);
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("'Name' must not be empty.").MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Quantity).NotEqual(0).InclusiveBetween(-1_000_000, 1_000_000);
        RuleFor(x => x.Reason).MaximumLength(200);
    }
}
