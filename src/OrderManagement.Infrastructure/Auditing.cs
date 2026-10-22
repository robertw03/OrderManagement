using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderManagement.Domain;

namespace OrderManagement.Infrastructure;

/// <summary>Who is performing the current operation; written to txtInsertedBy / txtUpdatedBy.</summary>
public interface ICurrentUser
{
    string UserName { get; }
}

/// <summary>
/// Fills the audit columns for everything saved through SaveChanges. Set-based updates
/// (ExecuteUpdate / raw SQL) bypass the change tracker and set these columns themselves.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            Stamp(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            Stamp(context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.InsertedAt = now;
                    entry.Entity.InsertedBy = currentUser.UserName;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = currentUser.UserName;
                    break;
            }
        }
    }
}
