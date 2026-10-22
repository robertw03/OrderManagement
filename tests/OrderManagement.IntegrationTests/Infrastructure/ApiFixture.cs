using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderManagement.Infrastructure;

namespace OrderManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a real, throw-away PostgreSQL database. Concurrency behaviour depends on
/// the database's locking, so an in-memory provider or SQLite would make these tests meaningless.
///
/// Server connection comes from ORDERMGMT_TEST_CONNECTION (no database name needed); defaults to a
/// local PostgreSQL with postgres/postgres. A uniquely named database is created and dropped per run.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string DefaultServerConnection = "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private readonly string _serverConnection;
    private readonly string _databaseName = $"OrderManagement_Test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _factory;

    public ApiFixture()
    {
        _serverConnection = Environment.GetEnvironmentVariable("ORDERMGMT_TEST_CONNECTION") ?? DefaultServerConnection;
        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnection)
        {
            Database = _databaseName,
            // Concurrency tests open many connections at once.
            MaxPoolSize = 200
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:OrderDb", ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>Clears orders and puts every product back to its seeded stock.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "trOrderItem", "trOrder", "trIdempotencyKey" """);
        foreach (var product in SeedData.Products)
        {
            await db.Products.IgnoreQueryFilters().Where(p => p.Id == product.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, product.StockQuantity)
                    .SetProperty(p => p.IsActive, true)
                    .SetProperty(p => p.IsDeleted, false));
        }
    }

    public async Task SetStockAsync(Guid productId, int stock)
    {
        await using var db = CreateDbContext();
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, stock));
    }

    public async Task<int> GetStockAsync(Guid productId)
    {
        await using var db = CreateDbContext();
        return await db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    public async Task SetProductFlagsAsync(Guid productId, bool isActive, bool isDeleted)
    {
        await using var db = CreateDbContext();
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, isActive)
                .SetProperty(p => p.IsDeleted, isDeleted));
    }

    public OrderDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
