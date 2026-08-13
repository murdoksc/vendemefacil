namespace VendemeFacil.Api.Contracts;

public sealed record CreateProductRequest(
    string Name,
    Guid? CategoryId,
    string? ImageUrl,
    string VariantName,
    string Sku,
    string? Barcode,
    decimal Cost = 0,
    decimal Price = 0,
    decimal MinimumStock = 0,
    decimal InitialStock = 0,
    Guid? BranchId = null);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Category,
    Guid? CategoryId,
    string? ImageUrl,
    Guid VariantId,
    string Variant,
    string Sku,
    string? Barcode,
    decimal Cost,
    decimal Price,
    decimal Stock,
    decimal MinimumStock,
    bool IsActive);

public sealed record QuickEntryRequest(Guid BranchId, Guid ProductVariantId, decimal Quantity, decimal UnitCost = 0, string? Note = null);
public sealed record UpdateProductRequest(string Name, Guid? CategoryId, string? ImageUrl, string VariantName, string Sku, string? Barcode, decimal Cost, decimal Price, decimal MinimumStock, bool IsActive);
public sealed record SaveCategoryRequest(string Name, bool IsActive = true);
public sealed record CreateVariantRequest(string VariantName, string Sku, string? Barcode, decimal Cost, decimal Price, decimal MinimumStock = 0, decimal InitialStock = 0, Guid? BranchId = null);
public sealed record PhysicalCountLineRequest(Guid ProductVariantId, decimal CountedQuantity);
public sealed record PhysicalCountRequest(Guid BranchId, string? Note, IReadOnlyList<PhysicalCountLineRequest> Items);
public sealed record ImportProductRow(string Name, string? Category, string? ImageUrl, string Variant, string Sku, string? Barcode, decimal Cost, decimal Price, decimal MinimumStock, decimal InitialStock);
public sealed record ImportProductsRequest(Guid BranchId, IReadOnlyList<ImportProductRow> Rows);
public sealed record InventoryAdjustmentRequest(Guid BranchId, Guid ProductVariantId, decimal QuantityChange, string Reason);
