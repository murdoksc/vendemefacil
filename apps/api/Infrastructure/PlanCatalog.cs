namespace VendemeFacil.Api.Infrastructure;

public sealed record PlanCapabilities(string Code, string Name, decimal MonthlyPrice, int MaxUsers, int MaxBranches, bool EmailAndWhatsApp, bool SilentPrinting, bool FullReports, bool CustomBranding, bool ConsolidatedReports = false, bool DataExport = false, bool PrioritySupport = false);

public static class PlanCatalog
{
    public static readonly PlanCapabilities Essential = new("esencial", "Esencial", 199, 1, 1, false, false, false, false);
    public static readonly PlanCapabilities Business = new("negocio", "Negocio", 499, 5, 2, true, true, true, false);
    public static readonly PlanCapabilities Pro = new("pro", "Pro", 799, 15, 5, true, true, true, true, true, true, true);
    public static PlanCapabilities Get(string? code) => code?.ToLowerInvariant() switch { "negocio" => Business, "pro" => Pro, _ => Essential };
}
