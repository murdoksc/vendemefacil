namespace VendemeFacil.Api.Contracts;

public sealed record RegisterBusinessRequest(string BusinessName, string OwnerName, string Email, string Password, string? PlanCode, string? AcquisitionSource, string? AcquisitionCampaign, string? FacebookClickId, string? GoogleClickId);
public sealed record LoginRequest(string BusinessSlug, string Email, string Password);
public sealed record PlatformLoginRequest(string Email, string Password);
public sealed record UpdateTenantSubscriptionRequest(string PlanCode, string Status, DateTimeOffset? TrialEndsAtUtc, DateTimeOffset? CurrentPeriodEndsAtUtc, string? Notes, bool IsActive);
public sealed record UpdateLeadStatusRequest(string Status);
public sealed record RecordSubscriptionPaymentRequest(decimal Amount, string Method, string? Reference, DateTimeOffset PaidAtUtc, DateTimeOffset PeriodStartsAtUtc, DateTimeOffset PeriodEndsAtUtc, string? Notes);
public sealed record RequestPlanChangeRequest(string PlanCode);
public sealed record ForgotPasswordRequest(string BusinessSlug, string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
public sealed record CreateProspectLeadRequest(
    string ContactName,
    string BusinessName,
    string Phone,
    string? Email,
    string? City,
    string? BusinessType,
    string? PreferredContactTime,
    string? Notes,
    string? Website);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserSession User);
public sealed record UserSession(Guid Id, Guid TenantId, string BusinessName, string BusinessSlug, string DisplayName, string Email, string Role, bool CanViewCosts);
public sealed record CreateUserRequest(string DisplayName, string Email, string Password, string Role, bool CanViewCosts);
public sealed record UpdateUserRequest(string DisplayName, string Role, bool CanViewCosts, bool IsActive);
