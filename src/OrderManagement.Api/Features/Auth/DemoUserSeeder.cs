using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Auth;

/// <summary>
/// Creates demo accounts when the user table is empty. Enabled by Database:SeedDemoUsers
/// (Development and tests only) — production accounts are provisioned separately.
/// </summary>
public static class DemoUserSeeder
{
    public static readonly (string Username, string DisplayName, UserRole Role, string Password)[] Accounts =
    [
        ("admin", "Admin Gudang", UserRole.Admin, "Admin123!"),
        ("admin2", "Admin Pengiriman", UserRole.Admin, "Admin123!"),
        ("sales", "Sales Budi", UserRole.Sales, "Sales123!")
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(ct))
            return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        foreach (var (username, displayName, role, password) in Accounts)
        {
            var user = new User { Username = username, DisplayName = displayName, Role = role };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);
    }
}
