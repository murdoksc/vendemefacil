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
});
builder.Services.AddDbContext<VendemeFacilDbContext>(options =>
    options.UseSqlServer(databaseConnection, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["FrontendUrl"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

await using (var migrationScope = app.Services.CreateAsyncScope())
{
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
        detail: app.Environment.IsDevelopment()
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

var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", async (
    RegisterBusinessRequest request,
    ITenantContext tenantContext,
    IPasswordHasher<AppUser> passwordHasher,
    JwtTokenService tokens,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    var errors = new Dictionary<string, string[]>();
    if (request.BusinessName.Trim().Length < 2) errors["businessName"] = ["Escribe el nombre del negocio."];
    if (request.OwnerName.Trim().Length < 2) errors["ownerName"] = ["Escribe el nombre del propietario."];
    if (!request.Email.Contains('@')) errors["email"] = ["Escribe un correo válido."];
    if (request.Password.Length < 8) errors["password"] = ["La contraseña debe contener al menos 8 caracteres."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var baseSlug = Regex.Replace(request.BusinessName.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "negocio";
    var slug = baseSlug;
    var suffix = 1;
    while (await db.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken)) slug = $"{baseSlug}-{++suffix}";

    var tenant = new Tenant { Name = request.BusinessName.Trim(), Slug = slug };
    tenantContext.SetTenant(tenant.Id);
    db.Tenants.Add(tenant);
    var owner = new AppUser
    {
        DisplayName = request.OwnerName.Trim(),
        Email = request.Email.Trim().ToLowerInvariant(),
        Role = UserRole.Owner,
        CanViewCosts = true
    };
    owner.PasswordHash = passwordHasher.HashPassword(owner, request.Password);
    db.Users.Add(owner);
    db.Branches.Add(new Branch { Name = "Sucursal principal", IsMain = true });
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(tokens.Create(tenant, owner));
});

auth.MapPost("/login", async (
    LoginRequest request,
    IPasswordHasher<AppUser> passwordHasher,
    JwtTokenService tokens,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    var slug = (request.BusinessSlug ?? string.Empty).Trim().ToLowerInvariant();
    var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
    var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.IsActive, cancellationToken);
    if (tenant is null) return Results.Unauthorized();

    var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(
        x => x.TenantId == tenant.Id && x.Email == email && x.IsActive,
        cancellationToken);
    if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    return Results.Ok(tokens.Create(tenant, user));
});

auth.MapPost("/forgot-password", async (
    ForgotPasswordRequest request,
    OutboundEmailQueue emailQueue,
    IConfiguration configuration,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    var elapsed = Stopwatch.StartNew();
    var slug = request.BusinessSlug.Trim().ToLowerInvariant();
    var email = request.Email.Trim().ToLowerInvariant();
    var tenant = await db.Tenants.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Slug == slug && x.IsActive, cancellationToken);
    AppUser? user = null;
    if (tenant is not null)
        user = await db.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Email == email && x.IsActive, cancellationToken);

    if (tenant is not null && user is not null)
    {
        var now = DateTimeOffset.UtcNow;
        var recoveryRecentlyRequested = await db.PasswordResetTokens.AsNoTracking()
            .AnyAsync(x => x.UserId == user.Id && x.CreatedAtUtc > now.AddMinutes(-2), cancellationToken);
        if (recoveryRecentlyRequested)
            goto RecoveryResponse;

        await db.PasswordResetTokens
            .Where(x => x.UserId == user.Id && x.UsedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAtUtc, now), cancellationToken);

        var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiresAtUtc = now.AddMinutes(30)
        });
        await db.SaveChangesAsync(cancellationToken);

        var frontendBaseUrl = (configuration["Email:FrontendBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        emailQueue.TryQueue(OutboundEmailFactory.PasswordReset(
            user.Email,
            user.DisplayName,
            tenant.Name,
            $"{frontendBaseUrl}/reset-password#token={Uri.EscapeDataString(token)}"));
    }

RecoveryResponse:
    var minimumDuration = TimeSpan.FromMilliseconds(350);
    if (elapsed.Elapsed < minimumDuration)
        await Task.Delay(minimumDuration - elapsed.Elapsed, cancellationToken);

    return Results.Accepted(value: new
    {
        message = "Si los datos corresponden a una cuenta activa, recibirás un correo con instrucciones."
    });
}).RequireRateLimiting("password-recovery");

