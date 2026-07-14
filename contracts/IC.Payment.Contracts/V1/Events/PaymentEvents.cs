using IC.Payment.Contracts.V1.Purchases;

namespace IC.Payment.Contracts.V1.Events;

public sealed record PurchaseCompleted(
    string EventId,
    string BillerId,
    string PurchaseId,
    PurchasePlan Plan,
    DateTimeOffset OccurredAt);
