namespace VendemeFacil.Api.Contracts;

public sealed record BusinessSettingsResponse(string Name, string Slug, string PrimaryColor, string AccentColor, string ButtonColor, string HoverColor, string BackgroundColor, string SurfaceColor, string TextColor, int CornerRadius, int LayawayReminderDaysBefore, bool AllowNegativeStock, string? LogoUrl, string OperationMode, string? Phone, string? Address, string? TicketMessage);
public sealed record UpdateBusinessSettingsRequest(string Name, string PrimaryColor, string AccentColor, string ButtonColor, string HoverColor, string BackgroundColor, string SurfaceColor, string TextColor, int CornerRadius, int LayawayReminderDaysBefore, bool AllowNegativeStock, string? LogoUrl, string? Phone, string? Address, string? TicketMessage);
public sealed record SendDocumentEmailRequest(string Email, string DocumentType, string Reference, string Content);
public sealed record QzSignRequest(string Request);
