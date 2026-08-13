using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Contracts;

public sealed record CreateLayawayItemRequest(Guid ProductVariantId, decimal Quantity);
public sealed record CreateLayawayRequest(Guid BranchId, Guid CustomerId, int TermDays, decimal Deposit, PaymentMethod PaymentMethod, string? Notes, IReadOnlyList<CreateLayawayItemRequest> Items, IReadOnlyList<PaymentPartRequest>? Payments = null);
public sealed record AddLayawayPaymentRequest(decimal Amount, PaymentMethod PaymentMethod, string? Note, IReadOnlyList<PaymentPartRequest>? Payments = null);
