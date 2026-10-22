using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OrderManagement.Infrastructure;

/// <summary>
/// Applies the database naming standard to the whole model so individual entity configurations
/// do not have to repeat it:
/// <list type="bullet">
/// <item>Tables: <c>m</c> prefix for master data, <c>tr</c> for transactions (set per entity), e.g. <c>mProduct</c>, <c>trOrder</c>.</item>
/// <item>Primary key: <c>uid{Table without prefix}UUID</c>, foreign keys <c>uid{Referenced}UUID</c>.</item>
/// <item>Columns are prefixed by type: <c>txt</c> string/enum, <c>int</c>, <c>dec</c>, <c>bol</c>, <c>dt</c>, <c>uid</c>.</item>
/// <item>Constraints: <c>PK_{table}</c>, <c>FK_{table}_{referenced}</c>, <c>IX_/UX_{table}_{columns}</c>.</item>
/// </list>
/// </summary>
public static class DatabaseNaming
{
    private static readonly Dictionary<string, string> StandardColumns = new()
    {
        [nameof(Domain.AuditableEntity.IsActive)] = "bolActive",
        [nameof(Domain.AuditableEntity.IsDeleted)] = "bolDeleted",
        [nameof(Domain.AuditableEntity.InsertedAt)] = "dtInserted",
        [nameof(Domain.AuditableEntity.InsertedBy)] = "txtInsertedBy",
        [nameof(Domain.AuditableEntity.UpdatedAt)] = "dtUpdated",
        [nameof(Domain.AuditableEntity.UpdatedBy)] = "txtUpdatedBy"
    };

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()
                ?? throw new InvalidOperationException($"{entity.ClrType.Name} has no table name.");

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ColumnName(TableStem(table), property));

            entity.FindPrimaryKey()?.SetName($"PK_{table}");

            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.SetConstraintName($"FK_{table}_{foreignKey.PrincipalEntityType.GetTableName()}");

            foreach (var index in entity.GetIndexes())
            {
                var columns = string.Join("_", index.Properties.Select(p => p.GetColumnName()));
                index.SetDatabaseName($"{(index.IsUnique ? "UX" : "IX")}_{table}_{columns}");
            }
        }
    }

    /// <summary>The quoted identifier for a column, for use in raw SQL and check constraints.</summary>
    public static string Q(string identifier) => $"\"{identifier}\"";

    // "mProduct" -> "Product", "trOrderItem" -> "OrderItem"
    private static string TableStem(string table)
    {
        if (table.StartsWith("tr") && char.IsUpper(table[2])) return table[2..];
        if (table.StartsWith('m') && char.IsUpper(table[1])) return table[1..];
        return table;
    }

    private static string ColumnName(string tableStem, IMutableProperty property)
    {
        if (StandardColumns.TryGetValue(property.Name, out var standard))
            return standard;

        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (type == typeof(Guid))
        {
            if (property.Name == "Id") return $"uid{tableStem}UUID";
            var stem = property.Name.EndsWith("Id") ? property.Name[..^2] : property.Name;
            return $"uid{stem}UUID";
        }

        var prefix = type switch
        {
            _ when type == typeof(string) || type.IsEnum => "txt",
            _ when type == typeof(int) || type == typeof(long) => "int",
            _ when type == typeof(decimal) => "dec",
            _ when type == typeof(bool) => "bol",
            _ when type == typeof(DateTimeOffset) || type == typeof(DateTime) => "dt",
            _ => throw new NotSupportedException($"No column prefix defined for {type.Name} ({property.DeclaringType.ClrType.Name}.{property.Name}).")
        };
        return prefix + property.Name;
    }
}
