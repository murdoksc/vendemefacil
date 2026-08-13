namespace VendemeFacil.Api.Domain;

public sealed class Category : TenantEntity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product : TenantEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ProductVariant> Variants { get; set; } = [];
}

public sealed class ProductVariant : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string Name { get; set; }
    public required string Sku { get; set; }
    public string? Barcode { get; set; }
    public decimal Cost { get; set; }
    public decimal Price { get; set; }
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class InventoryBalance : TenantEntity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
}

public sealed class InventoryMovement : TenantEntity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public InventoryMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Note { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public AppUser? PerformedByUser { get; set; }
}
