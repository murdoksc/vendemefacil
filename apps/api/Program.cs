using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Data;
using System.Threading.RateLimiting;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;
using VendemeFacil.Api.Features.Business;
using VendemeFacil.Api.Features.Layaways;
using VendemeFacil.Api.Features.Sales;
using VendemeFacil.Api.Features.Inventory;
using VendemeFacil.Api.Features.Reporting;
using VendemeFacil.Api.Features.Identity;
using VendemeFacil.Api.Features.Account;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"];
var databaseConnection = builder.Configuration.GetConnectionString("VendemeFacilDb");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");
if (string.IsNullOrWhiteSpace(databaseConnection))
    throw new InvalidOperationException("ConnectionStrings:VendemeFacilDb must be configured.");

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<OutboundEmailQueue>();
builder.Services.AddHostedService<OutboundEmailWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.FindFirstValue("platform_admin") == "true")
                {
                    var configuredEmail = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["PlatformAdmin:Email"]?.Trim().ToLowerInvariant();
                    var tokenEmail = context.Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(configuredEmail) || tokenEmail != configuredEmail)
                        context.Fail("Administrador de plataforma inválido.");
                    return;
                }
                var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue("sub");
                var versionValue = context.Principal?.FindFirstValue("security_version");
                var tokenVersion = int.TryParse(versionValue, out var parsedVersion) ? parsedVersion : 0;
                if (!Guid.TryParse(userIdValue, out var userId))
                {
                    context.Fail("Usuario inválido.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<VendemeFacilDbContext>();
                var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
                if (user is null || !user.IsActive || user.SecurityVersion != tokenVersion)
                    context.Fail("La sesión ya no es válida.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("platform-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("public-leads", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("document-email", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("qz-signing", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddDbContext<VendemeFacilDbContext>(options =>
    options.UseSqlServer(databaseConnection, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["FrontendUrl"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<VendemeFacilDbContext>();
    await migrationDb.Database.MigrateAsync();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    logger.LogError(exception, "Error no controlado. TraceId: {TraceId}", context.TraceIdentifier);
    await Results.Problem(
        title: "No pudimos completar la operación",
        detail: app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")
            ? exception?.Message
            : $"Ocurrió un error inesperado. Comparte este código con soporte: {context.TraceIdentifier}",
        statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
}));
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    service = "VendemeFacil.Api",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/api/health/database", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem(title: "Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapGet("/api/qz/certificate", (IConfiguration configuration) =>
{
    var encodedCertificate = configuration["Qz:CertificateBase64"]?.Trim();
    if (string.IsNullOrWhiteSpace(encodedCertificate))
        return Results.NotFound();

    try
    {
        var certificate = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCertificate));
        return Results.Text(certificate, "text/plain", Encoding.UTF8);
    }
    catch (FormatException)
    {
        return Results.Problem(title: "El certificado QZ no tiene un formato valido.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapIdentityPlatformEndpoints();
var publicGroup = app.MapGroup("/api/v1/public");

publicGroup.MapGet("/catalog/{slug}", async (
    string slug,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    var trimmedSlug = slug.Trim().ToLowerInvariant();
    
    var tenant = await db.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.Slug == trimmedSlug, cancellationToken);

    if (tenant == null || !tenant.IsActive)
        return Results.NotFound();

    var isLocked = tenant.PlanCode == "esencial";

    object categories;
    object products;

    if (isLocked)
    {
        categories = new List<object>();
        products = new List<object>();
    }
    else
    {
        categories = await db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.IsActive)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        products = await db.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.IsActive && x.Product.IsActive)
            .OrderBy(x => x.Product.Name)
            .Select(x => new
            {
                x.ProductId,
                ProductName = x.Product.Name,
                CategoryName = x.Product.Category != null ? x.Product.Category.Name : null,
                x.Product.CategoryId,
                x.Product.ImageUrl,
                VariantId = x.Id,
                VariantName = x.Name,
                x.Sku,
                x.Barcode,
                x.Price,
                Stock = db.InventoryBalances.IgnoreQueryFilters().Where(b => b.TenantId == tenant.Id && b.ProductVariantId == x.Id).Sum(b => b.Quantity)
            })
            .ToListAsync(cancellationToken);
    }

    return Results.Ok(new
    {
        Business = new
        {
            tenant.Name,
            tenant.Slug,
            tenant.PrimaryColor,
            tenant.AccentColor,
            tenant.ButtonColor,
            tenant.HoverColor,
            tenant.BackgroundColor,
            tenant.SurfaceColor,
            tenant.TextColor,
            tenant.CornerRadius,
            tenant.Phone,
            tenant.Address,
            tenant.LogoUrl,
            PlanCode = tenant.PlanCode,
            IsLocked = isLocked
        },
        Categories = categories,
        Products = products
    });
});

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.AddEndpointFilter(async (context, next) =>
{
    var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
    return tenant.HasTenant
        ? await next(context)
        : Results.Problem(
            title: "Negocio no identificado",
            detail: "La solicitud debe incluir el encabezado X-Tenant-Id.",
            statusCode: StatusCodes.Status400BadRequest);
});

api.MapAccountEndpoints();
api.MapReportingDocumentEndpoints();
api.MapBusinessEndpoints();

api.MapInventoryCatalogEndpoints();
api.MapSalesCashEndpoints();
api.MapLayawayEndpoints();
app.Run();

public partial class Program;
