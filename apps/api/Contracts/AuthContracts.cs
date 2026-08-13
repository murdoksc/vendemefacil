namespace VendemeFacil.Api.Contracts;

public sealed record RegisterBusinessRequest(string BusinessName, string OwnerName, string Email, string Password);
public sealed record LoginRequest(string BusinessSlug, string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserSession User);
public sealed record UserSession(Guid Id, Guid TenantId, string BusinessName, string BusinessSlug, string DisplayName, string Email, string Role, bool CanViewCosts);
public sealed record CreateUserRequest(string DisplayName, string Email, string Password, string Role, bool CanViewCosts);
public sealed record UpdateUserRequest(string DisplayName, string Role, bool CanViewCosts, bool IsActive);
