using OrderManagement.Api.Features.Orders;

namespace OrderManagement.UnitTests;

public sealed class CreateOrderTests
{
    private static readonly Guid P1 = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid P3 = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private readonly CreateOrderRequestValidator _validator = new();

    private static CreateOrderRequest Valid() => new()
    {
        CustomerId = "CUST-1",
        ShippingAddress = "Jl. Asia Afrika 1",
        Items = [new CreateOrderItemRequest { ProductId = P1, Quantity = 1 }]
    };

    [Fact]
    public void Valid_request_passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_items_fail()
    {
        var result = _validator.Validate(new CreateOrderRequest { CustomerId = "C", ShippingAddress = "A", Items = [] });
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Items");
    }

    [Fact]
    public void Null_items_fail_without_throwing()
    {
        var result = _validator.Validate(new CreateOrderRequest { CustomerId = "C", ShippingAddress = "A", Items = null! });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CreateOrderRequestValidator.MaxQuantityPerItem + 1)]
    public void Quantity_out_of_range_fails(int quantity)
    {
        var request = Valid();
        request.Items[0] = new CreateOrderItemRequest { ProductId = P1, Quantity = quantity };
        _validator.Validate(request).Errors.Should().ContainSingle(e => e.PropertyName == "Items[0].Quantity");
    }

    [Fact]
    public void Whitespace_shipping_address_fails()
    {
        var request = new CreateOrderRequest { CustomerId = "C", ShippingAddress = "   ", Items = Valid().Items };
        _validator.Validate(request).Errors.Should().ContainSingle(e => e.PropertyName == "ShippingAddress");
    }

    [Fact]
    public void Request_hash_is_deterministic_and_sensitive_to_every_field()
    {
        var baseline = RequestHasher.Hash("C1", "Addr", [(P1, 2), (P3, 4)]);

        RequestHasher.Hash("C1", "Addr", [(P1, 2), (P3, 4)]).Should().Be(baseline);
        RequestHasher.Hash("C2", "Addr", [(P1, 2), (P3, 4)]).Should().NotBe(baseline);
        RequestHasher.Hash("C1", "Addr 2", [(P1, 2), (P3, 4)]).Should().NotBe(baseline);
        RequestHasher.Hash("C1", "Addr", [(P1, 2), (P3, 5)]).Should().NotBe(baseline);
        baseline.Should().HaveLength(64);
    }
}
