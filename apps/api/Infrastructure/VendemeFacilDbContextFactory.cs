using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VendemeFacil.Api.Infrastructure;

public sealed class VendemeFacilDbContextFactory : IDesignTimeDbContextFactory<VendemeFacilDbContext>
{
    public VendemeFacilDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__VendemeFacilDb")
            ?? "Server=localhost;Database=VendemeFacil;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<VendemeFacilDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new VendemeFacilDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool HasTenant => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
