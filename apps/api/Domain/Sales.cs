namespace VendemeFacil.Api.Domain;

public sealed class CashSession : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public decimal OpeningAmount { get; set; }
    public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public decimal? CountedAmount { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public decimal? DifferenceAmount { get; set; }
}

public sealed class Sale : TenantEntity
{
    public required string Folio { get; set; }
    public Guid BranchId { get; set; }
    public Guid CashSessionId { get; set; }
    public Guid SoldByUserId { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTimeOffset SoldAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public ICollection<SaleItem> Items { get; set; } = [];
    public ICollection<SalePayment> Payments { get; set; } = [];
}

public sealed class SaleItem : TenantEntity
{
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public required string ProductName { get; set; }
    public required string VariantName { get; set; }
    public required string Sku { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    public decimal ReturnedQuantity { get; set; }
}

public sealed class SalePayment : TenantEntity
{
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal ChangeAmount { get; set; }
}
