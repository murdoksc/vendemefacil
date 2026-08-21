namespace VendemeFacil.Api.Domain;

public sealed class AuditLog : TenantEntity
{
    public required string Action { get; set; }
    public required string Description { get; set; }
    public string? DetailsJson { get; set; }
    public Guid PerformedByUserId { get; set; }
    public AppUser PerformedByUser { get; set; } = null!;
    public Guid BranchId { get; set; }
    public DateTimeOffset ClientCreatedAtUtc { get; set; }
    public required string IpAddress { get; set; }
}
