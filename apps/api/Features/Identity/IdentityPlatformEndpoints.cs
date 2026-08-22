using System.Security.Claims;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;
using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Features.Identity;

public static class IdentityPlatformEndpoints
{
    public static WebApplication MapIdentityPlatformEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        
        auth.MapPost("/platform-login", (PlatformLoginRequest request, IConfiguration configuration, JwtTokenService tokens) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var configuredEmail = configuration["PlatformAdmin:Email"]?.Trim().ToLowerInvariant();
            var configuredHash = configuration["PlatformAdmin:PasswordSha256"]?.Trim().ToUpperInvariant();
            var suppliedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Password)));
            if (string.IsNullOrWhiteSpace(configuredEmail) || string.IsNullOrWhiteSpace(configuredHash)
                || email != configuredEmail || configuredHash.Length != suppliedHash.Length
                || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(configuredHash), Encoding.ASCII.GetBytes(suppliedHash)))
                return Results.Unauthorized();
            return Results.Ok(tokens.CreatePlatformAdmin(email));
        }).RequireRateLimiting("platform-login");
        
        var platform = app.MapGroup("/api/platform").RequireAuthorization(policy => policy.RequireRole("PlatformAdmin"));
        platform.MapGet("/dashboard", async (VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var tenants = await db.Tenants.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Select(x => new
            {
                x.Id, x.Name, x.Slug, x.PlanCode, x.SubscriptionStatus, x.TrialEndsAtUtc, x.CurrentPeriodEndsAtUtc,
                x.SubscriptionNotes, x.IsActive, x.CreatedAtUtc, x.BusinessType,
                Users = db.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id && u.IsActive),
                Branches = db.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.Id && b.IsActive),
                Products = db.ProductVariants.IgnoreQueryFilters().Count(p => p.TenantId == x.Id && p.IsActive),
                LastSaleAtUtc = db.Sales.IgnoreQueryFilters().Where(s => s.TenantId == x.Id).Max(s => (DateTimeOffset?)s.SoldAtUtc),
                ActivationSteps = (!string.IsNullOrEmpty(x.BusinessType) && !string.IsNullOrEmpty(x.Phone) ? 1 : 0)
                    + (db.ProductVariants.IgnoreQueryFilters().Any(p => p.TenantId == x.Id && p.IsActive) ? 1 : 0)
                    + (db.InventoryBalances.IgnoreQueryFilters().Any(b => b.TenantId == x.Id && b.Quantity > 0) ? 1 : 0)
                    + (db.CashSessions.IgnoreQueryFilters().Any(c => c.TenantId == x.Id) ? 1 : 0)
                    + (db.Sales.IgnoreQueryFilters().Any(s => s.TenantId == x.Id && s.Status == SaleStatus.Completed) ? 1 : 0)
                    + (x.PrintingConfigured ? 1 : 0)
                    + (db.Users.IgnoreQueryFilters().Count(u => u.TenantId == x.Id && u.IsActive) > 1 ? 1 : 0)
            }).ToListAsync(cancellationToken);
            var leads = await db.ProspectLeads.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
            var changeRequests = await db.SubscriptionEvents.AsNoTracking().Where(x => x.Type == "PlanChangeRequested").OrderByDescending(x => x.CreatedAtUtc).Take(50)
                .Join(db.Tenants.AsNoTracking(), x => x.TenantId, x => x.Id, (history, tenant) => new { history.Id, history.TenantId, BusinessName = tenant.Name, history.Description, history.CreatedAtUtc }).ToListAsync(cancellationToken);
            return Results.Ok(new
            {
                Summary = new { Total = tenants.Count, Active = tenants.Count(x => x.IsActive), Trials = tenants.Count(x => x.SubscriptionStatus == "Trial" && x.TrialEndsAtUtc >= now), PastDue = tenants.Count(x => x.SubscriptionStatus == "PastDue"), NewLeads = leads.Count(x => x.Status == "New") },
                Tenants = tenants, Leads = leads,
                FollowUps = new
                {
                    TrialsExpiring = tenants.Where(x => x.SubscriptionStatus == "Trial" && x.TrialEndsAtUtc >= now && x.TrialEndsAtUtc <= now.AddDays(7)).Select(x => new { x.Id, x.Name, x.TrialEndsAtUtc }),
                    WithoutFirstSale = tenants.Where(x => x.LastSaleAtUtc == null && x.CreatedAtUtc <= now.AddDays(-2)).Select(x => new { x.Id, x.Name, x.CreatedAtUtc, x.ActivationSteps }),
                    PlanChangeRequests = changeRequests
                }
            });
        });
        platform.MapPut("/tenants/{tenantId:guid}/subscription", async (Guid tenantId, UpdateTenantSubscriptionRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var plans = new[] { "esencial", "negocio", "pro" };
            var statuses = new[] { "Trial", "Active", "PastDue", "Suspended", "Cancelled" };
            if (!plans.Contains(request.PlanCode) || !statuses.Contains(request.Status)) return Results.BadRequest(new { title = "Plan o estado inválido." });
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
            if (tenant is null) return Results.NotFound();
            var prior = $"{tenant.PlanCode}/{tenant.SubscriptionStatus}";
            tenant.PlanCode = request.PlanCode; tenant.SubscriptionStatus = request.Status; tenant.TrialEndsAtUtc = request.TrialEndsAtUtc ?? tenant.TrialEndsAtUtc;
            tenant.CurrentPeriodEndsAtUtc = request.CurrentPeriodEndsAtUtc; tenant.SubscriptionNotes = request.Notes?.Trim();
            tenant.IsActive = request.Status is "Suspended" or "Cancelled" ? false : request.IsActive;
            db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenant.Id, Type = "SubscriptionUpdated", Description = $"{prior}  {tenant.PlanCode}/{tenant.SubscriptionStatus}. Acceso: {(tenant.IsActive ? "activo" : "bloqueado")}.", PerformedBy = "PlatformAdmin" });
            await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        platform.MapPost("/tenants/{tenantId:guid}/subscription/adjust", async (Guid tenantId, AdjustTenantSubscriptionRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
            if (tenant is null) return Results.NotFound();
            var action = request.Action.Trim().ToLowerInvariant();
            string description;
            if (action == "add-days")
            {
                if (request.Days is null or < 1 or > 3650) return Results.BadRequest(new { title = "Indica entre 1 y 3650 dias." });
                var isTrial = tenant.SubscriptionStatus == "Trial";
                var currentEnd = isTrial ? tenant.TrialEndsAtUtc : tenant.CurrentPeriodEndsAtUtc ?? DateTimeOffset.UtcNow;
                var newEnd = (currentEnd > DateTimeOffset.UtcNow ? currentEnd : DateTimeOffset.UtcNow).AddDays(request.Days.Value);
                if (isTrial) tenant.TrialEndsAtUtc = newEnd; else tenant.CurrentPeriodEndsAtUtc = newEnd;
                description = $"Se agregaron {request.Days} dias. Nueva vigencia: {newEnd:yyyy-MM-dd}.";
            }
            else if (action == "suspend")
            {
                tenant.SubscriptionStatus = "Suspended"; tenant.IsActive = false;
                description = "Membresia suspendida manualmente y acceso bloqueado.";
            }
            else if (action == "activate")
            {
                tenant.SubscriptionStatus = tenant.CurrentPeriodEndsAtUtc.HasValue ? "Active" : "Trial"; tenant.IsActive = true;
                description = $"Membresia activada manualmente como {tenant.SubscriptionStatus}.";
            }
            else return Results.BadRequest(new { title = "Accion no valida." });
            if (!string.IsNullOrWhiteSpace(request.Notes)) description += $" Nota: {request.Notes.Trim()}";
            db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenant.Id, Type = "ManualAdjustment", Description = description, PerformedBy = "PlatformAdmin" });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { tenant.SubscriptionStatus, tenant.IsActive, tenant.TrialEndsAtUtc, tenant.CurrentPeriodEndsAtUtc });
        });
        platform.MapGet("/tenants/{tenantId:guid}", async (Guid tenantId, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
            if (tenant is null) return Results.NotFound();
            var owner = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.Role == UserRole.Owner).Select(x => new { x.DisplayName, x.Email }).FirstOrDefaultAsync(cancellationToken);
            var payments = await db.SubscriptionPayments.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.PaidAtUtc).ToListAsync(cancellationToken);
            var history = await db.SubscriptionEvents.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
            return Results.Ok(new { Tenant = tenant, Owner = owner, Payments = payments, History = history });
        });
        platform.MapPost("/tenants/{tenantId:guid}/payments", async (Guid tenantId, RecordSubscriptionPaymentRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Amount <= 0 || request.PeriodEndsAtUtc <= request.PeriodStartsAtUtc) return Results.BadRequest(new { title = "Monto o periodo inválido." });
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
            if (tenant is null) return Results.NotFound();
            var payment = new SubscriptionPayment { TenantId = tenantId, Amount = request.Amount, Method = request.Method.Trim(), Reference = request.Reference?.Trim(), PaidAtUtc = request.PaidAtUtc, PeriodStartsAtUtc = request.PeriodStartsAtUtc, PeriodEndsAtUtc = request.PeriodEndsAtUtc, Notes = request.Notes?.Trim() };
            db.SubscriptionPayments.Add(payment); tenant.SubscriptionStatus = "Active"; tenant.CurrentPeriodEndsAtUtc = request.PeriodEndsAtUtc; tenant.IsActive = true;
            db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenantId, Type = "PaymentRecorded", Description = $"Pago de {request.Amount:C2} registrado para el periodo {request.PeriodStartsAtUtc:yyyy-MM-dd} a {request.PeriodEndsAtUtc:yyyy-MM-dd}.", PerformedBy = "PlatformAdmin" });
            await db.SaveChangesAsync(cancellationToken); return Results.Created($"/api/platform/tenants/{tenantId}/payments/{payment.Id}", new { payment.Id });
        });
        platform.MapPut("/leads/{leadId:guid}/status", async (Guid leadId, UpdateLeadStatusRequest request, VendemeFacilDbContext db, CancellationToken cancellationToken) =>
        {
            var statuses = new[] { "New", "Contacted", "DemoScheduled", "Trial", "Converted", "NotInterested" };
            if (!statuses.Contains(request.Status)) return Results.BadRequest(new { title = "Estado inválido." });
            var lead = await db.ProspectLeads.SingleOrDefaultAsync(x => x.Id == leadId, cancellationToken);
            if (lead is null) return Results.NotFound(); lead.Status = request.Status; await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });
        
        app.MapPost("/api/public/leads", async (
            CreateProspectLeadRequest request,
            OutboundEmailQueue emailQueue,
            IConfiguration configuration,
            VendemeFacilDbContext db,
            CancellationToken cancellationToken) =>
        {
            // Campo trampa: los visitantes reales nunca lo llenan.
            if (!string.IsNullOrWhiteSpace(request.Website))
                return Results.Accepted(value: new { message = "Gracias. Recibimos tus datos." });
        
            var errors = new Dictionary<string, string[]>();
            var contactName = (request.ContactName ?? string.Empty).Trim();
            var businessName = (request.BusinessName ?? string.Empty).Trim();
            var phone = (request.Phone ?? string.Empty).Trim();
            var email = request.Email?.Trim().ToLowerInvariant();
            if (contactName.Length is < 2 or > 120) errors["contactName"] = ["Escribe tu nombre."];
            if (businessName.Length is < 2 or > 160) errors["businessName"] = ["Escribe el nombre de tu negocio."];
            if (phone.Length is < 7 or > 30) errors["phone"] = ["Escribe un telefono valido."];
            if (!string.IsNullOrWhiteSpace(email) && !System.Net.Mail.MailAddress.TryCreate(email, out _))
                errors["email"] = ["Escribe un correo valido."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
        
            var lead = new ProspectLead
            {
                ContactName = contactName,
                BusinessName = businessName,
                Phone = phone,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                City = request.City?.Trim(),
                BusinessType = request.BusinessType?.Trim(),
                PreferredContactTime = request.PreferredContactTime?.Trim(),
                Notes = request.Notes?.Trim()
            };
            db.ProspectLeads.Add(lead);
            await db.SaveChangesAsync(cancellationToken);
            var notificationAddress = configuration["Email:LeadNotificationAddress"]?.Trim();
            if (!string.IsNullOrWhiteSpace(notificationAddress)
                && System.Net.Mail.MailAddress.TryCreate(notificationAddress, out _))
                emailQueue.TryQueue(OutboundEmailFactory.ProspectLead(notificationAddress, lead));
            return Results.Created("/api/public/leads", new { message = "Gracias. Te contactaremos muy pronto." });
        }).RequireRateLimiting("public-leads");
        
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
        
            var selectedPlan = "esencial";
            var tenant = new Tenant { Name = request.BusinessName.Trim(), Slug = slug, PlanCode = selectedPlan, SubscriptionStatus = "Trial", TrialEndsAtUtc = DateTimeOffset.UtcNow.AddDays(30), AcquisitionSource = request.AcquisitionSource?.Trim(), AcquisitionCampaign = request.AcquisitionCampaign?.Trim(), FacebookClickId = request.FacebookClickId?.Trim(), GoogleClickId = request.GoogleClickId?.Trim() };
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
            db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenant.Id, Type = "TrialStarted", Description = $"Prueba de 30 días iniciada con el plan {selectedPlan}.", PerformedBy = owner.Email });
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
        
        return app;
    }
}
