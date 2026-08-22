using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Inventory;

public static class InventoryCatalogEndpoints
{
    public static RouteGroupBuilder MapInventoryCatalogEndpoints(this RouteGroupBuilder api)
    {
        var inventoryAdmin = api.MapGroup("/inventory").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
        inventoryAdmin.MapGet("/movements", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.InventoryMovements.AsNoTracking().Where(x => x.Type == InventoryMovementType.Entry || x.Type == InventoryMovementType.InitialStock)
                .OrderByDescending(x => x.CreatedAtUtc).Take(25)
                .Select(x => new { x.Id, x.ProductVariantId, Product = x.ProductVariant.Product.Name, Variant = x.ProductVariant.Name, x.Quantity, x.UnitCost, x.Note, x.CreatedAtUtc })
                .ToListAsync(cancellationToken)));
        
        inventoryAdmin.MapGet("/kardex", async (Guid? productVariantId, DateTimeOffset? from, DateTimeOffset? to, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var query = db.InventoryMovements.AsNoTracking().AsQueryable();
            if (productVariantId.HasValue) query = query.Where(x => x.ProductVariantId == productVariantId.Value);
            if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from.Value);
            if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < to.Value);
            return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Take(1000).Select(x => new { x.Id, x.CreatedAtUtc, Type = x.Type.ToString(), x.Quantity, x.UnitCost, x.Note, Product = x.ProductVariant.Product.Name, Variant = x.ProductVariant.Name, x.ProductVariant.Sku, User = x.PerformedByUser != null ? x.PerformedByUser.DisplayName : null, Branch = x.Branch.Name }).ToListAsync(cancellationToken));
        });
        
        inventoryAdmin.MapPost("/physical-count", async (PhysicalCountRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Items.Count == 0) return Results.BadRequest(new { title = "Agrega al menos un producto al conteo." });
            if (request.Items.Any(x => x.CountedQuantity < 0)) return Results.BadRequest(new { title = "Las cantidades contadas no pueden ser negativas." });
            if (request.Items.Select(x => x.ProductVariantId).Distinct().Count() != request.Items.Count) return Results.BadRequest(new { title = "El conteo contiene productos repetidos." });
            if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive, cancellationToken)) return Results.BadRequest(new { title = "La sucursal seleccionada no está disponible." });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var ids = request.Items.Select(x => x.ProductVariantId).Distinct().ToList();
            var variants = await db.ProductVariants.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (variants.Count != ids.Count) return Results.BadRequest(new { title = "Uno o más productos del conteo ya no existen." });
            var balances = await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);
            foreach (var item in request.Items)
            {
                if (!variants.TryGetValue(item.ProductVariantId, out var variant)) return Results.BadRequest(new { title = "Uno de los productos del conteo ya no existe." });
                if (!balances.TryGetValue(item.ProductVariantId, out var balance)) { balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = item.ProductVariantId, AverageCost = variant.Cost }; db.InventoryBalances.Add(balance); }
                var difference = item.CountedQuantity - balance.Quantity;
                if (difference == 0) continue;
                balance.Quantity = item.CountedQuantity;
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = item.ProductVariantId, Type = InventoryMovementType.Adjustment, Quantity = difference, UnitCost = balance.AverageCost, Note = $"Conteo físico: {request.Note}".Trim(), PerformedByUserId = userId });
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { Updated = request.Items.Count });
        });
        
        
        api.MapGet("/products", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var products = await db.ProductVariants
                .AsNoTracking()
                .Where(x => x.IsActive && x.Product.IsActive)
                .OrderBy(x => x.Product.Name)
                .Select(x => new ProductResponse(
                    x.ProductId,
                    x.Product.Name,
                    x.Product.Category != null ? x.Product.Category.Name : null,
                    x.Product.CategoryId,
                    x.Product.ImageUrl,
                    x.Id,
                    x.Name,
                    x.Sku,
                    x.Barcode,
                    x.Cost,
                    x.Price,
                    db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity),
                    x.MinimumStock,
                    x.IsActive))
                .ToListAsync(cancellationToken);
        
            return Results.Ok(products);
        });
        
        api.MapGet("/categories", async (VendemeFacilDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync(cancellationToken)));
        api.MapPost("/categories", async (SaveCategoryRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
            var name=request.Name.Trim(); if(await db.Categories.AnyAsync(x=>x.Name==name,cancellationToken)) return Results.Conflict(new {title="La categoría ya existe."});
            var category=new Category{Name=name}; db.Categories.Add(category); await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/v1/categories/{category.Id}",new{category.Id,category.Name});
        });
        
        api.MapGet("/products/next-sku", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var skus = await db.ProductVariants.AsNoTracking().Select(x => x.Sku).ToListAsync(cancellationToken);
            var lastNumericSku = skus.Select(x => long.TryParse(x, out var value) ? value : 0).DefaultIfEmpty(0).Max();
            return Results.Ok(new { Sku = (lastNumericSku + 1).ToString() });
        });
        
        api.MapPost("/products/{productId:guid}/variants", async (Guid productId, CreateVariantRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken); if (product is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Sku) || request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0 || request.InitialStock < 0) return Results.BadRequest();
            var sku=request.Sku.Trim(); var barcode=string.IsNullOrWhiteSpace(request.Barcode)?null:request.Barcode.Trim();
            if(await db.ProductVariants.AnyAsync(x=>x.Sku==sku,cancellationToken)) return Results.Conflict(new{title="Ya existe otro producto con ese SKU."});
            if(barcode is not null && await db.ProductVariants.AnyAsync(x=>x.Barcode==barcode,cancellationToken)) return Results.Conflict(new{title="Ya existe otro producto con ese código de barras."});
            var variant=new ProductVariant{ProductId=product.Id,Name=string.IsNullOrWhiteSpace(request.VariantName)?"Única":request.VariantName.Trim(),Sku=sku,Barcode=barcode,Cost=request.Cost,Price=request.Price,MinimumStock=request.MinimumStock}; db.ProductVariants.Add(variant);
            if(request.InitialStock>0){if(request.BranchId is null)return Results.BadRequest();db.InventoryBalances.Add(new InventoryBalance{BranchId=request.BranchId.Value,ProductVariantId=variant.Id,Quantity=request.InitialStock,AverageCost=request.Cost});db.InventoryMovements.Add(new InventoryMovement{BranchId=request.BranchId.Value,ProductVariantId=variant.Id,Type=InventoryMovementType.InitialStock,Quantity=request.InitialStock,UnitCost=request.Cost,Note="Existencia inicial"});}
            await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/v1/products/{productId}/variants/{variant.Id}",new{variant.Id});
        });
        
        api.MapPost("/products/import", async (ImportProductsRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Rows.Count == 0) return Results.BadRequest(new { title = "El archivo no contiene productos." });
            if (request.Rows.Count > 2000) return Results.BadRequest(new { title = "El archivo supera el límite de 2,000 productos por importación." });
            if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId, cancellationToken))
                return Results.BadRequest(new { title = "La sucursal seleccionada no existe o ya no está disponible." });
            for (var index = 0; index < request.Rows.Count; index++)
            {
                var row = request.Rows[index];
                var csvRow = index + 2;
                if (string.IsNullOrWhiteSpace(row.Name)) return Results.BadRequest(new { title = $"Fila {csvRow}: el nombre del producto es obligatorio." });
                if (string.IsNullOrWhiteSpace(row.Sku)) return Results.BadRequest(new { title = $"Fila {csvRow}: el SKU es obligatorio." });
                if (row.Name.Trim().Length > 160) return Results.BadRequest(new { title = $"Fila {csvRow}: el nombre no puede superar 160 caracteres." });
                if (row.ImageUrl?.Trim().Length > 500) return Results.BadRequest(new { title = $"Fila {csvRow}: la URL de imagen no puede superar 500 caracteres." });
                if (row.Cost < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el costo no puede ser negativo." });
                if (row.Price < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el precio no puede ser negativo." });
                if (row.MinimumStock < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el stock mínimo no puede ser negativo." });
                if (row.InitialStock < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: la existencia no puede ser negativa." });
            }
        
            var duplicateSku = request.Rows.GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicateSku is not null) return Results.Conflict(new { title = $"El SKU {duplicateSku.Key} está repetido en el archivo." });
            var duplicateBarcode = request.Rows.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)).GroupBy(x => x.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicateBarcode is not null) return Results.Conflict(new { title = $"El código de barras {duplicateBarcode.Key} está repetido en el archivo." });
        
            var requestedSkus = request.Rows.Select(x => x.Sku.Trim()).ToList();
            var existingVariants = await db.ProductVariants.Include(x => x.Product).Where(x => requestedSkus.Contains(x.Sku)).ToListAsync(cancellationToken);
            var variantsBySku = existingVariants.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
            var requestedBarcodes = request.Rows.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)).Select(x => x.Barcode!.Trim()).ToList();
            var barcodeOwners = await db.ProductVariants.Where(x => x.Barcode != null && requestedBarcodes.Contains(x.Barcode)).Select(x => new { x.Sku, x.Barcode }).ToListAsync(cancellationToken);
            var barcodeConflict = barcodeOwners.FirstOrDefault(owner => request.Rows.Any(row => string.Equals(row.Barcode?.Trim(), owner.Barcode, StringComparison.OrdinalIgnoreCase) && !string.Equals(row.Sku.Trim(), owner.Sku, StringComparison.OrdinalIgnoreCase)));
            if (barcodeConflict is not null) return Results.Conflict(new { title = $"El código de barras {barcodeConflict.Barcode} ya pertenece a otro producto." });
        
            var categories = (await db.Categories.ToListAsync(cancellationToken)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var existingIds = existingVariants.Select(x => x.Id).ToList();
            var balancesByVariant = (await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && existingIds.Contains(x.ProductVariantId)).ToListAsync(cancellationToken)).ToDictionary(x => x.ProductVariantId);
            var imported = 0;
            var updated = 0;
            foreach (var row in request.Rows)
            {
                Category? category = null;
                if (!string.IsNullOrWhiteSpace(row.Category))
                {
                    var categoryName = row.Category.Trim();
                    if (!categories.TryGetValue(categoryName, out category))
                    {
                        category = new Category { Name = categoryName };
                        db.Categories.Add(category);
                        categories[categoryName] = category;
                    }
                }
        
                if (variantsBySku.TryGetValue(row.Sku.Trim(), out var variant))
                {
                    variant.Product.Name = row.Name.Trim();
                    variant.Product.Category = category;
                    variant.Product.CategoryId = category?.Id;
                    variant.Product.ImageUrl = string.IsNullOrWhiteSpace(row.ImageUrl) ? null : row.ImageUrl.Trim();
                    variant.Name = string.IsNullOrWhiteSpace(row.Variant) ? "Única" : row.Variant.Trim();
                    variant.Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim();
                    variant.Cost = row.Cost;
                    variant.Price = row.Price;
                    variant.MinimumStock = row.MinimumStock;
        
                    balancesByVariant.TryGetValue(variant.Id, out var balance);
                    var previousStock = balance?.Quantity ?? 0;
                    var difference = row.InitialStock - previousStock;
                    if (balance is null)
                    {
                        balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, Quantity = row.InitialStock, AverageCost = row.Cost };
                        db.InventoryBalances.Add(balance);
                    }
                    else
                    {
                        balance.Quantity = row.InitialStock;
                        balance.AverageCost = row.Cost;
                    }
                    if (difference != 0)
                        db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.Adjustment, Quantity = difference, UnitCost = row.Cost, Note = "Actualización mediante importación CSV" });
                    updated++;
                }
                else
                {
                    var product = new Product { Name = row.Name.Trim(), Category = category, ImageUrl = string.IsNullOrWhiteSpace(row.ImageUrl) ? null : row.ImageUrl.Trim() };
                    variant = new ProductVariant { Name = string.IsNullOrWhiteSpace(row.Variant) ? "Única" : row.Variant.Trim(), Sku = row.Sku.Trim(), Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(), Cost = row.Cost, Price = row.Price, MinimumStock = row.MinimumStock };
                    product.Variants.Add(variant);
                    db.Products.Add(product);
                    if (row.InitialStock != 0)
                    {
                        db.InventoryBalances.Add(new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, Quantity = row.InitialStock, AverageCost = row.Cost });
                        db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.InitialStock, Quantity = row.InitialStock, UnitCost = row.Cost, Note = "Importación de catálogo" });
                    }
                    imported++;
                }
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                var sqlException = exception.InnerException as Microsoft.Data.SqlClient.SqlException;
                var detail = sqlException?.Number is 2601 or 2627
                    ? "Hay un SKU, código de barras o categoría que ya está asignado a otro registro. Revisa los valores duplicados."
                    : "SQL Server rechazó uno de los datos. Revisa longitudes, números y valores duplicados del archivo.";
                return Results.Problem(title: "No se pudo importar el catálogo", detail: detail, statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Ok(new { Imported = imported, Updated = updated });
        });
        
        api.MapPost("/products", async (
            CreateProductRequest request,
            ITenantContext tenant,
            VendemeFacilDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["El nombre y el SKU son obligatorios."] });
            if (request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0 || request.InitialStock < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amounts"] = ["Costo, precio y existencia no pueden ser negativos."] });
            if (request.InitialStock > 0 && request.BranchId is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["branchId"] = ["Selecciona una sucursal para la existencia inicial."] });
        
            var product = new Product
            {
                Name = request.Name.Trim(),
                CategoryId = request.CategoryId,
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
                Variants =
                [
                    new ProductVariant
                    {
                        Name = string.IsNullOrWhiteSpace(request.VariantName) ? "Única" : request.VariantName.Trim(),
                        Sku = request.Sku.Trim(),
                        Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
                        Cost = request.Cost,
                        Price = request.Price,
                        MinimumStock = request.MinimumStock
                    }
                ]
            };
        
            db.Products.Add(product);
        
            if (request.InitialStock > 0)
            {
                var variant = product.Variants.Single();
                db.InventoryBalances.Add(new InventoryBalance
                {
                    BranchId = request.BranchId!.Value,
                    ProductVariantId = variant.Id,
                    Quantity = request.InitialStock,
                    AverageCost = request.Cost
                });
                db.InventoryMovements.Add(new InventoryMovement
                {
                    BranchId = request.BranchId.Value,
                    ProductVariantId = variant.Id,
                    Type = InventoryMovementType.InitialStock,
                    Quantity = request.InitialStock,
                    UnitCost = request.Cost,
                    Note = "Existencia inicial"
                });
            }
        
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/products/{product.Id}", new { product.Id, TenantId = tenant.TenantId });
        });
        
        api.MapPut("/products/{variantId:guid}", async (Guid variantId, UpdateProductRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku) || request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["Revisa nombre, SKU, costo, precio y stock mínimo."] });
            var variant = await db.ProductVariants.Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == variantId, cancellationToken);
            if (variant is null) return Results.NotFound();
            var normalizedSku = request.Sku.Trim();
            var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
            if (await db.ProductVariants.AnyAsync(x => x.Id != variantId && x.Sku == normalizedSku, cancellationToken))
                return Results.Conflict(new { title = "Ya existe otro producto con ese SKU." });
            if (barcode is not null && await db.ProductVariants.AnyAsync(x => x.Id != variantId && x.Barcode == barcode, cancellationToken))
                return Results.Conflict(new { title = "Ya existe otro producto con ese código de barras." });
            variant.Product.Name = request.Name.Trim(); variant.Product.CategoryId=request.CategoryId; variant.Product.ImageUrl=string.IsNullOrWhiteSpace(request.ImageUrl)?null:request.ImageUrl.Trim(); variant.Name = string.IsNullOrWhiteSpace(request.VariantName) ? "Única" : request.VariantName.Trim();
            variant.Sku = normalizedSku; variant.Barcode = barcode; variant.Cost = request.Cost; variant.Price = request.Price; variant.MinimumStock = request.MinimumStock; variant.IsActive = request.IsActive; variant.Product.IsActive = request.IsActive;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        
        inventoryAdmin.MapPost("/adjustment", async (InventoryAdjustmentRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.QuantityChange == 0 || string.IsNullOrWhiteSpace(request.Reason))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = ["Indica una cantidad distinta de cero y el motivo del ajuste."] });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            IResult result = Results.NotFound();
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == request.ProductVariantId, cancellationToken);
                if (variant is null) return;
                var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.BranchId == request.BranchId && x.ProductVariantId == request.ProductVariantId, cancellationToken);
                if (balance is null) { balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = request.ProductVariantId, AverageCost = variant.Cost }; db.InventoryBalances.Add(balance); }
                if (balance.Quantity + request.QuantityChange < 0)
                {
                    result = Results.ValidationProblem(new Dictionary<string, string[]> { ["quantityChange"] = ["El ajuste dejaría el inventario en negativo."] });
                    return;
                }
                balance.Quantity += request.QuantityChange;
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = request.ProductVariantId, Type = InventoryMovementType.Adjustment, Quantity = request.QuantityChange, UnitCost = balance.AverageCost, Note = request.Reason.Trim(), PerformedByUserId = userId });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                result = Results.Ok(new { balance.Quantity });
            });
            return result;
        });
        
        inventoryAdmin.MapPost("/quick-entry", async (
            QuickEntryRequest request,
            VendemeFacilDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (request.Quantity <= 0 || request.UnitCost < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["entry"] = ["La cantidad debe ser mayor a cero y el costo no puede ser negativo."] });
        
            var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == request.ProductVariantId, cancellationToken);
            if (variant is null) return Results.NotFound();
        
            var balance = await db.InventoryBalances.SingleOrDefaultAsync(
                x => x.BranchId == request.BranchId && x.ProductVariantId == request.ProductVariantId,
                cancellationToken);
        
            if (balance is null)
            {
                balance = new InventoryBalance
                {
                    BranchId = request.BranchId,
                    ProductVariantId = request.ProductVariantId,
                    Quantity = request.Quantity,
                    AverageCost = request.UnitCost
                };
                db.InventoryBalances.Add(balance);
            }
            else
            {
                var priorValue = balance.Quantity * balance.AverageCost;
                var incomingCost = request.UnitCost > 0 ? request.UnitCost : balance.AverageCost;
                balance.Quantity += request.Quantity;
                balance.AverageCost = balance.Quantity == 0 ? 0 : (priorValue + request.Quantity * incomingCost) / balance.Quantity;
            }
        
            if (request.UnitCost > 0) variant.Cost = balance.AverageCost;
            db.InventoryMovements.Add(new InventoryMovement
            {
                BranchId = request.BranchId,
                ProductVariantId = request.ProductVariantId,
                Type = InventoryMovementType.Entry,
                Quantity = request.Quantity,
                UnitCost = request.UnitCost,
                Note = request.Note
            });
        
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { balance.Quantity, balance.AverageCost, CostPending = balance.AverageCost == 0 });
        });
        
        return api;
    }
}
