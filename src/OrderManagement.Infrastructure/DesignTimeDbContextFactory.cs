using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderManagement.Infrastructure;

/// <summary>Used only by `dotnet ef` to create migrations without booting the API.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ORDERMGMT_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=OrderManagement;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderDbContext(options);
    }
}
