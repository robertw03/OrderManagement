using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain;
using static OrderManagement.Infrastructure.DatabaseNaming;

namespace OrderManagement.Infrastructure;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public const string ProductTable = "mProduct";
    public const string OrderTable = "trOrder";
    public const string OrderItemTable = "trOrderItem";
    public const string IdempotencyTable = "trIdempotencyKey";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable(ProductTable, t =>
            {
                // Last line of defense: even if application logic is wrong, the database
                // refuses to persist a negative stock.
                t.HasCheckConstraint($"CK_{ProductTable}_intStockQuantity", $"{Q("intStockQuantity")} >= 0");
                t.HasCheckConstraint($"CK_{ProductTable}_decPrice", $"{Q("decPrice")} >= 0");
            });
            b.Property(p => p.Name).HasMaxLength(200);
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.HasData(SeedData.Products);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable(OrderTable, t =>
            {
                t.HasCheckConstraint($"CK_{OrderTable}_txtStatus",
                    $"{Q("txtStatus")} IN ('Pending','Confirmed','Shipped','Delivered','Cancelled')");
                t.HasCheckConstraint($"CK_{OrderTable}_decTotalAmount", $"{Q("decTotalAmount")} >= 0");
            });
            b.Property(o => o.CustomerId).HasMaxLength(64);
            b.Property(o => o.ShippingAddress).HasMaxLength(500);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
            b.Property(o => o.Version).IsConcurrencyToken();
            b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);

            // Supports the list endpoint filters (status / customer + date range, newest first).
            b.HasIndex(o => new { o.Status, o.InsertedAt });
            b.HasIndex(o => new { o.CustomerId, o.InsertedAt });
            b.HasIndex(o => o.InsertedAt);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable(OrderItemTable, t =>
                t.HasCheckConstraint($"CK_{OrderItemTable}_intQuantity", $"{Q("intQuantity")} > 0"));
            b.Property(i => i.ProductName).HasMaxLength(200);
            b.Property(i => i.UnitPrice).HasPrecision(18, 2);
            b.Property(i => i.LineTotal).HasPrecision(18, 2);
            b.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(i => new { i.OrderId, i.ProductId }).IsUnique();
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable(IdempotencyTable);
            b.Property(r => r.CustomerId).HasMaxLength(64);
            b.Property(r => r.IdempotencyKey).HasMaxLength(100);
            b.Property(r => r.RequestHash).HasMaxLength(64);
            // This unique index is the lock that serializes concurrent requests with the same key.
            // Deliberately not partial on bolDeleted: a key must never be reusable.
            b.HasIndex(r => new { r.CustomerId, r.IdempotencyKey }).IsUnique();
            b.HasIndex(r => r.OrderId).IsUnique();
            b.HasIndex(r => r.InsertedAt);
        });

        ConfigureStandardColumns(modelBuilder);
        Apply(modelBuilder);
    }

    private static void ConfigureStandardColumns(ModelBuilder modelBuilder)
    {
        foreach (var type in new[] { typeof(Product), typeof(Order), typeof(OrderItem), typeof(IdempotencyRecord) })
        {
            var b = modelBuilder.Entity(type);
            // EF generates the UUID client-side (sequential, index-friendly) unless one is set explicitly.
            b.HasKey(nameof(AuditableEntity.Id));
            // Database defaults protect rows written by hand-written SQL. ValueGeneratedNever makes EF always
            // send the value, otherwise IsActive = false would be skipped and silently become true.
            b.Property(nameof(AuditableEntity.IsActive)).HasDefaultValue(true).ValueGeneratedNever();
            b.Property(nameof(AuditableEntity.IsDeleted)).HasDefaultValue(false).ValueGeneratedNever();
            b.Property(nameof(AuditableEntity.InsertedAt)).HasDefaultValueSql("now()").ValueGeneratedNever();
            b.Property(nameof(AuditableEntity.InsertedBy)).HasMaxLength(100).HasDefaultValue("").ValueGeneratedNever();
            b.Property(nameof(AuditableEntity.UpdatedBy)).HasMaxLength(100);
        }

        // Soft-deleted rows are invisible to every query unless IgnoreQueryFilters() is used.
        modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
        modelBuilder.Entity<OrderItem>().HasQueryFilter(i => !i.IsDeleted);
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(r => !r.IsDeleted);
    }
}
