using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the DbContext. The host must also register <see cref="ICurrentUser"/> and <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<OrderDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));

        return services;
    }
}
