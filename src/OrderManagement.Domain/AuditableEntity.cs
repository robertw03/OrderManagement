namespace OrderManagement.Domain;

/// <summary>
/// Standard columns every table carries: UUID primary key, active / soft-delete flags and audit stamps.
/// Mapped to uid{Table}UUID, bolActive, bolDeleted, dtInserted, txtInsertedBy, dtUpdated, txtUpdatedBy.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset InsertedAt { get; set; }
    public string InsertedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
