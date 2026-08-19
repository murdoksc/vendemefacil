using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Infrastructure;

public sealed class VendemeFacilDbContext(
    DbContextOptions<VendemeFacilDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<Layaway> Layaways => Set<Layaway>();
    public DbSet<LayawayItem> LayawayItems => Set<LayawayItem>();
    public DbSet<LayawayPayment> LayawayPayments => Set<LayawayPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Slug).HasMaxLength(100);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.PrimaryColor).HasMaxLength(7);
            entity.Property(x => x.AccentColor).HasMaxLength(7);
            entity.Property(x => x.ButtonColor).HasMaxLength(7);
            entity.Property(x => x.HoverColor).HasMaxLength(7);
            entity.Property(x => x.BackgroundColor).HasMaxLength(7);
            entity.Property(x => x.SurfaceColor).HasMaxLength(7);
            entity.Property(x => x.TextColor).HasMaxLength(7);
            entity.Property(x => x.LogoUrl).HasMaxLength(2048);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Address).HasMaxLength(300);
            entity.Property(x => x.TicketMessage).HasMaxLength(300);
        });

        modelBuilder.Entity<Branch>().HasAlternateKey(x => new { x.TenantId, x.Id });
        modelBuilder.Entity<AppUser>().HasAlternateKey(x => new { x.TenantId, x.Id });
        modelBuilder.Entity<Category>().HasAlternateKey(x => new { x.TenantId, x.Id });
        modelBuilder.Entity<Product>().HasAlternateKey(x => new { x.TenantId, x.Id });
        modelBuilder.Entity<ProductVariant>().HasAlternateKey(x => new { x.TenantId, x.Id });
        modelBuilder.Entity<Product>().Property(x => x.ImageUrl).HasMaxLength(500);
        modelBuilder.Entity<Customer>().HasAlternateKey(x => new { x.TenantId, x.Id });

        modelBuilder.Entity<Branch>()
            .HasOne(x => x.Tenant)
            .WithMany(x => x.Branches)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppUser>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.UsedAtUtc });
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Customer>().HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>()
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(nameof(Product.TenantId), nameof(Product.CategoryId))
            .HasPrincipalKey(nameof(Category.TenantId), nameof(Category.Id))
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariant>()
            .HasOne(x => x.Product)
            .WithMany(x => x.Variants)
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryBalance>()
            .HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryBalance>()
            .HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryMovement>()
            .HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryMovement>()
            .HasOne(x => x.PerformedByUser)
            .WithMany()
            .HasForeignKey(nameof(InventoryMovement.TenantId), nameof(InventoryMovement.PerformedByUserId))
            .HasPrincipalKey(nameof(AppUser.TenantId), nameof(AppUser.Id))
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryMovement>()
            .HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Branch>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<AppUser>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Customer>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Category>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Product>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<ProductVariant>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<InventoryBalance>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<InventoryMovement>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<CashSession>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Sale>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<SaleItem>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<SalePayment>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Layaway>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<LayawayItem>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<LayawayPayment>().HasQueryFilter(x => x.TenantId == tenantContext.TenantId);

        modelBuilder.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => new { x.TenantId, x.Name });
        modelBuilder.Entity<Customer>().Property(x => x.Name).HasMaxLength(160);
        modelBuilder.Entity<Customer>().Property(x => x.Phone).HasMaxLength(30);
        modelBuilder.Entity<Customer>().Property(x => x.Email).HasMaxLength(200);
        modelBuilder.Entity<Customer>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<Category>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        modelBuilder.Entity<ProductVariant>().HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        modelBuilder.Entity<ProductVariant>().HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        modelBuilder.Entity<InventoryBalance>().HasIndex(x => new { x.TenantId, x.BranchId, x.ProductVariantId }).IsUnique();

        modelBuilder.Entity<ProductVariant>().Property(x => x.Cost).HasPrecision(18, 2);
        modelBuilder.Entity<ProductVariant>().Property(x => x.Price).HasPrecision(18, 2);
        modelBuilder.Entity<ProductVariant>().Property(x => x.MinimumStock).HasPrecision(18, 3);
        modelBuilder.Entity<InventoryBalance>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<InventoryBalance>().Property(x => x.AverageCost).HasPrecision(18, 2);
        modelBuilder.Entity<InventoryMovement>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<InventoryMovement>().Property(x => x.UnitCost).HasPrecision(18, 2);
        modelBuilder.Entity<CashSession>().HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        modelBuilder.Entity<Sale>().HasIndex(x => new { x.TenantId, x.Folio }).IsUnique();
        modelBuilder.Entity<Sale>().HasMany(x => x.Items).WithOne(x => x.Sale).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Sale>().HasMany(x => x.Payments).WithOne(x => x.Sale).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Sale>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => new { x.TenantId, x.CustomerId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CashSession>().Property(x => x.OpeningAmount).HasPrecision(18, 2);
        modelBuilder.Entity<CashSession>().Property(x => x.CountedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<CashSession>().Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<CashSession>().Property(x => x.DifferenceAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Sale>().Property(x => x.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<Sale>().Property(x => x.Discount).HasPrecision(18, 2);
        modelBuilder.Entity<Sale>().Property(x => x.Total).HasPrecision(18, 2);
        modelBuilder.Entity<SaleItem>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<SaleItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<SaleItem>().Property(x => x.UnitCost).HasPrecision(18, 2);
        modelBuilder.Entity<SaleItem>().Property(x => x.LineTotal).HasPrecision(18, 2);
        modelBuilder.Entity<SaleItem>().Property(x => x.ReturnedQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<SalePayment>().Property(x => x.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<SalePayment>().Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<SalePayment>().Property(x => x.ChangeAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Layaway>().HasIndex(x => new { x.TenantId, x.Folio }).IsUnique();
        modelBuilder.Entity<Layaway>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => new { x.TenantId, x.CustomerId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Layaway>().HasMany(x => x.Items).WithOne(x => x.Layaway).HasForeignKey(x => x.LayawayId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Layaway>().HasMany(x => x.Payments).WithOne(x => x.Layaway).HasForeignKey(x => x.LayawayId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Layaway>().Property(x => x.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Layaway>().Property(x => x.Notes).HasMaxLength(500);
        modelBuilder.Entity<LayawayItem>().Property(x => x.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<LayawayItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<LayawayItem>().Property(x => x.LineTotal).HasPrecision(18, 2);
        modelBuilder.Entity<LayawayPayment>().Property(x => x.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<LayawayPayment>().Property(x => x.Note).HasMaxLength(300);
        modelBuilder.Entity<LayawayPayment>().HasOne(x => x.CashSession).WithMany().HasForeignKey(x => new { x.TenantId, x.CashSessionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Property(nameof(ITenantEntity.TenantId)).IsModified)
                throw new InvalidOperationException("Business data cannot be moved to another tenant.");
        }

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>().Where(x => x.State == EntityState.Added))
        {
            if (!tenantContext.HasTenant)
                throw new InvalidOperationException("A tenant is required to create business data.");

            entry.Entity.TenantId = tenantContext.TenantId;
        }

        foreach (var entry in ChangeTracker.Entries<Entity>().Where(x => x.State == EntityState.Modified))
            entry.Entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        return base.SaveChangesAsync(cancellationToken);
    }
}
