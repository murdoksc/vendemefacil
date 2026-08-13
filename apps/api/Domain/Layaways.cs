namespace VendemeFacil.Api.Domain;

public sealed class Layaway : TenantEntity
{
    public required string Folio { get; set; }
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DueAtUtc { get; set; }
    public LayawayStatus Status { get; set; } = LayawayStatus.Active;
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public ICollection<LayawayItem> Items { get; set; } = [];
    public ICollection<LayawayPayment> Payments { get; set; } = [];
}

public sealed class LayawayItem : TenantEntity
{
    public Guid LayawayId { get; set; }
    public Layaway Layaway { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public required string ProductName { get; set; }
    public required string VariantName { get; set; }
    public required string Sku { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class LayawayPayment : TenantEntity
{
    public Guid LayawayId { get; set; }
    public Layaway Layaway { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid ReceivedByUserId { get; set; }
    public Guid? CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }
    public string? Note { get; set; }
}