auth.MapPost("/reset-password", async (
    ResetPasswordRequest request,
    IPasswordHasher<AppUser> passwordHasher,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["La contraseña debe contener al menos 8 caracteres."] });
    if (request.NewPassword != request.ConfirmPassword)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["confirmPassword"] = ["Las contraseñas no coinciden."] });
    if (string.IsNullOrWhiteSpace(request.Token))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = ["El enlace no es válido o ya venció."] });

    var now = DateTimeOffset.UtcNow;
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
    IResult result = Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = ["El enlace no es válido o ya venció."] });
    var executionStrategy = db.Database.CreateExecutionStrategy();
    await executionStrategy.ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var resetToken = await db.PasswordResetTokens.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken);
        if (resetToken is null)
            return;

        var claimed = await db.PasswordResetTokens
            .Where(x => x.Id == resetToken.Id && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAtUtc, now), cancellationToken);
        if (claimed != 1)
            return;

        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.Id == resetToken.UserId && x.TenantId == resetToken.TenantId && x.IsActive,
            cancellationToken);
        if (user is null)
            return;

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityVersion++;
        await db.PasswordResetTokens
            .Where(x => x.UserId == user.Id && x.UsedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAtUtc, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        result = Results.NoContent();
    });
    return result;
}).RequireRateLimiting("password-recovery");

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

api.MapPost("/documents/email", async (
    SendDocumentEmailRequest request,
    ITenantContext tenantContext,
    OutboundEmailQueue emailQueue,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sale-ticket"] = "Ticket de venta",
        ["layaway-receipt"] = "Comprobante de apartado",
        ["layaway-reminder"] = "Recordatorio de apartado",
        ["cash-report"] = "Corte de caja"
    };
    var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
    var reference = (request.Reference ?? string.Empty).Trim();
    var content = (request.Content ?? string.Empty).Trim();
    if (!labels.TryGetValue(request.DocumentType ?? string.Empty, out var label)
        || !System.Net.Mail.MailAddress.TryCreate(email, out _)
        || reference.Length is < 1 or > 100
        || content.Length is < 1 or > 20000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Revisa el correo y el contenido del documento."] });

    var businessName = await db.Tenants.AsNoTracking()
        .Where(x => x.Id == tenantContext.TenantId)
        .Select(x => x.Name)
        .SingleAsync(cancellationToken);
    if (!emailQueue.TryQueue(OutboundEmailFactory.Document(email, businessName, label, reference, content)))
        return Results.Problem(title: "No pudimos preparar el correo.", statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Accepted(value: new { message = $"{label} enviado a {email}." });
}).RequireRateLimiting("document-email");

var userAdmin = api.MapGroup("/users").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
userAdmin.MapGet("/", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Users.AsNoTracking().OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.DisplayName, x.Email, Role = x.Role.ToString(), x.CanViewCosts, x.IsActive }).ToListAsync(cancellationToken)));
userAdmin.MapPost("/", async (CreateUserRequest request, IPasswordHasher<AppUser> passwordHasher, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) || !request.Email.Contains('@') || request.Password.Length < 8 || !Enum.TryParse<UserRole>(request.Role, true, out var role))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = ["Revisa nombre, correo, contraseña y rol."] });
    var email = request.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken)) return Results.Conflict(new { title = "Ya existe un usuario con ese correo." });
    var user = new AppUser { DisplayName = request.DisplayName.Trim(), Email = email, Role = role, CanViewCosts = request.CanViewCosts };
    user.PasswordHash = passwordHasher.HashPassword(user, request.Password); db.Users.Add(user); await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/users/{user.Id}", new { user.Id });
});
userAdmin.MapPut("/{userId:guid}", async (Guid userId, UpdateUserRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<UserRole>(request.Role, true, out var role) || string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest();
    var currentId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
    var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken); if (user is null) return Results.NotFound();
    if (user.Id == currentId && !request.IsActive) return Results.Conflict(new { title = "No puedes desactivar tu propia cuenta." });
    user.DisplayName = request.DisplayName.Trim(); user.Role = role; user.CanViewCosts = request.CanViewCosts; user.IsActive = request.IsActive;
    await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
});

api.MapGet("/branches", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderByDescending(x => x.IsMain)
        .Select(x => new { x.Id, x.Name, x.IsMain }).ToListAsync(cancellationToken)));

api.MapGet("/customers", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.Notes, x.IsActive, Purchases = db.Sales.Count(s => s.CustomerId == x.Id && s.Status != SaleStatus.Cancelled), TotalSpent = db.Sales.Where(s => s.CustomerId == x.Id && s.Status != SaleStatus.Cancelled).Sum(s => (decimal?)s.Total) ?? 0 })
        .ToListAsync(cancellationToken)));
api.MapPost("/customers", async (SaveCustomerRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["El nombre es obligatorio."] });
    var customer = new Customer { Name = request.Name.Trim(), Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(), Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(), Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim() };
    db.Customers.Add(customer); await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/v1/customers/{customer.Id}", new { customer.Id });
});
api.MapPut("/customers/{customerId:guid}", async (Guid customerId, SaveCustomerRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken); if (customer is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
    customer.Name = request.Name.Trim(); customer.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(); customer.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(); customer.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(); customer.IsActive = request.IsActive;
    await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
});

api.MapGet("/business/settings", async (ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
    return Results.Ok(new BusinessSettingsResponse(tenant.Name, tenant.Slug, tenant.PrimaryColor, tenant.AccentColor, tenant.ButtonColor, tenant.HoverColor, tenant.BackgroundColor, tenant.SurfaceColor, tenant.TextColor, tenant.CornerRadius, tenant.LayawayReminderDaysBefore, tenant.AllowNegativeStock, tenant.LogoUrl, tenant.OperationMode.ToString(), tenant.Phone, tenant.Address, tenant.TicketMessage));
});

