using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class SalesLifecycleTests
{
    [Fact]
    public async Task SaleReturnAndCashCloseKeepInventoryAndCashConsistent()
    {
        var context = new TestTenantContext(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<VendemeFacilDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new VendemeFacilDbContext(options, context);
        var branch = new Branch { Name = "Matriz", IsMain = true };
        var user = new AppUser { DisplayName = "Caja", Email = "caja@example.com" };
        var product = new Product { Name = "Playera" };
        var variant = new ProductVariant { Product = product, Name = "M", Sku = "PLAYERA-M", Price = 100, Cost = 40 };
        product.Variants.Add(variant);
        var balance = new InventoryBalance { Branch = branch, ProductVariant = variant, Quantity = 10, AverageCost = 40 };
        var cash = new CashSession { BranchId = branch.Id, OpenedByUserId = user.Id, OpeningAmount = 200 };
        db.AddRange(branch, user, product, balance, cash);
        await db.SaveChangesAsync();

        var sale = new Sale { Folio = "V-TEST", BranchId = branch.Id, CashSessionId = cash.Id, SoldByUserId = user.Id, Subtotal = 200, Total = 200 };
        var item = new SaleItem { ProductVariantId = variant.Id, ProductName = product.Name, VariantName = variant.Name, Sku = variant.Sku, Quantity = 2, UnitPrice = 100, UnitCost = 40, LineTotal = 200 };
        sale.Items.Add(item);
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = 200, ReceivedAmount = 200 });
        balance.Quantity -= item.Quantity;
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        Assert.Equal(8, balance.Quantity);

        item.ReturnedQuantity = 1; sale.Status = SaleStatus.PartiallyReturned; sale.Subtotal -= 100; sale.Total -= 100;
        sale.Payments.Single().Amount = 100; balance.Quantity += 1;
        await db.SaveChangesAsync();
        Assert.Equal(9, balance.Quantity);
        Assert.Equal(100, sale.Total);

        cash.ExpectedAmount = cash.OpeningAmount + sale.Payments.Sum(x => x.Amount);
        cash.CountedAmount = 300; cash.DifferenceAmount = cash.CountedAmount - cash.ExpectedAmount; cash.Status = CashSessionStatus.Closed;
        await db.SaveChangesAsync();
        Assert.Equal(300, cash.ExpectedAmount);
        Assert.Equal(0, cash.DifferenceAmount);
    }

    private sealed class TestTenantContext(Guid id) : ITenantContext
    {
        public Guid TenantId { get; private set; } = id;
        public bool HasTenant => true;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
