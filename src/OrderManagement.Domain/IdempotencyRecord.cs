namespace OrderManagement.Domain;

/// <summary>
/// Claims an Idempotency-Key for a customer. The unique index on (CustomerId, IdempotencyKey) is what
/// serializes concurrent requests carrying the same key (see README, Scenario C).
/// </summary>
public class IdempotencyRecord : AuditableEntity
{
    public string CustomerId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>SHA-256 of the normalized request body, used to detect key reuse with a different payload.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public Guid OrderId { get; set; }
}