api.MapPut("/business/settings", async (UpdateBusinessSettingsRequest request, ITenantContext tenantContext, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var colors = new[] { request.PrimaryColor, request.AccentColor, request.ButtonColor, request.HoverColor, request.BackgroundColor, request.SurfaceColor, request.TextColor };
    if (string.IsNullOrWhiteSpace(request.Name) || colors.Any(color => !Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$")) || request.CornerRadius is < 0 or > 24 || request.LayawayReminderDaysBefore is < 0 or > 30)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Revisa el nombre y los colores seleccionados."] });
    var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantContext.TenantId, cancellationToken);
    tenant.Name = request.Name.Trim(); tenant.PrimaryColor = request.PrimaryColor; tenant.AccentColor = request.AccentColor; tenant.ButtonColor = request.ButtonColor; tenant.HoverColor = request.HoverColor; tenant.BackgroundColor = request.BackgroundColor; tenant.SurfaceColor = request.SurfaceColor; tenant.TextColor = request.TextColor; tenant.CornerRadius = request.CornerRadius; tenant.LayawayReminderDaysBefore = request.LayawayReminderDaysBefore; tenant.AllowNegativeStock = request.AllowNegativeStock;
    var logoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
    if (logoUrl is not null && (logoUrl.Length > 2048 || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var logoUri) || (logoUri.Scheme != Uri.UriSchemeHttp && logoUri.Scheme != Uri.UriSchemeHttps)))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["logoUrl"] = ["La URL del logotipo debe ser una dirección HTTP o HTTPS válida de hasta 2,048 caracteres."] });
    tenant.LogoUrl = logoUrl;
    tenant.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
    tenant.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
    tenant.TicketMessage = string.IsNullOrWhiteSpace(request.TicketMessage) ? null : request.TicketMessage.Trim();
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new BusinessSettingsResponse(tenant.Name, tenant.Slug, tenant.PrimaryColor, tenant.AccentColor, tenant.ButtonColor, tenant.HoverColor, tenant.BackgroundColor, tenant.SurfaceColor, tenant.TextColor, tenant.CornerRadius, tenant.LayawayReminderDaysBefore, tenant.AllowNegativeStock, tenant.LogoUrl, tenant.OperationMode.ToString(), tenant.Phone, tenant.Address, tenant.TicketMessage));
});

var inventoryAdmin = api.MapGroup("/inventory").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
inventoryAdmin.MapGet("/movements", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.InventoryMovements.AsNoTracking().Where(x => x.Type == InventoryMovementType.Entry || x.Type == InventoryMovementType.InitialStock)
        .OrderByDescending(x => x.CreatedAtUtc).Take(25)
        .Select(x => new { x.Id, x.ProductVariantId, Product = x.ProductVariant.Product.Name, Variant = x.ProductVariant.Name, x.Quantity, x.UnitCost, x.Note, x.CreatedAtUtc })
        .ToListAsync(cancellationToken)));

inventoryAdmin.MapGet("/kardex", async (Guid? productVariantId, DateTimeOffset? from, DateTimeOffset? to, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.InventoryMovements.AsNoTracking().AsQueryable();
    if (productVariantId.HasValue) query = query.Where(x => x.ProductVariantId == productVariantId.Value);
    if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from.Value);
    if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < to.Value);
    return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Take(1000).Select(x => new { x.Id, x.CreatedAtUtc, Type = x.Type.ToString(), x.Quantity, x.UnitCost, x.Note, Product = x.ProductVariant.Product.Name, Variant = x.ProductVariant.Name, x.ProductVariant.Sku, User = x.PerformedByUser != null ? x.PerformedByUser.DisplayName : null, Branch = x.Branch.Name }).ToListAsync(cancellationToken));
});

