namespace VendemeFacil.Api.Domain;

public sealed class Tenant : Entity
{
    public required string Name { get; set; }
    public string Slug { get; set; } = string.Empty;
    public OperationMode OperationMode { get; set; } = OperationMode.Simple;
    public string CurrencyCode { get; set; } = "MXN";
    public string TimeZoneId { get; set; } = "America/Matamoros";
    public string PrimaryColor { get; set; } = "#153f35";
    public string AccentColor { get; set; } = "#f5c45e";
    public string ButtonColor { get; set; } = "#196651";
    public string HoverColor { get; set; } = "#124f3f";
    public string BackgroundColor { get; set; } = "#f4f5ef";
    public string SurfaceColor { get; set; } = "#ffffff";
    public string TextColor { get; set; } = "#17251f";
    public int CornerRadius { get; set; } = 12;
    public int LayawayReminderDaysBefore { get; set; } = 3;
    public bool AllowNegativeStock { get; set; }
    public string? LogoUrl { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TicketMessage { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Branch> Branches { get; set; } = [];
}

public sealed class Branch : TenantEntity
{
    public required string Name { get; set; }
    public string? Address { get; set; }
    public bool IsMain { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant Tenant { get; set; } = null!;
}

public sealed class AppUser : TenantEntity
{
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Cashier;
    public bool CanViewCosts { get; set; }
    public bool IsActive { get; set; } = true;
    public int SecurityVersion { get; set; }
}

public sealed class PasswordResetToken : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public AppUser User { get; set; } = null!;
}

public sealed class ProspectLead : Entity
{
    public required string ContactName { get; set; }
    public required string BusinessName { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? BusinessType { get; set; }
    public string? PreferredContactTime { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "New";
}

public sealed class Customer : TenantEntity
{
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
