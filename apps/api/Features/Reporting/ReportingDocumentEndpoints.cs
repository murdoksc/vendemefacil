using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Reporting;

public static class ReportingDocumentEndpoints
{
    public static RouteGroupBuilder MapReportingDocumentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/documents/email", async (
            SendDocumentEmailRequest request,
            ITenantContext tenantContext,
            OutboundEmailQueue emailQueue,
            VendemeFacilDbContext db,
            CancellationToken cancellationToken) =>
        {
            var planCode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantContext.TenantId).Select(x => x.PlanCode).SingleAsync(cancellationToken);
            if (!PlanCatalog.Get(planCode).EmailAndWhatsApp)
                return Results.Json(new { title = "El envío por correo está disponible desde el plan Negocio.", requiredPlan = "negocio" }, statusCode: StatusCodes.Status403Forbidden);
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sale-ticket"] = "Ticket de venta",
                ["layaway-receipt"] = "Comprobante de apartado",
                ["layaway-reminder"] = "Recordatorio de apartado",
                ["cash-report"] = "Corte de caja"
            };
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            var reference = (request.Reference ?? string.Empty).Trim();
            var content = (request.Content ?? string.Empty).Trim();
            if (!labels.TryGetValue(request.DocumentType ?? string.Empty, out var label)
                || !System.Net.Mail.MailAddress.TryCreate(email, out _)
                || reference.Length is < 1 or > 100
                || content.Length is < 1 or > 20000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Revisa el correo y el contenido del documento."] });
        
            var businessName = await db.Tenants.AsNoTracking()
                .Where(x => x.Id == tenantContext.TenantId)
                .Select(x => x.Name)
                .SingleAsync(cancellationToken);
            if (!emailQueue.TryQueue(OutboundEmailFactory.Document(email, businessName, label, reference, content)))
                return Results.Problem(title: "No pudimos preparar el correo.", statusCode: StatusCodes.Status503ServiceUnavailable);
            return Results.Accepted(value: new { message = $"{label} enviado a {email}." });
        }).RequireRateLimiting("document-email");
        
        api.MapPost("/qz/sign", async (QzSignRequest request, IConfiguration configuration, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var planCode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantContext.TenantId).Select(x => x.PlanCode).SingleAsync(cancellationToken);
            if (!PlanCatalog.Get(planCode).SilentPrinting)
                return Results.Json(new { title = "La impresión silenciosa está disponible desde el plan Negocio.", requiredPlan = "negocio" }, statusCode: StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.Request) || request.Request.Length > 100_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["La solicitud de impresion no es valida."] });
        
            var encodedPrivateKey = configuration["Qz:PrivateKeyBase64"]?.Trim();
            if (string.IsNullOrWhiteSpace(encodedPrivateKey))
                return Results.Problem(title: "La firma QZ no esta configurada.", statusCode: StatusCodes.Status503ServiceUnavailable);
        
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(encodedPrivateKey), out _);
                var signature = rsa.SignData(Encoding.UTF8.GetBytes(request.Request), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
                return Results.Ok(new { signature = Convert.ToBase64String(signature) });
            }
            catch (CryptographicException)
            {
                return Results.Problem(title: "La llave de firma QZ no es valida.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (FormatException)
            {
                return Results.Problem(title: "La llave de firma QZ no tiene un formato valido.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("qz-signing");
        
        
        api.MapGet("/dashboard", async (HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var zone = ClientTimeZone.From(context);
            var today = ClientTimeZone.Today(zone);
            var todayUtc = ClientTimeZone.StartOfDayUtc(today, zone);
            var tomorrowUtc = ClientTimeZone.StartOfDayUtc(today.AddDays(1), zone);
            var completedToday = db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= todayUtc && x.SoldAtUtc < tomorrowUtc);
            var salesToday = await completedToday.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0;
            var transactionsToday = await completedToday.CountAsync(cancellationToken);
            var products = await db.ProductVariants.CountAsync(x => x.IsActive, cancellationToken);
            var units = await db.InventoryBalances.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
            var lowStock = await db.ProductVariants.CountAsync(
                x => x.IsActive && db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity) <= x.MinimumStock,
                cancellationToken);
            var weekStart = today.AddDays(-6);
            var weekStartUtc = ClientTimeZone.StartOfDayUtc(weekStart, zone);
            var weeklyRaw = await db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= weekStartUtc && x.SoldAtUtc < tomorrowUtc).Select(x => new { x.SoldAtUtc, x.Total }).ToListAsync(cancellationToken);
            var weeklySales = Enumerable.Range(0, 7).Select(i => { var date = weekStart.AddDays(i); return new { Date = date, Sales = weeklyRaw.Where(x => ClientTimeZone.LocalDate(x.SoldAtUtc, zone) == date).Sum(x => x.Total) }; }).ToList();
            var recentProducts = await db.ProductVariants.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(5).Select(x => new { x.Id, Name = x.Product.Name, Variant = x.Name, x.Sku, x.Price, Stock = db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity), x.MinimumStock }).ToListAsync(cancellationToken);
            var currentUserId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cashOpen = await db.CashSessions.AnyAsync(x => x.OpenedByUserId == currentUserId && x.Status == CashSessionStatus.Open, cancellationToken);
            return Results.Ok(new { SalesToday = salesToday, TransactionsToday = transactionsToday, AverageTicket = transactionsToday == 0 ? 0 : salesToday / transactionsToday, ProductsInStock = products, UnitsInStock = units, LowStockProducts = lowStock, WeeklySales = weeklySales, RecentProducts = recentProducts, CashOpen = cashOpen });
        });
        
        var reportsAdmin = api.MapGroup("/reports").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
        reportsAdmin.AddEndpointFilter(async (context, next) =>
        {
            var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            var db = context.HttpContext.RequestServices.GetRequiredService<VendemeFacilDbContext>();
            var planCode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenant.TenantId).Select(x => x.PlanCode).SingleAsync(context.HttpContext.RequestAborted);
            return PlanCatalog.Get(planCode).FullReports
                ? await next(context)
                : Results.Json(new { title = "Los reportes completos están disponibles desde el plan Negocio.", requiredPlan = "negocio" }, statusCode: StatusCodes.Status403Forbidden);
        });
        reportsAdmin.MapPost("/sales/backfill-costs", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var pendingLines = await db.SaleItems
                .Where(x => x.UnitCost == 0 && x.Sale.Status == SaleStatus.Completed)
                .ToListAsync(cancellationToken);
            if (pendingLines.Count == 0) return Results.Ok(new { UpdatedLines = 0, RemainingLines = 0 });
        
            var variantIds = pendingLines.Select(x => x.ProductVariantId).Distinct().ToList();
            var costs = await db.ProductVariants.AsNoTracking()
                .Where(x => variantIds.Contains(x.Id) && x.Cost > 0)
                .ToDictionaryAsync(x => x.Id, x => x.Cost, cancellationToken);
            var updated = 0;
            foreach (var line in pendingLines)
            {
                if (!costs.TryGetValue(line.ProductVariantId, out var cost)) continue;
                line.UnitCost = cost;
                updated++;
            }
            if (updated > 0) await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { UpdatedLines = updated, RemainingLines = pendingLines.Count - updated });
        });
        reportsAdmin.MapGet("/sales", async (DateOnly? from, DateOnly? to, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var zone = ClientTimeZone.From(context);
            var endDate = to ?? ClientTimeZone.Today(zone);
            var startDate = from ?? endDate.AddDays(-29);
            if (startDate > endDate || endDate.DayNumber - startDate.DayNumber > 365)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Selecciona un periodo válido de hasta 365 días."] });
            var fromUtc = ClientTimeZone.StartOfDayUtc(startDate, zone);
            var toUtc = ClientTimeZone.StartOfDayUtc(endDate.AddDays(1), zone);
            var sales = db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= fromUtc && x.SoldAtUtc < toUtc);
            var gross = await sales.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0;
            var transactions = await sales.CountAsync(cancellationToken);
            var lines = db.SaleItems.AsNoTracking().Where(x => sales.Select(s => s.Id).Contains(x.SaleId));
            var knownCost = await lines.SumAsync(x => (decimal?)(x.UnitCost * x.Quantity), cancellationToken) ?? 0;
            var pendingCost = await lines.CountAsync(x => x.UnitCost == 0, cancellationToken);
            var dailySource = await sales.Select(x => new { x.SoldAtUtc, x.Total }).ToListAsync(cancellationToken);
            var dailyRaw = dailySource.GroupBy(x => ClientTimeZone.LocalDate(x.SoldAtUtc, zone)).Select(g => new { Date = g.Key, Sales = g.Sum(x => x.Total), Transactions = g.Count() }).OrderBy(x => x.Date).ToList();
            var payments = await db.SalePayments.AsNoTracking().Where(x => sales.Select(s => s.Id).Contains(x.SaleId)).GroupBy(x => x.Method).Select(g => new PaymentBreakdown(g.Key.ToString(), g.Sum(x => x.Amount), g.Select(x => x.SaleId).Distinct().Count())).ToListAsync(cancellationToken);
            var top = await lines.GroupBy(x => new { x.ProductVariantId, x.ProductName, x.VariantName, x.Sku }).Select(g => new { g.Key.ProductVariantId, Product = g.Key.ProductName, Variant = g.Key.VariantName, g.Key.Sku, Quantity = g.Sum(x => x.Quantity), Sales = g.Sum(x => x.LineTotal), Cost = g.Sum(x => x.UnitCost * x.Quantity), CostPending = g.Any(x => x.UnitCost == 0) }).OrderByDescending(x => x.Quantity).Take(10).ToListAsync(cancellationToken);
            var result = new SalesReportSummary(fromUtc, toUtc.AddTicks(-1), gross, knownCost, pendingCost == 0 ? gross - knownCost : null, transactions, transactions == 0 ? 0 : gross / transactions, pendingCost,
                dailyRaw.Select(x => new DailySalesPoint(x.Date, x.Sales, x.Transactions)).ToList(), payments,
                top.Select(x => new TopProduct(x.ProductVariantId, x.Product, x.Variant, x.Sku, x.Quantity, x.Sales, x.CostPending ? null : x.Sales - x.Cost, x.CostPending)).ToList());
            return Results.Ok(result);
        });
        
        return api;
    }
}