inventoryAdmin.MapPost("/physical-count", async (PhysicalCountRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (request.Items.Count == 0) return Results.BadRequest(new { title = "Agrega al menos un producto al conteo." });
    if (request.Items.Any(x => x.CountedQuantity < 0)) return Results.BadRequest(new { title = "Las cantidades contadas no pueden ser negativas." });
    if (request.Items.Select(x => x.ProductVariantId).Distinct().Count() != request.Items.Count) return Results.BadRequest(new { title = "El conteo contiene productos repetidos." });
    if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive, cancellationToken)) return Results.BadRequest(new { title = "La sucursal seleccionada no está disponible." });
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
    var ids = request.Items.Select(x => x.ProductVariantId).Distinct().ToList();
    var variants = await db.ProductVariants.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
    if (variants.Count != ids.Count) return Results.BadRequest(new { title = "Uno o más productos del conteo ya no existen." });
    var balances = await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, cancellationToken);
    foreach (var item in request.Items)
    {
        if (!variants.TryGetValue(item.ProductVariantId, out var variant)) return Results.BadRequest(new { title = "Uno de los productos del conteo ya no existe." });
        if (!balances.TryGetValue(item.ProductVariantId, out var balance)) { balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = item.ProductVariantId, AverageCost = variant.Cost }; db.InventoryBalances.Add(balance); }
        var difference = item.CountedQuantity - balance.Quantity;
        if (difference == 0) continue;
        balance.Quantity = item.CountedQuantity;
        db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = item.ProductVariantId, Type = InventoryMovementType.Adjustment, Quantity = difference, UnitCost = balance.AverageCost, Note = $"Conteo físico: {request.Note}".Trim(), PerformedByUserId = userId });
    }
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { Updated = request.Items.Count });
});

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
    sale.Discount = decimal.Round(request.Discount, 2); sale.Total = sale.Subtotal - sale.Discount;
    var requestedPayments = request.Payments is { Count: > 0 } ? request.Payments : [new PaymentPartRequest(request.PaymentMethod, sale.Total, request.ReceivedAmount)];
    if (requestedPayments.Any(x => !Enum.IsDefined(x.Method) || x.Amount <= 0) || decimal.Round(requestedPayments.Sum(x => x.Amount), 2) != sale.Total)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["payments"] = ["La suma de los pagos debe ser exactamente igual al total de la venta."] });
    foreach (var payment in requestedPayments)
    {
        var receivedAmount = payment.Method == PaymentMethod.Cash ? (payment.ReceivedAmount > 0 ? payment.ReceivedAmount : payment.Amount) : payment.Amount;
        if (receivedAmount < payment.Amount) return Results.ValidationProblem(new Dictionary<string, string[]> { ["receivedAmount"] = ["El efectivo recibido no cubre la parte pagada en efectivo."] });
        sale.Payments.Add(new SalePayment { Method = payment.Method, Amount = payment.Amount, ReceivedAmount = receivedAmount, ChangeAmount = payment.Method == PaymentMethod.Cash ? receivedAmount - payment.Amount : 0 });
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

api.MapGet("/products", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var products = await db.ProductVariants
        .AsNoTracking()
        .Where(x => x.IsActive && x.Product.IsActive)
        .OrderBy(x => x.Product.Name)
        .Select(x => new ProductResponse(
            x.ProductId,
            x.Product.Name,
            x.Product.Category != null ? x.Product.Category.Name : null,
            x.Product.CategoryId,
            x.Product.ImageUrl,
            x.Id,
            x.Name,
            x.Sku,
            x.Barcode,
            x.Cost,
            x.Price,
            db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity),
            x.MinimumStock,
            x.IsActive))
        .ToListAsync(cancellationToken);

    return Results.Ok(products);
});

api.MapGet("/categories", async (VendemeFacilDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync(cancellationToken)));
api.MapPost("/categories", async (SaveCategoryRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
    var name=request.Name.Trim(); if(await db.Categories.AnyAsync(x=>x.Name==name,cancellationToken)) return Results.Conflict(new {title="La categoría ya existe."});
    var category=new Category{Name=name}; db.Categories.Add(category); await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/v1/categories/{category.Id}",new{category.Id,category.Name});
});

api.MapGet("/products/next-sku", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var skus = await db.ProductVariants.AsNoTracking().Select(x => x.Sku).ToListAsync(cancellationToken);
    var lastNumericSku = skus.Select(x => long.TryParse(x, out var value) ? value : 0).DefaultIfEmpty(0).Max();
    return Results.Ok(new { Sku = (lastNumericSku + 1).ToString() });
});

api.MapPost("/products/{productId:guid}/variants", async (Guid productId, CreateVariantRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken); if (product is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Sku) || request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0 || request.InitialStock < 0) return Results.BadRequest();
    var sku=request.Sku.Trim(); var barcode=string.IsNullOrWhiteSpace(request.Barcode)?null:request.Barcode.Trim();
    if(await db.ProductVariants.AnyAsync(x=>x.Sku==sku,cancellationToken)) return Results.Conflict(new{title="Ya existe otro producto con ese SKU."});
    if(barcode is not null && await db.ProductVariants.AnyAsync(x=>x.Barcode==barcode,cancellationToken)) return Results.Conflict(new{title="Ya existe otro producto con ese código de barras."});
    var variant=new ProductVariant{ProductId=product.Id,Name=string.IsNullOrWhiteSpace(request.VariantName)?"Única":request.VariantName.Trim(),Sku=sku,Barcode=barcode,Cost=request.Cost,Price=request.Price,MinimumStock=request.MinimumStock}; db.ProductVariants.Add(variant);
    if(request.InitialStock>0){if(request.BranchId is null)return Results.BadRequest();db.InventoryBalances.Add(new InventoryBalance{BranchId=request.BranchId.Value,ProductVariantId=variant.Id,Quantity=request.InitialStock,AverageCost=request.Cost});db.InventoryMovements.Add(new InventoryMovement{BranchId=request.BranchId.Value,ProductVariantId=variant.Id,Type=InventoryMovementType.InitialStock,Quantity=request.InitialStock,UnitCost=request.Cost,Note="Existencia inicial"});}
    await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/v1/products/{productId}/variants/{variant.Id}",new{variant.Id});
});

