namespace OrderManagement.Domain;

public enum UserRole
{
    /// <summary>Manages products and moves orders through their lifecycle.</summary>
    Admin,

    /// <summary>Creates and cancels orders.</summary>
    Sales
}

public class User : AuditableEntity
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}
