namespace VendemeFacil.Api.Contracts;

public sealed record SalesReportSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal GrossSales,
    decimal KnownCost,
    decimal? EstimatedProfit,
    int Transactions,
    decimal AverageTicket,
    int ItemsWithPendingCost,
    IReadOnlyList<DailySalesPoint> DailySales,
    IReadOnlyList<PaymentBreakdown> Payments,
    IReadOnlyList<TopProduct> TopProducts);

public sealed record DailySalesPoint(DateOnly Date, decimal Sales, int Transactions);
public sealed record PaymentBreakdown(string Method, decimal Total, int Transactions);
public sealed record TopProduct(Guid ProductVariantId, string Product, string Variant, string Sku, decimal Quantity, decimal Sales, decimal? EstimatedProfit, bool CostPending);