api.MapPost("/products/import", async (ImportProductsRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (request.Rows.Count == 0) return Results.BadRequest(new { title = "El archivo no contiene productos." });
    if (request.Rows.Count > 2000) return Results.BadRequest(new { title = "El archivo supera el límite de 2,000 productos por importación." });
    if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId, cancellationToken))
        return Results.BadRequest(new { title = "La sucursal seleccionada no existe o ya no está disponible." });
    for (var index = 0; index < request.Rows.Count; index++)
    {
        var row = request.Rows[index];
        var csvRow = index + 2;
        if (string.IsNullOrWhiteSpace(row.Name)) return Results.BadRequest(new { title = $"Fila {csvRow}: el nombre del producto es obligatorio." });
        if (string.IsNullOrWhiteSpace(row.Sku)) return Results.BadRequest(new { title = $"Fila {csvRow}: el SKU es obligatorio." });
        if (row.Name.Trim().Length > 160) return Results.BadRequest(new { title = $"Fila {csvRow}: el nombre no puede superar 160 caracteres." });
        if (row.ImageUrl?.Trim().Length > 500) return Results.BadRequest(new { title = $"Fila {csvRow}: la URL de imagen no puede superar 500 caracteres." });
        if (row.Cost < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el costo no puede ser negativo." });
        if (row.Price < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el precio no puede ser negativo." });
        if (row.MinimumStock < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: el stock mínimo no puede ser negativo." });
        if (row.InitialStock < 0) return Results.BadRequest(new { title = $"Fila {csvRow}: la existencia no puede ser negativa." });
    }

    var duplicateSku = request.Rows.GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
    if (duplicateSku is not null) return Results.Conflict(new { title = $"El SKU {duplicateSku.Key} está repetido en el archivo." });
    var duplicateBarcode = request.Rows.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)).GroupBy(x => x.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
    if (duplicateBarcode is not null) return Results.Conflict(new { title = $"El código de barras {duplicateBarcode.Key} está repetido en el archivo." });

    var requestedSkus = request.Rows.Select(x => x.Sku.Trim()).ToList();
    var existingVariants = await db.ProductVariants.Include(x => x.Product).Where(x => requestedSkus.Contains(x.Sku)).ToListAsync(cancellationToken);
    var variantsBySku = existingVariants.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
    var requestedBarcodes = request.Rows.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)).Select(x => x.Barcode!.Trim()).ToList();
    var barcodeOwners = await db.ProductVariants.Where(x => x.Barcode != null && requestedBarcodes.Contains(x.Barcode)).Select(x => new { x.Sku, x.Barcode }).ToListAsync(cancellationToken);
    var barcodeConflict = barcodeOwners.FirstOrDefault(owner => request.Rows.Any(row => string.Equals(row.Barcode?.Trim(), owner.Barcode, StringComparison.OrdinalIgnoreCase) && !string.Equals(row.Sku.Trim(), owner.Sku, StringComparison.OrdinalIgnoreCase)));
    if (barcodeConflict is not null) return Results.Conflict(new { title = $"El código de barras {barcodeConflict.Barcode} ya pertenece a otro producto." });

    var categories = (await db.Categories.ToListAsync(cancellationToken)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    var existingIds = existingVariants.Select(x => x.Id).ToList();
    var balancesByVariant = (await db.InventoryBalances.Where(x => x.BranchId == request.BranchId && existingIds.Contains(x.ProductVariantId)).ToListAsync(cancellationToken)).ToDictionary(x => x.ProductVariantId);
    var imported = 0;
    var updated = 0;
    foreach (var row in request.Rows)
    {
        Category? category = null;
        if (!string.IsNullOrWhiteSpace(row.Category))
        {
            var categoryName = row.Category.Trim();
            if (!categories.TryGetValue(categoryName, out category))
            {
                category = new Category { Name = categoryName };
                db.Categories.Add(category);
                categories[categoryName] = category;
            }
        }

        if (variantsBySku.TryGetValue(row.Sku.Trim(), out var variant))
        {
            variant.Product.Name = row.Name.Trim();
            variant.Product.Category = category;
            variant.Product.CategoryId = category?.Id;
            variant.Product.ImageUrl = string.IsNullOrWhiteSpace(row.ImageUrl) ? null : row.ImageUrl.Trim();
            variant.Name = string.IsNullOrWhiteSpace(row.Variant) ? "Única" : row.Variant.Trim();
            variant.Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim();
            variant.Cost = row.Cost;
            variant.Price = row.Price;
            variant.MinimumStock = row.MinimumStock;

            balancesByVariant.TryGetValue(variant.Id, out var balance);
            var previousStock = balance?.Quantity ?? 0;
            var difference = row.InitialStock - previousStock;
            if (balance is null)
            {
                balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, Quantity = row.InitialStock, AverageCost = row.Cost };
                db.InventoryBalances.Add(balance);
            }
            else
            {
                balance.Quantity = row.InitialStock;
                balance.AverageCost = row.Cost;
            }
            if (difference != 0)
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.Adjustment, Quantity = difference, UnitCost = row.Cost, Note = "Actualización mediante importación CSV" });
            updated++;
        }
        else
        {
            var product = new Product { Name = row.Name.Trim(), Category = category, ImageUrl = string.IsNullOrWhiteSpace(row.ImageUrl) ? null : row.ImageUrl.Trim() };
            variant = new ProductVariant { Name = string.IsNullOrWhiteSpace(row.Variant) ? "Única" : row.Variant.Trim(), Sku = row.Sku.Trim(), Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(), Cost = row.Cost, Price = row.Price, MinimumStock = row.MinimumStock };
            product.Variants.Add(variant);
            db.Products.Add(product);
            if (row.InitialStock != 0)
            {
                db.InventoryBalances.Add(new InventoryBalance { BranchId = request.BranchId, ProductVariantId = variant.Id, Quantity = row.InitialStock, AverageCost = row.Cost });
                db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = variant.Id, Type = InventoryMovementType.InitialStock, Quantity = row.InitialStock, UnitCost = row.Cost, Note = "Importación de catálogo" });
            }
            imported++;
        }
    }
    try
    {
        await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        var sqlException = exception.InnerException as Microsoft.Data.SqlClient.SqlException;
        var detail = sqlException?.Number is 2601 or 2627
            ? "Hay un SKU, código de barras o categoría que ya está asignado a otro registro. Revisa los valores duplicados."
            : "SQL Server rechazó uno de los datos. Revisa longitudes, números y valores duplicados del archivo.";
        return Results.Problem(title: "No se pudo importar el catálogo", detail: detail, statusCode: StatusCodes.Status409Conflict);
    }
    return Results.Ok(new { Imported = imported, Updated = updated });
});

