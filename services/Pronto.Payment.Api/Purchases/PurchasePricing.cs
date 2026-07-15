using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Purchases;

/// <summary>Mock-rail platform pricing per plan (integer cents).</summary>
public static class PurchasePricing
{
    public const int SharedCents = 49900;
    public const int IsolatedCents = 199900;

    public static int AmountFor(PurchasePlan plan) =>
        plan == PurchasePlan.Isolated ? IsolatedCents : SharedCents;
}
