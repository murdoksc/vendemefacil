using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"vendemefacil-tests-{Guid.NewGuid()}";

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-key-with-more-than-32-characters");
        Environment.SetEnvironmentVariable("ConnectionStrings__VendemeFacilDb", "Server=test;Database=test;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:VendemeFacilDb"] = "Server=test;Database=test;",
            ["Jwt:Key"] = "integration-test-key-with-more-than-32-characters",
            ["Jwt:Issuer"] = "VendemeFacil.Api",
            ["Jwt:Audience"] = "VendemeFacil.Web",
            ["FrontendUrl"] = "http://localhost"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VendemeFacilDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<VendemeFacilDbContext>>();
            services.RemoveAll<VendemeFacilDbContext>();
            services.AddDbContext<VendemeFacilDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