api.MapPost("/products", async (
    CreateProductRequest request,
    ITenantContext tenant,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["El nombre y el SKU son obligatorios."] });
    if (request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0 || request.InitialStock < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amounts"] = ["Costo, precio y existencia no pueden ser negativos."] });
    if (request.InitialStock > 0 && request.BranchId is null)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["branchId"] = ["Selecciona una sucursal para la existencia inicial."] });

    var product = new Product
    {
        Name = request.Name.Trim(),
        CategoryId = request.CategoryId,
        ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
        Variants =
        [
            new ProductVariant
            {
                Name = string.IsNullOrWhiteSpace(request.VariantName) ? "Única" : request.VariantName.Trim(),
                Sku = request.Sku.Trim(),
                Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
                Cost = request.Cost,
                Price = request.Price,
                MinimumStock = request.MinimumStock
            }
        ]
    };

    db.Products.Add(product);

    if (request.InitialStock > 0)
    {
        var variant = product.Variants.Single();
        db.InventoryBalances.Add(new InventoryBalance
        {
            BranchId = request.BranchId!.Value,
            ProductVariantId = variant.Id,
            Quantity = request.InitialStock,
            AverageCost = request.Cost
        });
        db.InventoryMovements.Add(new InventoryMovement
        {
            BranchId = request.BranchId.Value,
            ProductVariantId = variant.Id,
            Type = InventoryMovementType.InitialStock,
            Quantity = request.InitialStock,
            UnitCost = request.Cost,
            Note = "Existencia inicial"
        });
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/products/{product.Id}", new { product.Id, TenantId = tenant.TenantId });
});

api.MapPut("/products/{variantId:guid}", async (Guid variantId, UpdateProductRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sku) || request.Cost < 0 || request.Price < 0 || request.MinimumStock < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["Revisa nombre, SKU, costo, precio y stock mínimo."] });
    var variant = await db.ProductVariants.Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == variantId, cancellationToken);
    if (variant is null) return Results.NotFound();
    var normalizedSku = request.Sku.Trim();
    var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
    if (await db.ProductVariants.AnyAsync(x => x.Id != variantId && x.Sku == normalizedSku, cancellationToken))
        return Results.Conflict(new { title = "Ya existe otro producto con ese SKU." });
    if (barcode is not null && await db.ProductVariants.AnyAsync(x => x.Id != variantId && x.Barcode == barcode, cancellationToken))
        return Results.Conflict(new { title = "Ya existe otro producto con ese código de barras." });
    variant.Product.Name = request.Name.Trim(); variant.Product.CategoryId=request.CategoryId; variant.Product.ImageUrl=string.IsNullOrWhiteSpace(request.ImageUrl)?null:request.ImageUrl.Trim(); variant.Name = string.IsNullOrWhiteSpace(request.VariantName) ? "Única" : request.VariantName.Trim();
    variant.Sku = normalizedSku; variant.Barcode = barcode; variant.Cost = request.Cost; variant.Price = request.Price; variant.MinimumStock = request.MinimumStock; variant.IsActive = request.IsActive; variant.Product.IsActive = request.IsActive;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

