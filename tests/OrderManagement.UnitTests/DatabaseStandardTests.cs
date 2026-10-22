using Microsoft.EntityFrameworkCore;
using OrderManagement.Infrastructure;

namespace OrderManagement.UnitTests;

/// <summary>
/// Guards the KALBE KIOS database standard: m/tr table prefixes, uid…UUID primary keys,
/// type-prefixed columns and the mandatory audit columns on every table.
/// </summary>
public sealed class DatabaseStandardTests
{
    private static readonly string[] StandardColumns =
        ["bolActive", "bolDeleted", "dtInserted", "txtInsertedBy", "dtUpdated", "txtUpdatedBy"];

    private static readonly string[] AllowedPrefixes = ["uid", "txt", "int", "dec", "bol", "dt"];

    private static readonly OrderDbContext Db = new(new DbContextOptionsBuilder<OrderDbContext>()
        .UseNpgsql("Host=unused")
        .Options);

    public static TheoryData<string> Tables()
    {
        var data = new TheoryData<string>();
        foreach (var entity in Db.Model.GetEntityTypes())
            data.Add(entity.GetTableName()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void Table_follows_the_standard(string table)
    {
        var entity = Db.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var columns = entity.GetProperties().Select(p => p.GetColumnName()).ToList();

        table.Should().MatchRegex("^(m|tr)[A-Z][A-Za-z]+$", "master tables start with m, transaction tables with tr");

        var stem = table.StartsWith("tr") ? table[2..] : table[1..];
        entity.FindPrimaryKey()!.Properties.Should().ContainSingle()
            .Which.GetColumnName().Should().Be($"uid{stem}UUID");
        entity.FindPrimaryKey()!.Properties[0].ClrType.Should().Be<Guid>();

        columns.Should().Contain(StandardColumns);
        columns.Should().OnlyContain(c => AllowedPrefixes.Any(c.StartsWith));
    }

    [Fact]
    public void Expected_tables_exist()
    {
        Db.Model.GetEntityTypes().Select(e => e.GetTableName())
            .Should().BeEquivalentTo("mProduct", "trOrder", "trOrderItem", "trIdempotencyKey");
    }
}
