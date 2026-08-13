namespace VendemeFacil.Api.Infrastructure;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
    void SetTenant(Guid tenantId);
}

public sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId
    {
        get
        {
            if (_tenantId.HasValue) return _tenantId.Value;
            var context = httpContextAccessor.HttpContext;
            var value = context?.User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrWhiteSpace(value) && context?.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() == true)
                value = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            return Guid.TryParse(value, out var tenantId) ? tenantId : Guid.Empty;
        }
    }

    public bool HasTenant => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId) => _tenantId = tenantId;
}