inventoryAdmin.MapPost("/adjustment", async (InventoryAdjustmentRequest request, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    if (request.QuantityChange == 0 || string.IsNullOrWhiteSpace(request.Reason))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["adjustment"] = ["Indica una cantidad distinta de cero y el motivo del ajuste."] });
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
    IResult result = Results.NotFound();
    var strategy = db.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == request.ProductVariantId, cancellationToken);
        if (variant is null) return;
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.BranchId == request.BranchId && x.ProductVariantId == request.ProductVariantId, cancellationToken);
        if (balance is null) { balance = new InventoryBalance { BranchId = request.BranchId, ProductVariantId = request.ProductVariantId, AverageCost = variant.Cost }; db.InventoryBalances.Add(balance); }
        if (balance.Quantity + request.QuantityChange < 0)
        {
            result = Results.ValidationProblem(new Dictionary<string, string[]> { ["quantityChange"] = ["El ajuste dejaría el inventario en negativo."] });
            return;
        }
        balance.Quantity += request.QuantityChange;
        db.InventoryMovements.Add(new InventoryMovement { BranchId = request.BranchId, ProductVariantId = request.ProductVariantId, Type = InventoryMovementType.Adjustment, Quantity = request.QuantityChange, UnitCost = balance.AverageCost, Note = request.Reason.Trim(), PerformedByUserId = userId });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        result = Results.Ok(new { balance.Quantity });
    });
    return result;
});

inventoryAdmin.MapPost("/quick-entry", async (
    QuickEntryRequest request,
    VendemeFacilDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.Quantity <= 0 || request.UnitCost < 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["entry"] = ["La cantidad debe ser mayor a cero y el costo no puede ser negativo."] });

    var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == request.ProductVariantId, cancellationToken);
    if (variant is null) return Results.NotFound();

    var balance = await db.InventoryBalances.SingleOrDefaultAsync(
        x => x.BranchId == request.BranchId && x.ProductVariantId == request.ProductVariantId,
        cancellationToken);

    if (balance is null)
    {
        balance = new InventoryBalance
        {
            BranchId = request.BranchId,
            ProductVariantId = request.ProductVariantId,
            Quantity = request.Quantity,
            AverageCost = request.UnitCost
        };
        db.InventoryBalances.Add(balance);
    }
    else
    {
        var priorValue = balance.Quantity * balance.AverageCost;
        var incomingCost = request.UnitCost > 0 ? request.UnitCost : balance.AverageCost;
        balance.Quantity += request.Quantity;
        balance.AverageCost = balance.Quantity == 0 ? 0 : (priorValue + request.Quantity * incomingCost) / balance.Quantity;
    }

    if (request.UnitCost > 0) variant.Cost = balance.AverageCost;
    db.InventoryMovements.Add(new InventoryMovement
    {
        BranchId = request.BranchId,
        ProductVariantId = request.ProductVariantId,
        Type = InventoryMovementType.Entry,
        Quantity = request.Quantity,
        UnitCost = request.UnitCost,
        Note = request.Note
    });

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { balance.Quantity, balance.AverageCost, CostPending = balance.AverageCost == 0 });
});

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

api.MapGet("/dashboard", async (HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var zone = ClientTimeZone.From(context);
    var today = ClientTimeZone.Today(zone);
    var todayUtc = ClientTimeZone.StartOfDayUtc(today, zone);
    var tomorrowUtc = ClientTimeZone.StartOfDayUtc(today.AddDays(1), zone);
    var completedToday = db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= todayUtc && x.SoldAtUtc < tomorrowUtc);
    var salesToday = await completedToday.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0;
    var transactionsToday = await completedToday.CountAsync(cancellationToken);
    var products = await db.ProductVariants.CountAsync(x => x.IsActive, cancellationToken);
    var units = await db.InventoryBalances.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
    var lowStock = await db.ProductVariants.CountAsync(
        x => x.IsActive && db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity) <= x.MinimumStock,
        cancellationToken);
    var weekStart = today.AddDays(-6);
    var weekStartUtc = ClientTimeZone.StartOfDayUtc(weekStart, zone);
    var weeklyRaw = await db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= weekStartUtc && x.SoldAtUtc < tomorrowUtc).Select(x => new { x.SoldAtUtc, x.Total }).ToListAsync(cancellationToken);
    var weeklySales = Enumerable.Range(0, 7).Select(i => { var date = weekStart.AddDays(i); return new { Date = date, Sales = weeklyRaw.Where(x => ClientTimeZone.LocalDate(x.SoldAtUtc, zone) == date).Sum(x => x.Total) }; }).ToList();
    var recentProducts = await db.ProductVariants.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(5).Select(x => new { x.Id, Name = x.Product.Name, Variant = x.Name, x.Sku, x.Price, Stock = db.InventoryBalances.Where(b => b.ProductVariantId == x.Id).Sum(b => b.Quantity), x.MinimumStock }).ToListAsync(cancellationToken);
    var currentUserId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")!);
    var cashOpen = await db.CashSessions.AnyAsync(x => x.OpenedByUserId == currentUserId && x.Status == CashSessionStatus.Open, cancellationToken);
    return Results.Ok(new { SalesToday = salesToday, TransactionsToday = transactionsToday, AverageTicket = transactionsToday == 0 ? 0 : salesToday / transactionsToday, ProductsInStock = products, UnitsInStock = units, LowStockProducts = lowStock, WeeklySales = weeklySales, RecentProducts = recentProducts, CashOpen = cashOpen });
});

