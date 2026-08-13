using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Contracts;

public sealed record OpenCashSessionRequest(Guid BranchId, decimal OpeningAmount);
public sealed record CreateSaleItemRequest(Guid ProductVariantId, decimal Quantity);
public sealed record PaymentPartRequest(PaymentMethod Method, decimal Amount, decimal ReceivedAmount = 0);
public sealed record CreateSaleRequest(Guid BranchId, Guid CashSessionId, IReadOnlyList<CreateSaleItemRequest> Items, PaymentMethod PaymentMethod, decimal ReceivedAmount, Guid? CustomerId = null, decimal Discount = 0, IReadOnlyList<PaymentPartRequest>? Payments = null);
public sealed record CloseCashSessionRequest(decimal CountedAmount);
public sealed record CancelSaleRequest(string? Reason);
public sealed record SaveCustomerRequest(string Name, string? Phone, string? Email, string? Notes, bool IsActive = true);
public sealed record ReturnSaleItemRequest(Guid SaleItemId, decimal Quantity);
public sealed record ReturnSaleRequest(string? Reason, IReadOnlyList<ReturnSaleItemRequest> Items);
