using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Account;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/subscription", async (ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
            var payments = await db.SubscriptionPayments.AsNoTracking().Where(x => x.TenantId == tenant.Id).OrderByDescending(x => x.PaidAtUtc).Take(24).ToListAsync(cancellationToken);
            var effectiveEnd = tenant.SubscriptionStatus == "Trial" ? tenant.TrialEndsAtUtc : tenant.CurrentPeriodEndsAtUtc;
            return Results.Ok(new { tenant.PlanCode, tenant.SubscriptionStatus, tenant.TrialEndsAtUtc, tenant.CurrentPeriodEndsAtUtc, GraceEndsAtUtc = effectiveEnd?.AddDays(7), Capabilities = PlanCatalog.Get(tenant.PlanCode), Payments = payments });
        });
        
        api.MapPost("/audit/sync", async (
            AuditSyncRequest request,
            VendemeFacilDbContext db,
            ITenantContext tenant,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request?.Logs == null || request.Logs.Count == 0)
                return Results.NoContent();
        
            var tenantId = tenant.TenantId;
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
            var clientIds = request.Logs.Select(x => x.Id).ToList();
            var existingIds = await db.AuditLogs
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && clientIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        
            var logsToInsert = request.Logs
                .Where(x => !existingIds.Contains(x.Id))
                .Select(x => new AuditLog
                {
                    Id = x.Id,
                    TenantId = tenantId,
                    Action = x.Action,
                    Description = x.Description,
                    DetailsJson = x.DetailsJson,
                    PerformedByUserId = x.PerformedByUserId,
                    BranchId = x.BranchId,
                    ClientCreatedAtUtc = x.ClientCreatedAtUtc,
                    IpAddress = ipAddress
                })
                .ToList();
        
            if (logsToInsert.Count > 0)
            {
                await db.AuditLogs.AddRangeAsync(logsToInsert, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        
            return Results.NoContent();
        });
        
        api.MapGet("/audit", async (
            VendemeFacilDbContext db,
            CancellationToken cancellationToken) =>
        {
            var logs = await db.AuditLogs
                .AsNoTracking()
                .Include(x => x.PerformedByUser)
                .OrderByDescending(x => x.ClientCreatedAtUtc)
                .Take(150)
                .Select(x => new
                {
                    x.Id,
                    x.Action,
                    x.Description,
                    x.DetailsJson,
                    PerformedByUser = x.PerformedByUser.DisplayName,
                    x.ClientCreatedAtUtc,
                    x.IpAddress
                })
                .ToListAsync(cancellationToken);
        
            return Results.Ok(logs);
        });
        api.MapPost("/subscription/change-request", async (RequestPlanChangeRequest request, HttpContext context, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var plan = request.PlanCode.Trim().ToLowerInvariant();
            if (!new[] { "esencial", "negocio", "pro" }.Contains(plan)) return Results.BadRequest(new { title = "Plan inválido." });
            var current = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantContext.TenantId).Select(x => x.PlanCode).SingleAsync(cancellationToken);
            if (current == plan) return Results.Conflict(new { title = "Ese ya es tu plan actual." });
            db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenantContext.TenantId, Type = "PlanChangeRequested", Description = $"Solicitud de cambio de {current} a {plan}.", PerformedBy = context.User.FindFirstValue(ClaimTypes.Email) });
            await db.SaveChangesAsync(cancellationToken); return Results.Accepted(value: new { message = "Recibimos tu solicitud. Nuestro equipo te contactará para confirmar el cambio." });
        });
        
        api.MapGet("/onboarding", async (ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var id = tenantContext.TenantId;
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
            var productCount = await db.ProductVariants.CountAsync(x => x.IsActive, cancellationToken);
            var inventoryReady = await db.InventoryBalances.AnyAsync(x => x.Quantity > 0, cancellationToken);
            var cashOpened = await db.CashSessions.AnyAsync(cancellationToken);
            var firstSale = await db.Sales.AnyAsync(x => x.Status == SaleStatus.Completed, cancellationToken);
            var invitedUser = await db.Users.CountAsync(x => x.IsActive, cancellationToken) > 1;
            var demoCount = await db.Products.CountAsync(x => x.IsDemoData, cancellationToken);
            var steps = new[]
            {
                new { Code = "profile", Label = "Completa los datos de tu negocio", Completed = !string.IsNullOrWhiteSpace(tenant.BusinessType) && !string.IsNullOrWhiteSpace(tenant.Phone) },
                new { Code = "product", Label = "Crea o importa tu primer producto", Completed = productCount > 0 },
                new { Code = "inventory", Label = "Registra inventario inicial", Completed = inventoryReady },
                new { Code = "cash", Label = "Abre tu primera caja", Completed = cashOpened },
                new { Code = "sale", Label = "Realiza tu primera venta", Completed = firstSale },
                new { Code = "printing", Label = "Configura la impresión", Completed = tenant.PrintingConfigured },
                new { Code = "user", Label = "Invita a un usuario", Completed = invitedUser }
            };
            return Results.Ok(new { tenant.BusinessType, tenant.Phone, tenant.Address, tenant.LogoUrl, tenant.OnboardingDismissed, DemoProductCount = demoCount, CompletedSteps = steps.Count(x => x.Completed), TotalSteps = steps.Length, Steps = steps });
        });
        api.MapPut("/onboarding/profile", async (SaveOnboardingProfileRequest request, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var allowed = new[] { "boutique", "calzado", "papeleria", "regalos", "otro" };
            var type = request.BusinessType.Trim().ToLowerInvariant();
            if (!allowed.Contains(type)) return Results.BadRequest(new { title = "Selecciona un giro válido." });
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
            tenant.BusinessType = type; tenant.Phone = request.Phone?.Trim(); tenant.Address = request.Address?.Trim(); tenant.LogoUrl = request.LogoUrl?.Trim();
            await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        api.MapPut("/onboarding/preferences", async (UpdateOnboardingPreferenceRequest request, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
            if (request.PrintingConfigured.HasValue) tenant.PrintingConfigured = request.PrintingConfigured.Value;
            if (request.Dismissed.HasValue) tenant.OnboardingDismissed = request.Dismissed.Value;
            await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        api.MapPost("/onboarding/demo-catalog", async (CreateDemoCatalogRequest request, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (await db.Products.AnyAsync(x => x.IsDemoData, cancellationToken)) return Results.Conflict(new { title = "Ya existe un catálogo de demostración." });
            var type = request.BusinessType.Trim().ToLowerInvariant();
            var templates = type switch
            {
                "boutique" => new[] { ("Blusa básica", "Mediana / Beige", "DEMO-BLU-M", 180m, 349m, 8m), ("Vestido casual", "Grande / Negro", "DEMO-VES-G", 320m, 649m, 5m), ("Pantalón mezclilla", "Talla 28", "DEMO-PAN-28", 280m, 559m, 6m) },
                "calzado" => new[] { ("Tenis urbano", "Número 25 / Blanco", "DEMO-TEN-25", 390m, 799m, 5m), ("Zapatilla clásica", "Número 24 / Negro", "DEMO-ZAP-24", 340m, 699m, 4m), ("Sandalia", "Número 23 / Café", "DEMO-SAN-23", 210m, 449m, 6m) },
                "papeleria" => new[] { ("Cuaderno profesional", "Cuadro grande", "DEMO-CUA-01", 42m, 69m, 20m), ("Bolígrafo azul", "Punto mediano", "DEMO-BOL-AZ", 6m, 12m, 50m), ("Papel carta", "Paquete 500 hojas", "DEMO-PAP-500", 95m, 139m, 10m) },
                _ => new[] { ("Producto muestra A", "Presentación Única", "DEMO-001", 50m, 99m, 10m), ("Producto muestra B", "Presentación Única", "DEMO-002", 80m, 159m, 8m), ("Producto muestra C", "Presentación Única", "DEMO-003", 120m, 239m, 6m) }
            };
            var branch = await db.Branches.OrderByDescending(x => x.IsMain).FirstAsync(cancellationToken);
            var category = await db.Categories.FirstOrDefaultAsync(x => x.Name == "Productos de demostración", cancellationToken);
            if (category is null) { category = new Category { Name = "Productos de demostración" }; db.Categories.Add(category); }
            foreach (var item in templates)
            {
                var product = new Product { Name = item.Item1, CategoryId = category.Id, IsDemoData = true };
                var variant = new ProductVariant { ProductId = product.Id, Name = item.Item2, Sku = item.Item3, Cost = item.Item4, Price = item.Item5, MinimumStock = 2 };
                product.Variants.Add(variant); db.Products.Add(product);
                db.InventoryBalances.Add(new InventoryBalance { BranchId = branch.Id, ProductVariantId = variant.Id, Quantity = item.Item6, AverageCost = item.Item4 });
                db.InventoryMovements.Add(new InventoryMovement { BranchId = branch.Id, ProductVariantId = variant.Id, Type = InventoryMovementType.InitialStock, Quantity = item.Item6, UnitCost = item.Item4, Note = "Datos de demostración" });
            }
            await db.SaveChangesAsync(cancellationToken); return Results.Created("/api/v1/onboarding/demo-catalog", new { Products = templates.Length });
        });
        api.MapDelete("/onboarding/demo-catalog", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var productIds = await db.Products.Where(x => x.IsDemoData).Select(x => x.Id).ToListAsync(cancellationToken);
            var variantIds = await db.ProductVariants.Where(x => productIds.Contains(x.ProductId)).Select(x => x.Id).ToListAsync(cancellationToken);
            if (await db.SaleItems.AnyAsync(x => variantIds.Contains(x.ProductVariantId), cancellationToken) || await db.LayawayItems.AnyAsync(x => variantIds.Contains(x.ProductVariantId), cancellationToken))
                return Results.Conflict(new { title = "No se pueden eliminar ejemplos que ya forman parte de una venta o apartado. Puedes desactivarlos desde Productos." });
            await db.InventoryMovements.Where(x => variantIds.Contains(x.ProductVariantId)).ExecuteDeleteAsync(cancellationToken);
            await db.InventoryBalances.Where(x => variantIds.Contains(x.ProductVariantId)).ExecuteDeleteAsync(cancellationToken);
            await db.ProductVariants.Where(x => variantIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
            await db.Products.Where(x => productIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
            return Results.NoContent();
        });
        
        
        var userAdmin = api.MapGroup("/users").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
        userAdmin.MapGet("/", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Users.AsNoTracking().OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.DisplayName, x.Email, Role = x.Role.ToString(), x.CanViewCosts, x.IsActive }).ToListAsync(cancellationToken)));
        userAdmin.MapPost("/", async (CreateUserRequest request, ITenantContext tenantContext, IPasswordHasher<AppUser> passwordHasher, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) || !request.Email.Contains('@') || request.Password.Length < 8 || !Enum.TryParse<UserRole>(request.Role, true, out var role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = ["Revisa nombre, correo, contraseña y rol."] });
            var email = request.Email.Trim().ToLowerInvariant();
            var planCode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantContext.TenantId).Select(x => x.PlanCode).SingleAsync(cancellationToken);
            var capabilities = PlanCatalog.Get(planCode);
            if (await db.Users.CountAsync(x => x.IsActive, cancellationToken) >= capabilities.MaxUsers)
                return Results.Json(new { title = $"El plan {capabilities.Name} permite hasta {capabilities.MaxUsers} usuario(s). Cambia al plan Negocio para agregar más.", requiredPlan = "negocio" }, statusCode: StatusCodes.Status403Forbidden);
            if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken)) return Results.Conflict(new { title = "Ya existe un usuario con ese correo." });
            var user = new AppUser { DisplayName = request.DisplayName.Trim(), Email = email, Role = role, CanViewCosts = request.CanViewCosts };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password); db.Users.Add(user); await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", new { user.Id });
        });
        userAdmin.MapPut("/{userId:guid}", async (Guid userId, UpdateUserRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var role) || string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest();
            var currentId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken); if (user is null) return Results.NotFound();
            if (user.Id == currentId && !request.IsActive) return Results.Conflict(new { title = "No puedes desactivar tu propia cuenta." });
            user.DisplayName = request.DisplayName.Trim(); user.Role = role; user.CanViewCosts = request.CanViewCosts; user.IsActive = request.IsActive;
            await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        
        
        api.MapGet("/business/settings", async (ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
            return Results.Ok(new BusinessSettingsResponse(tenant.Name, tenant.Slug, tenant.PrimaryColor, tenant.AccentColor, tenant.ButtonColor, tenant.HoverColor, tenant.BackgroundColor, tenant.SurfaceColor, tenant.TextColor, tenant.CornerRadius, tenant.LayawayReminderDaysBefore, tenant.AllowNegativeStock, tenant.LogoUrl, tenant.OperationMode.ToString(), tenant.Phone, tenant.Address, tenant.TicketMessage, tenant.LoyaltyActive, tenant.LoyaltyCashbackPercent));
        });
        
        api.MapPut("/business/settings", async (UpdateBusinessSettingsRequest request, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var colors = new[] { request.PrimaryColor, request.AccentColor, request.ButtonColor, request.HoverColor, request.BackgroundColor, request.SurfaceColor, request.TextColor };
            if (string.IsNullOrWhiteSpace(request.Name) || colors.Any(color => !Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$")) || request.CornerRadius is < 0 or > 24 || request.LayawayReminderDaysBefore is < 0 or > 30)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Revisa el nombre y los colores seleccionados."] });
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
            tenant.Name = request.Name.Trim(); tenant.PrimaryColor = request.PrimaryColor; tenant.AccentColor = request.AccentColor; tenant.ButtonColor = request.ButtonColor; tenant.HoverColor = request.HoverColor; tenant.BackgroundColor = request.BackgroundColor; tenant.SurfaceColor = request.SurfaceColor; tenant.TextColor = request.TextColor; tenant.CornerRadius = request.CornerRadius; tenant.LayawayReminderDaysBefore = request.LayawayReminderDaysBefore; tenant.AllowNegativeStock = request.AllowNegativeStock;
            tenant.LoyaltyActive = request.LoyaltyActive; tenant.LoyaltyCashbackPercent = request.LoyaltyCashbackPercent;
            var logoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
            if (logoUrl is not null && (logoUrl.Length > 2048 || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var logoUri) || (logoUri.Scheme != Uri.UriSchemeHttp && logoUri.Scheme != Uri.UriSchemeHttps)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["logoUrl"] = ["La URL del logotipo debe ser una dirección HTTP o HTTPS válida de hasta 2,048 caracteres."] });
            tenant.LogoUrl = logoUrl;
            tenant.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            tenant.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            tenant.TicketMessage = string.IsNullOrWhiteSpace(request.TicketMessage) ? null : request.TicketMessage.Trim();
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new BusinessSettingsResponse(tenant.Name, tenant.Slug, tenant.PrimaryColor, tenant.AccentColor, tenant.ButtonColor, tenant.HoverColor, tenant.BackgroundColor, tenant.SurfaceColor, tenant.TextColor, tenant.CornerRadius, tenant.LayawayReminderDaysBefore, tenant.AllowNegativeStock, tenant.LogoUrl, tenant.OperationMode.ToString(), tenant.Phone, tenant.Address, tenant.TicketMessage, tenant.LoyaltyActive, tenant.LoyaltyCashbackPercent));
        });
        
        return api;
    }
}