var reportsAdmin = api.MapGroup("/reports").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Administrator)));
reportsAdmin.MapPost("/sales/backfill-costs", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var pendingLines = await db.SaleItems
        .Where(x => x.UnitCost == 0 && x.Sale.Status == SaleStatus.Completed)
        .ToListAsync(cancellationToken);
    if (pendingLines.Count == 0) return Results.Ok(new { UpdatedLines = 0, RemainingLines = 0 });

    var variantIds = pendingLines.Select(x => x.ProductVariantId).Distinct().ToList();
    var costs = await db.ProductVariants.AsNoTracking()
        .Where(x => variantIds.Contains(x.Id) && x.Cost > 0)
        .ToDictionaryAsync(x => x.Id, x => x.Cost, cancellationToken);
    var updated = 0;
    foreach (var line in pendingLines)
    {
        if (!costs.TryGetValue(line.ProductVariantId, out var cost)) continue;
        line.UnitCost = cost;
        updated++;
    }
    if (updated > 0) await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { UpdatedLines = updated, RemainingLines = pendingLines.Count - updated });
});
reportsAdmin.MapGet("/sales", async (DateOnly? from, DateOnly? to, HttpContext context, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
{
    var zone = ClientTimeZone.From(context);
    var endDate = to ?? ClientTimeZone.Today(zone);
    var startDate = from ?? endDate.AddDays(-29);
    if (startDate > endDate || endDate.DayNumber - startDate.DayNumber > 365)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Selecciona un periodo válido de hasta 365 días."] });
    var fromUtc = ClientTimeZone.StartOfDayUtc(startDate, zone);
    var toUtc = ClientTimeZone.StartOfDayUtc(endDate.AddDays(1), zone);
    var sales = db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Completed && x.SoldAtUtc >= fromUtc && x.SoldAtUtc < toUtc);
    var gross = await sales.SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0;
    var transactions = await sales.CountAsync(cancellationToken);
    var lines = db.SaleItems.AsNoTracking().Where(x => sales.Select(s => s.Id).Contains(x.SaleId));
    var knownCost = await lines.SumAsync(x => (decimal?)(x.UnitCost * x.Quantity), cancellationToken) ?? 0;
    var pendingCost = await lines.CountAsync(x => x.UnitCost == 0, cancellationToken);
    var dailySource = await sales.Select(x => new { x.SoldAtUtc, x.Total }).ToListAsync(cancellationToken);
    var dailyRaw = dailySource.GroupBy(x => ClientTimeZone.LocalDate(x.SoldAtUtc, zone)).Select(g => new { Date = g.Key, Sales = g.Sum(x => x.Total), Transactions = g.Count() }).OrderBy(x => x.Date).ToList();
    var payments = await db.SalePayments.AsNoTracking().Where(x => sales.Select(s => s.Id).Contains(x.SaleId)).GroupBy(x => x.Method).Select(g => new PaymentBreakdown(g.Key.ToString(), g.Sum(x => x.Amount), g.Select(x => x.SaleId).Distinct().Count())).ToListAsync(cancellationToken);
    var top = await lines.GroupBy(x => new { x.ProductVariantId, x.ProductName, x.VariantName, x.Sku }).Select(g => new { g.Key.ProductVariantId, Product = g.Key.ProductName, Variant = g.Key.VariantName, g.Key.Sku, Quantity = g.Sum(x => x.Quantity), Sales = g.Sum(x => x.LineTotal), Cost = g.Sum(x => x.UnitCost * x.Quantity), CostPending = g.Any(x => x.UnitCost == 0) }).OrderByDescending(x => x.Quantity).Take(10).ToListAsync(cancellationToken);
    var result = new SalesReportSummary(fromUtc, toUtc.AddTicks(-1), gross, knownCost, pendingCost == 0 ? gross - knownCost : null, transactions, transactions == 0 ? 0 : gross / transactions, pendingCost,
        dailyRaw.Select(x => new DailySalesPoint(x.Date, x.Sales, x.Transactions)).ToList(), payments,
        top.Select(x => new TopProduct(x.ProductVariantId, x.Product, x.Variant, x.Sku, x.Quantity, x.Sales, x.CostPending ? null : x.Sales - x.Cost, x.CostPending)).ToList());
    return Results.Ok(result);
});

app.Run();

public partial class Program;
