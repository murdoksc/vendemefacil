using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class SecurityAndTenantTests
{
    [Fact]
    public void JwtContainsTenantRoleAndSecurityVersion()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "integration-test-key-with-more-than-32-characters",
            ["Jwt:Issuer"] = "VendemeFacil.Api",
            ["Jwt:Audience"] = "VendemeFacil.Web"
        }).Build();
        var tenant = new Tenant { Name = "Tienda A", Slug = "tienda-a" };
        var user = new AppUser { DisplayName = "Ana", Email = "ana@example.com", Role = UserRole.Administrator, SecurityVersion = 7 };
        var response = new JwtTokenService(configuration).Create(tenant, user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal(tenant.Id.ToString(), token.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal(nameof(UserRole.Administrator), token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.Equal("7", token.Claims.Single(x => x.Type == "security_version").Value);
    }

    [Fact]
    public async Task QueryFiltersPreventReadingAnotherTenant()
    {
        var options = new DbContextOptionsBuilder<VendemeFacilDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var dbA = new VendemeFacilDbContext(options, new TestTenantContext(Guid.NewGuid()));
        dbA.Customers.Add(new Customer { Name = "Cliente A" });
        await dbA.SaveChangesAsync();

        await using var dbB = new VendemeFacilDbContext(options, new TestTenantContext(Guid.NewGuid()));
        Assert.Empty(await dbB.Customers.ToListAsync());
        Assert.Single(await dbB.Customers.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task SaveChangesOverwritesInjectedTenantId()
    {
        var tenant = new TestTenantContext(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<VendemeFacilDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new VendemeFacilDbContext(options, tenant);
        var customer = new Customer { Name = "Intruso", TenantId = Guid.NewGuid() };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        Assert.Equal(tenant.TenantId, customer.TenantId);
    }

    private sealed class TestTenantContext(Guid id) : ITenantContext
    {
        public Guid TenantId { get; private set; } = id;
        public bool HasTenant => true;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
