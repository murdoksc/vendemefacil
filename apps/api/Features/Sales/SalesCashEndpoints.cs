using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Sales;

public static class SalesCashEndpoints
{
    public static RouteGroupBuilder MapSalesCashEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cash/current", async (HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var session = await db.CashSessions.AsNoTracking().Where(x => x.OpenedByUserId == userId && x.Status == CashSessionStatus.Open)
                .Select(x => new { x.Id, x.BranchId, x.OpeningAmount, x.OpenedAtUtc, Status = x.Status.ToString() }).FirstOrDefaultAsync(cancellationToken);
            return session is null ? Results.NoContent() : Results.Ok(session);
        });
        
        api.MapGet("/cash/current/summary", async (HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cash = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(x => x.OpenedByUserId == userId && x.Status == CashSessionStatus.Open, cancellationToken);
            if (cash is null) return Results.NoContent();
            var salePayments = await db.Sales.Where(x => x.CashSessionId == cash.Id && x.Status != SaleStatus.Cancelled).SelectMany(x => x.Payments).GroupBy(x => x.Method).Select(g => new { Method = g.Key.ToString(), Total = g.Sum(x => x.Amount), Transactions = g.Select(x => x.SaleId).Distinct().Count() }).ToListAsync(cancellationToken);
            var layawayPayments = await db.LayawayPayments.Where(x => x.CashSessionId == cash.Id).GroupBy(x => x.Method).Select(g => new { Method = g.Key.ToString(), Total = g.Sum(x => x.Amount), Transactions = g.Select(x => x.LayawayId).Distinct().Count() }).ToListAsync(cancellationToken);
            var payments = salePayments.Concat(layawayPayments).GroupBy(x => x.Method).Select(g => new { Method = g.Key, Total = g.Sum(x => x.Total), Transactions = g.Sum(x => x.Transactions) }).ToList();
            var cashReceived = payments.Where(x => x.Method == nameof(PaymentMethod.Cash)).Sum(x => x.Total);
            return Results.Ok(new { cash.Id, cash.OpeningAmount, cash.OpenedAtUtc, ExpectedAmount = cash.OpeningAmount + cashReceived, SalesTotal = salePayments.Sum(x => x.Total), LayawayTotal = layawayPayments.Sum(x => x.Total), Transactions = await db.Sales.CountAsync(x => x.CashSessionId == cash.Id && x.Status != SaleStatus.Cancelled, cancellationToken), LayawayTransactions = await db.LayawayPayments.Where(x => x.CashSessionId == cash.Id).Select(x => x.LayawayId).Distinct().CountAsync(cancellationToken), Payments = payments, SalePayments = salePayments, LayawayPayments = layawayPayments });
        });
        api.MapGet("/cash/history", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.CashSessions.AsNoTracking().OrderByDescending(x => x.OpenedAtUtc).Take(50).Select(x => new { x.Id, x.OpenedAtUtc, x.ClosedAtUtc, Status = x.Status.ToString(), x.OpeningAmount, x.ExpectedAmount, x.CountedAmount, x.DifferenceAmount, User = db.Users.Where(u => u.Id == x.OpenedByUserId).Select(u => u.DisplayName).FirstOrDefault(), Branch = db.Branches.Where(b => b.Id == x.BranchId).Select(b => b.Name).FirstOrDefault() }).ToListAsync(cancellationToken)));
        
        api.MapGet("/cash/{cashSessionId:guid}/report", async (Guid cashSessionId, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var cash = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cashSessionId, cancellationToken);
            if (cash is null) return Results.NotFound();
            var sales = await db.Sales.AsNoTracking().Where(x => x.CashSessionId == cash.Id).OrderBy(x => x.SoldAtUtc).Select(x => new
            {
                x.Id, x.Folio, x.SoldAtUtc, Status = x.Status.ToString(), x.Total,
                Items = x.Items.Select(i => new { i.ProductName, i.VariantName, i.Sku, i.Quantity, i.LineTotal }),
                Payments = x.Payments.Select(p => new { Method = p.Method.ToString(), p.Amount })
            }).ToListAsync(cancellationToken);
            var layawayPayments = await db.LayawayPayments.AsNoTracking().Where(x => x.CashSessionId == cash.Id).OrderBy(x => x.PaidAtUtc).Select(x => new
            {
                x.Id, x.LayawayId, x.Layaway.Folio, Customer = x.Layaway.Customer.Name, x.PaidAtUtc, Method = x.Method.ToString(), x.Amount, x.Note
            }).ToListAsync(cancellationToken);
            var salePayments = sales.SelectMany(x => x.Payments).GroupBy(x => x.Method).Select(g => new { Method = g.Key, Total = g.Sum(x => x.Amount) }).ToList();
            var layawayBreakdown = layawayPayments.GroupBy(x => x.Method).Select(g => new { Method = g.Key, Total = g.Sum(x => x.Amount) }).ToList();
            var cashReceived = salePayments.Where(x => x.Method == nameof(PaymentMethod.Cash)).Sum(x => x.Total) + layawayBreakdown.Where(x => x.Method == nameof(PaymentMethod.Cash)).Sum(x => x.Total);
            return Results.Ok(new
            {
                cash.Id, cash.OpenedAtUtc, cash.ClosedAtUtc, cash.OpeningAmount,
                ExpectedAmount = cash.ExpectedAmount ?? cash.OpeningAmount + cashReceived,
                cash.CountedAmount, cash.DifferenceAmount,
                User = await db.Users.Where(x => x.Id == cash.OpenedByUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken),
                Branch = await db.Branches.Where(x => x.Id == cash.BranchId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken),
                SalesTotal = sales.Where(x => x.Status != nameof(SaleStatus.Cancelled)).Sum(x => x.Total),
                LayawayTotal = layawayPayments.Sum(x => x.Amount), SalePayments = salePayments, LayawayPayments = layawayBreakdown, Sales = sales, LayawayDetails = layawayPayments
            });
        });
        
        api.MapPost("/cash/open", async (OpenCashSessionRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.OpeningAmount < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["openingAmount"] = ["El fondo inicial no puede ser negativo."] });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            if (await db.CashSessions.AnyAsync(x => x.OpenedByUserId == userId && x.Status == CashSessionStatus.Open, cancellationToken))
                return Results.Conflict(new { title = "Ya tienes una caja abierta." });
            var session = new CashSession { BranchId = request.BranchId, OpenedByUserId = userId, OpeningAmount = request.OpeningAmount };
            db.CashSessions.Add(session); await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { session.Id, session.BranchId, session.OpeningAmount, session.OpenedAtUtc, Status = session.Status.ToString() });
        });
        
        api.MapPost("/sales", async (CreateSaleRequest request, HttpContext context, ITenantContext tenant, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Agrega al menos un producto con una cantidad válida."] });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Id == request.CashSessionId && x.BranchId == request.BranchId && x.OpenedByUserId == userId && x.Status == CashSessionStatus.Open, cancellationToken);
            if (cash is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashSession"] = ["Abre tu caja antes de cobrar."] });
        
            var variantIds = request.Items.Select(x => x.ProductVariantId).Distinct().ToList();
            var variants = await db.ProductVariants.Include(x => x.Product).Where(x => variantIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            var balances = await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && variantIds.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);
            var allowNegativeStock = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.AllowNegativeStock).SingleAsync(cancellationToken);
            if (request.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == request.CustomerId.Value && x.IsActive, cancellationToken)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["customerId"] = ["El cliente seleccionado ya no está disponible."] });
            var sale = new Sale { Folio = $"V-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", BranchId = request.BranchId, CashSessionId = cash.Id, SoldByUserId = userId, CustomerId = request.CustomerId };
            foreach (var line in request.Items)
            {
                if (!variants.TryGetValue(line.ProductVariantId, out var variant))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["stock"] = ["Uno de los productos ya no tiene existencia suficiente."] });
                if (!balances.TryGetValue(line.ProductVariantId, out var balance))
                {
                    if (!allowNegativeStock)
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["stock"] = ["Uno de los productos ya no tiene existencia suficiente."] });
                    balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, AverageCost = variant.Cost };
                    balances[variant.Id] = balance;
                    db.InventoryBalances.Add(balance);
                }
                if (!allowNegativeStock && balance.Quantity < line.Quantity)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["stock"] = ["Uno de los productos ya no tiene existencia suficiente."] });
                var total = decimal.Round(variant.Price * line.Quantity, 2);
                sale.Items.Add(new SaleItem { ProductVariantId = variant.Id, ProductName = variant.Product.Name, VariantName = variant.Name, Sku = variant.Sku, Quantity = line.Quantity, UnitPrice = variant.Price, UnitCost = balance.AverageCost, LineTotal = total });
                balance.Quantity -= line.Quantity;
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.Sale, Quantity = -line.Quantity, UnitCost = balance.AverageCost, Note = sale.Folio, PerformedByUserId = userId });
            }
            sale.Subtotal = sale.Items.Sum(x => x.LineTotal);
            if (request.Discount < 0 || request.Discount > sale.Subtotal) return Results.ValidationProblem(new Dictionary<string, string[]> { ["discount"] = ["El descuento debe estar entre cero y el subtotal."] });
            sale.Total = sale.Subtotal - decimal.Round(request.Discount, 2);
            var requestedPayments = request.Payments is { Count: > 0 } ? request.Payments : [new PaymentPartRequest(request.PaymentMethod, sale.Total, request.ReceivedAmount)];
            if (requestedPayments.Any(x => !Enum.IsDefined(x.Method) || x.Amount <= 0) || decimal.Round(requestedPayments.Sum(x => x.Amount), 2) != sale.Total)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["payments"] = ["La suma de los pagos debe ser exactamente igual al total de la venta."] });
        
            // Validate and process wallet payments
            var walletPayment = requestedPayments.FirstOrDefault(x => x.Method == PaymentMethod.Wallet);
            if (walletPayment != null)
            {
                if (!request.CustomerId.HasValue)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["payments"] = ["Para pagar con monedero electrónico debes seleccionar un cliente."] });
        
                var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId.Value, cancellationToken);
                if (customer == null || customer.WalletBalance < walletPayment.Amount)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["payments"] = ["El cliente seleccionado no tiene saldo suficiente en su monedero electrónico."] });
        
                customer.WalletBalance -= walletPayment.Amount;
            }
        
            foreach (var payment in requestedPayments)
            {
                var receivedAmount = payment.Method == PaymentMethod.Cash ? (payment.ReceivedAmount > 0 ? payment.ReceivedAmount : payment.Amount) : payment.Amount;
                if (receivedAmount < payment.Amount) return Results.ValidationProblem(new Dictionary<string, string[]> { ["receivedAmount"] = ["El efectivo recibido no cubre la parte pagada en efectivo."] });
                sale.Payments.Add(new SalePayment { Method = payment.Method, Amount = payment.Amount, ReceivedAmount = receivedAmount, ChangeAmount = payment.Method == PaymentMethod.Cash ? receivedAmount - payment.Amount : 0 });
            }
        
            // Process loyalty cashback earning
            var tenantRecord = await db.Tenants.SingleAsync(x => x.Id == tenant.TenantId, cancellationToken);
            if (tenantRecord.LoyaltyActive && request.CustomerId.HasValue)
            {
                var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId.Value, cancellationToken);
                if (customer != null)
                {
                    var cashback = decimal.Round(sale.Total * (tenantRecord.LoyaltyCashbackPercent / 100m), 2);
                    customer.WalletBalance += cashback;
                }
            }
        
            db.Sales.Add(sale); await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { sale.Id, sale.Folio, sale.SoldAtUtc, sale.Subtotal, sale.Discount, sale.Total, Payments = sale.Payments.Select(x => new { Method = x.Method.ToString(), x.Amount, x.ReceivedAmount, x.ChangeAmount }), Items = sale.Items.Select(x => new { x.ProductName, x.VariantName, x.Sku, x.Quantity, x.UnitPrice, x.LineTotal }) });
        });
        
        api.MapGet("/sales", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Sales.AsNoTracking().OrderByDescending(x => x.SoldAtUtc).Take(100)
                .Select(x => new { x.Id, x.Folio, x.SoldAtUtc, Status = x.Status.ToString(), x.Total, ItemCount = x.Items.Sum(i => i.Quantity), PaymentMethod = x.Payments.Select(p => p.Method.ToString()).FirstOrDefault() })
                .ToListAsync(cancellationToken)));
        
        api.MapGet("/sales/{saleId:guid}", async (Guid saleId, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var sale = await db.Sales.AsNoTracking().Where(x => x.Id == saleId).Select(x => new { x.Id, x.Folio, x.SoldAtUtc, Status = x.Status.ToString(), x.Subtotal, x.Discount, x.Total, x.CancellationReason, Customer = x.Customer != null ? x.Customer.Name : "Público general", CustomerPhone = x.Customer != null ? x.Customer.Phone : null, CustomerEmail = x.Customer != null ? x.Customer.Email : null, Items = x.Items.Select(i => new { i.Id, i.ProductVariantId, i.ProductName, i.VariantName, i.Sku, i.Quantity, i.ReturnedQuantity, AvailableToReturn = i.Quantity - i.ReturnedQuantity, i.UnitPrice, i.LineTotal }), Payments = x.Payments.Select(p => new { Method = p.Method.ToString(), p.Amount, p.ReceivedAmount, p.ChangeAmount }) }).SingleOrDefaultAsync(cancellationToken);
            return sale is null ? Results.NotFound() : Results.Ok(sale);
        });
        
        api.MapPost("/sales/{saleId:guid}/return", async (Guid saleId, ReturnSaleRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0)) return Results.BadRequest();
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var sale = await db.Sales.Include(x => x.Items).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken); if (sale is null) return Results.NotFound();
            if (sale.Status == SaleStatus.Cancelled) return Results.Conflict(new { title = "La venta ya fue devuelta completamente." });
            foreach (var requestItem in request.Items)
            {
                var item = sale.Items.SingleOrDefault(x => x.Id == requestItem.SaleItemId); if (item is null || item.ReturnedQuantity + requestItem.Quantity > item.Quantity) return Results.ValidationProblem(new Dictionary<string,string[]>{{"quantity",["La cantidad excede lo disponible para devolución."]}});
                var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.BranchId == sale.BranchId && x.ProductVariantId == item.ProductVariantId, cancellationToken); if (balance is null) { balance = new InventoryBalance { BranchId = sale.BranchId, ProductVariantId = item.ProductVariantId, AverageCost = item.UnitCost }; db.InventoryBalances.Add(balance); }
                balance.Quantity += requestItem.Quantity; item.ReturnedQuantity += requestItem.Quantity;
                db.InventoryMovements.Add(new InventoryMovement { BranchId = sale.BranchId, ProductVariantId = item.ProductVariantId, Type = InventoryMovementType.Return, Quantity = requestItem.Quantity, UnitCost = item.UnitCost, Note = $"Devolución {sale.Folio}: {request.Reason}", PerformedByUserId = userId });
            }
            var fullyReturned = sale.Items.All(x => x.ReturnedQuantity == x.Quantity); sale.Status = fullyReturned ? SaleStatus.Cancelled : SaleStatus.PartiallyReturned; if (fullyReturned) { sale.CancelledAtUtc = DateTimeOffset.UtcNow; sale.CancelledByUserId = userId; } sale.CancellationReason = request.Reason;
            var refund = request.Items.Sum(r => sale.Items.Single(x => x.Id == r.SaleItemId).UnitPrice * r.Quantity);
            sale.Subtotal = Math.Max(0, sale.Subtotal - refund); sale.Total = Math.Max(0, sale.Total - refund);
            var remainingPayment = sale.Total;
            foreach (var payment in sale.Payments.OrderBy(x => x.CreatedAtUtc))
            {
                payment.Amount = Math.Min(payment.Amount, remainingPayment);
                remainingPayment -= payment.Amount;
                if (payment.Method != PaymentMethod.Cash) payment.ReceivedAmount = payment.Amount;
                payment.ChangeAmount = 0;
            }
            await db.SaveChangesAsync(cancellationToken); return Results.Ok(new { RefundAmount = refund, FullyReturned = fullyReturned });
        });
        
        api.MapPost("/sales/{saleId:guid}/cancel", async (Guid saleId, CancelSaleRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var sale = await db.Sales.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken);
            if (sale is null) return Results.NotFound();
            if (sale.Status == SaleStatus.Cancelled) return Results.Conflict(new { title = "La venta ya está cancelada." });
            foreach (var item in sale.Items)
            {
                var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.BranchId == sale.BranchId && x.ProductVariantId == item.ProductVariantId, cancellationToken);
                if (balance is null) { balance = new InventoryBalance { BranchId = sale.BranchId, ProductVariantId = item.ProductVariantId }; db.InventoryBalances.Add(balance); }
                balance.Quantity += item.Quantity;
                db.InventoryMovements.Add(new InventoryMovement { BranchId = sale.BranchId, ProductVariantId = item.ProductVariantId, Type = InventoryMovementType.Return, Quantity = item.Quantity, UnitCost = item.UnitCost, Note = $"Cancelación {sale.Folio}: {request.Reason}", PerformedByUserId = userId });
            }
            sale.Status = SaleStatus.Cancelled; sale.CancelledAtUtc = DateTimeOffset.UtcNow; sale.CancelledByUserId = userId; sale.CancellationReason = string.IsNullOrWhiteSpace(request.Reason) ? "Cancelación de venta" : request.Reason.Trim();
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        
        api.MapPost("/cash/{cashSessionId:guid}/close", async (Guid cashSessionId, CloseCashSessionRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.CountedAmount < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["countedAmount"] = ["El efectivo contado no puede ser negativo."] });
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
            var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Id == cashSessionId && x.OpenedByUserId == userId && x.Status == CashSessionStatus.Open, cancellationToken);
            if (cash is null) return Results.NotFound();
            var cashSales = await db.Sales.Where(x => x.CashSessionId == cash.Id && x.Status != SaleStatus.Cancelled).SelectMany(x => x.Payments).Where(x => x.Method == PaymentMethod.Cash).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
            var cashLayaways = await db.LayawayPayments.Where(x => x.CashSessionId == cash.Id && x.Method == PaymentMethod.Cash).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
            cash.ExpectedAmount = cash.OpeningAmount + cashSales + cashLayaways; cash.CountedAmount = request.CountedAmount; cash.DifferenceAmount = request.CountedAmount - cash.ExpectedAmount; cash.ClosedAtUtc = DateTimeOffset.UtcNow; cash.Status = CashSessionStatus.Closed;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { cash.Id, cash.OpeningAmount, CashSales = cashSales, CashLayaways = cashLayaways, cash.ExpectedAmount, cash.CountedAmount, cash.DifferenceAmount, cash.ClosedAtUtc });
        });
        
        return api;
    }
}
