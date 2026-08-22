using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Layaways;

public static class LayawayEndpoints
{
    public static RouteGroupBuilder MapLayawayEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/layaways", async (string? status, string? query, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var source = db.Layaways.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LayawayStatus>(status, true, out var parsedStatus)) source = source.Where(x => x.Status == parsedStatus);
            if (!string.IsNullOrWhiteSpace(query)) { var term = query.Trim(); source = source.Where(x => x.Folio.Contains(term) || x.Customer.Name.Contains(term) || (x.Customer.Phone != null && x.Customer.Phone.Contains(term))); }
            var rows = await source.OrderByDescending(x => x.OpenedAtUtc).Take(250).Select(x => new
            {
                x.Id, x.Folio, x.OpenedAtUtc, x.DueAtUtc, Status = x.Status.ToString(), x.Total, Paid = x.Payments.Sum(p => (decimal?)p.Amount) ?? 0,
                Balance = x.Total - (x.Payments.Sum(p => (decimal?)p.Amount) ?? 0), Customer = x.Customer.Name, x.Customer.Phone, x.Customer.Email,
                Items = x.Items.Select(i => new { i.Id, i.ProductVariantId, i.ProductName, i.VariantName, i.Sku, i.Quantity, i.UnitPrice, i.LineTotal }),
                Payments = x.Payments.OrderByDescending(p => p.PaidAtUtc).Select(p => new { p.Id, p.Amount, Method = p.Method.ToString(), p.PaidAtUtc, p.Note })
            }).ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });
        
        api.MapGet("/layaways/reminders", async (HttpContext context, ITenantContext tenant, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var reminderDays = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.LayawayReminderDaysBefore).SingleAsync(cancellationToken);
            var zone = ClientTimeZone.From(context);
            var today = ClientTimeZone.Today(zone);
            var limit = ClientTimeZone.StartOfDayUtc(today.AddDays(reminderDays + 1), zone);
            return Results.Ok(await db.Layaways.AsNoTracking().Where(x => x.Status == LayawayStatus.Active && x.DueAtUtc < limit)
                .OrderBy(x => x.DueAtUtc).Select(x => new { x.Id, x.Folio, x.DueAtUtc, Customer = x.Customer.Name, x.Customer.Phone, x.Customer.Email, Balance = x.Total - (x.Payments.Sum(p => (decimal?)p.Amount) ?? 0), IsOverdue = x.DueAtUtc < DateTimeOffset.UtcNow }).ToListAsync(cancellationToken));
        });
        
        api.MapPost("/layaways", async (CreateLayawayRequest request, HttpContext context, ITenantContext tenant, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0)) return Results.BadRequest(new { title = "Agrega al menos un producto con cantidad válida." });
            if (request.TermDays is < 1 or > 90) return Results.BadRequest(new { title = "El plazo debe estar entre 1 y 90 días." });
            if (!await db.Customers.AnyAsync(x => x.Id == request.CustomerId && x.IsActive, cancellationToken)) return Results.BadRequest(new { title = "Selecciona un cliente activo." });
            if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive, cancellationToken)) return Results.BadRequest(new { title = "Selecciona una sucursal activa." });
            var ids = request.Items.Select(x => x.ProductVariantId).Distinct().ToList();
            if (ids.Count != request.Items.Count) return Results.BadRequest(new { title = "No repitas productos dentro del apartado." });
            var variants = await db.ProductVariants.Include(x => x.Product).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            var balances = await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);
            var allowNegativeStock = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.AllowNegativeStock).SingleAsync(cancellationToken);
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.OpenedByUserId == userId && x.BranchId == request.BranchId && x.Status == CashSessionStatus.Open, cancellationToken);
            if (cash is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashSession"] = ["Abre tu caja antes de crear un apartado."] });
            var layaway = new Layaway { Folio = $"A-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", BranchId = request.BranchId, CustomerId = request.CustomerId, CreatedByUserId = userId, DueAtUtc = DateTimeOffset.UtcNow.AddDays(request.TermDays), Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim() };
            foreach (var line in request.Items)
            {
                if (!variants.TryGetValue(line.ProductVariantId, out var variant)) return Results.BadRequest(new { title = "Uno de los productos no está disponible." });
                if (!balances.TryGetValue(line.ProductVariantId, out var balance))
                {
                    if (!allowNegativeStock) return Results.BadRequest(new { title = "Uno de los productos no tiene existencia suficiente." });
                    balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, AverageCost = variant.Cost };
                    balances[variant.Id] = balance;
                    db.InventoryBalances.Add(balance);
                }
                if (!allowNegativeStock && balance.Quantity < line.Quantity) return Results.BadRequest(new { title = "Uno de los productos no tiene existencia suficiente." });
                var total = decimal.Round(variant.Price * line.Quantity, 2); layaway.Total += total; balance.Quantity -= line.Quantity;
                layaway.Items.Add(new LayawayItem { ProductVariantId = variant.Id, ProductName = variant.Product.Name, VariantName = variant.Name, Sku = variant.Sku, Quantity = line.Quantity, UnitPrice = variant.Price, LineTotal = total });
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.Layaway, Quantity = -line.Quantity, UnitCost = balance.AverageCost, Note = layaway.Folio, PerformedByUserId = userId });
            }
            var depositPayments = request.Payments is { Count: > 0 } ? request.Payments : request.Deposit > 0 ? [new PaymentPartRequest(request.PaymentMethod, request.Deposit)] : [];
            var deposit = depositPayments.Sum(x => x.Amount);
            if (depositPayments.Any(x => !Enum.IsDefined(x.Method) || x.Amount <= 0) || deposit < 0 || deposit > layaway.Total) return Results.BadRequest(new { title = "Los pagos del anticipo no son válidos o superan el total." });
            foreach (var payment in depositPayments) layaway.Payments.Add(new LayawayPayment { Amount = payment.Amount, Method = payment.Method, ReceivedByUserId = userId, CashSessionId = cash.Id, Note = "Anticipo" });
            if (deposit == layaway.Total) { layaway.Status = LayawayStatus.Completed; layaway.CompletedAtUtc = DateTimeOffset.UtcNow; }
            db.Layaways.Add(layaway); await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { layaway.Id, layaway.Folio, layaway.Total, Paid = deposit, Balance = layaway.Total - deposit, layaway.DueAtUtc });
        });
        
        api.MapPost("/layaways/{layawayId:guid}/payments", async (Guid layawayId, AddLayawayPaymentRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var paymentParts = request.Payments is { Count: > 0 } ? request.Payments : [new PaymentPartRequest(request.PaymentMethod, request.Amount)];
            var paymentTotal = decimal.Round(paymentParts.Sum(x => x.Amount), 2);
            if (paymentTotal <= 0 || paymentParts.Any(x => !Enum.IsDefined(x.Method) || x.Amount <= 0)) return Results.BadRequest(new { title = "El abono debe ser mayor a cero y sus formas de pago deben ser válidas." });
            var layaway = await db.Layaways.Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == layawayId, cancellationToken);
            if (layaway is null) return Results.NotFound(); if (layaway.Status != LayawayStatus.Active) return Results.Conflict(new { title = "Este apartado ya no acepta abonos." });
            var paidBefore = layaway.Payments.Sum(x => x.Amount);
            var balance = decimal.Round(layaway.Total - paidBefore, 2); if (paymentTotal > balance) return Results.BadRequest(new { title = $"El abono supera el saldo pendiente de {balance:C2}." });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.OpenedByUserId == userId && x.BranchId == layaway.BranchId && x.Status == CashSessionStatus.Open, cancellationToken);
            if (cash is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashSession"] = ["Abre tu caja antes de registrar un abono."] });
            foreach (var payment in paymentParts) db.LayawayPayments.Add(new LayawayPayment { LayawayId = layaway.Id, Amount = decimal.Round(payment.Amount, 2), Method = payment.Method, ReceivedByUserId = userId, CashSessionId = cash.Id, Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() });
            if (paymentTotal == balance) { layaway.Status = LayawayStatus.Completed; layaway.CompletedAtUtc = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(cancellationToken); return Results.Ok(new { Paid = paidBefore + paymentTotal, Balance = balance - paymentTotal, Status = layaway.Status.ToString() });
        });
        
        api.MapPost("/layaways/{layawayId:guid}/cancel", async (Guid layawayId, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var layaway = await db.Layaways.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == layawayId, cancellationToken); if (layaway is null) return Results.NotFound();
            if (layaway.Status != LayawayStatus.Active) return Results.Conflict(new { title = "Solo se pueden cancelar apartados activos." });
            var ids = layaway.Items.Select(x => x.ProductVariantId).ToList(); var balances = await db.InventoryBalances.Where(x => x.BranchId == layaway.BranchId && ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            foreach (var item in layaway.Items) { if (!balances.TryGetValue(item.ProductVariantId, out var balance)) { balance = new InventoryBalance { BranchId = layaway.BranchId, ProductVariantId = item.ProductVariantId }; db.InventoryBalances.Add(balance); } balance.Quantity += item.Quantity; db.InventoryMovements.Add(new InventoryMovement { BranchId = layaway.BranchId, ProductVariantId = item.ProductVariantId, Type = InventoryMovementType.Return, Quantity = item.Quantity, Note = $"Cancelación {layaway.Folio}", PerformedByUserId = userId }); }
            layaway.Status = LayawayStatus.Cancelled; layaway.CancelledAtUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        
        return api;
    }
}
