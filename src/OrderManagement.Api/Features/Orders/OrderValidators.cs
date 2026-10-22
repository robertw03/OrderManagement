using FluentValidation;

namespace OrderManagement.Api.Features.Orders;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxItems = 100;
    public const int MaxQuantityPerItem = 10_000;

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.ShippingAddress)
            .Must(a => !string.IsNullOrWhiteSpace(a)).WithMessage("'Shipping Address' must not be empty.")
            .MaximumLength(500);

        RuleFor(x => x.Items)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("An order must contain at least one item.")
            .Must(items => items.Count <= MaxItems).WithMessage($"An order may contain at most {MaxItems} items.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, MaxQuantityPerItem);
        });
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().IsInEnum();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    public const int MaxPageSize = 100;

    public ListOrdersQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.CustomerId).MaximumLength(64);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.To)
            .GreaterThan(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("'To' must be later than 'From'.");
    }
}
