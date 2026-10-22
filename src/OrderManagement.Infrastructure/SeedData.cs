using OrderManagement.Domain;

namespace OrderManagement.Infrastructure;

public static class SeedData
{
    private const string SeedUser = "seed";
    private static readonly DateTimeOffset SeedDate = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

    public static readonly Guid ProductXId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    public static readonly Guid IndomieId = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    public static readonly Guid AquaId = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    public static readonly Guid BerasId = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    public static readonly Guid MinyakId = Guid.Parse("a1000000-0000-4000-8000-000000000005");

    /// <summary>
    /// "Product X" starts with 15 units to reproduce Scenario A:
    /// two concurrent orders of 10 units each must not both succeed.
    /// </summary>
    public static readonly Product[] Products =
    [
        Seed(ProductXId, "Product X", 15, 100_000m),
        Seed(IndomieId, "Indomie Goreng (1 dus)", 500, 115_000m),
        Seed(AquaId, "Aqua 600ml (1 dus)", 300, 52_000m),
        Seed(BerasId, "Beras Premium 5kg", 120, 78_500m),
        Seed(MinyakId, "Minyak Goreng 2L", 200, 38_000m)
    ];

    private static Product Seed(Guid id, string name, int stock, decimal price) => new()
    {
        Id = id,
        Name = name,
        StockQuantity = stock,
        Price = price,
        IsActive = true,
        IsDeleted = false,
        InsertedAt = SeedDate,
        InsertedBy = SeedUser
    };
}
