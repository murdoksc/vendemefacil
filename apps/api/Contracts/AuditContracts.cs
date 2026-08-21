namespace VendemeFacil.Api.Contracts;

public sealed record LocalAuditLogItem(
    Guid Id,
    string Action,
    string Description,
    string? DetailsJson,
    Guid PerformedByUserId,
    Guid BranchId,
    DateTimeOffset ClientCreatedAtUtc
);

public sealed record AuditSyncRequest(IReadOnlyList<LocalAuditLogItem> Logs);
