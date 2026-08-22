using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class AuthenticationApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthenticationApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ProtectedEndpointRejectsAnonymousRequests()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/customers")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedTenantsCannotReadEachOthersCustomers()
    {
        using var client = _factory.CreateClient();
        var first = await Register(client, $"tienda-a-{Guid.NewGuid():N}", $"a-{Guid.NewGuid():N}@example.com");
        var second = await Register(client, $"tienda-b-{Guid.NewGuid():N}", $"b-{Guid.NewGuid():N}@example.com");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/customers", new SaveCustomerRequest("Solo A", null, null, null))).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        var customers = await client.GetFromJsonAsync<JsonElement>("/api/v1/customers");
        Assert.Equal(0, customers.GetArrayLength());
    }

    [Fact]
    public async Task CashierCannotUseOwnerAdministrationEndpoints()
    {
        using var client = _factory.CreateClient();
        var owner = await Register(client, $"roles-{Guid.NewGuid():N}", $"owner-{Guid.NewGuid():N}@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VendemeFacilDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(x => x.Id == owner.User.TenantId);
            tenant.PlanCode = "pro";
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var cashierEmail = $"cashier-{Guid.NewGuid():N}@example.com";
        var created = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("Caja", cashierEmail, "Password123!", "Cashier", false));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.User.BusinessSlug, cashierEmail, "Password123!"));
        login.EnsureSuccessStatusCode();
        var cashier = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashier.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/sales")).StatusCode);
    }

    private static async Task<AuthResponse> Register(HttpClient client, string business, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterBusinessRequest(business, "Propietario", email, "Password123!", "negocio", null, null, null, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
}
