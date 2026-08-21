namespace VendemeFacil.Api.Contracts;

public sealed record SaveOnboardingProfileRequest(string BusinessType, string? Phone, string? Address, string? LogoUrl);
public sealed record UpdateOnboardingPreferenceRequest(bool? PrintingConfigured, bool? Dismissed);
public sealed record CreateDemoCatalogRequest(string BusinessType);
