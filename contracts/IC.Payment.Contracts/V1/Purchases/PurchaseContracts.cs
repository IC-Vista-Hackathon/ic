namespace IC.Payment.Contracts.V1.Purchases;

public sealed record CreatePurchaseRequest(
    string BillerId,
    PurchasePlan Plan);

public sealed record PurchaseResponse(
    string PurchaseId,
    string BillerId,
    PurchasePlan Plan,
    int AmountCents,
    PurchaseStatus Status);

public enum PurchasePlan
{
    Shared,
    Isolated
}

public enum PurchaseStatus
{
    Pending,
    Paid
}
