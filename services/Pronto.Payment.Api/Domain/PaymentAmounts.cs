namespace Pronto.Payment.Api.Domain;

/// <summary>
/// Pure amount arithmetic for partial payments and installment plans. Kept deterministic and
/// side-effect free so the money math has focused unit coverage and the controller stays a thin
/// validator. The server always feeds these functions the balance it looked up itself — never a
/// client-supplied amount — so the results are authoritative.
/// </summary>
public static class PaymentAmounts
{
    /// <summary>
    /// Split <paramref name="outstandingCents"/> into <paramref name="count"/> installment amounts
    /// that sum <em>exactly</em> to the balance. The remainder cents are spread one-per-installment
    /// across the earliest installments, so the schedule never over- or under-charges.
    /// </summary>
    public static IReadOnlyList<int> SplitIntoInstallments(int outstandingCents, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outstandingCents);
        if (outstandingCents < count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), "each installment must be at least one cent");
        }

        var baseAmount = outstandingCents / count;
        var remainder = outstandingCents % count;

        var amounts = new int[count];
        for (var index = 0; index < count; index++)
        {
            amounts[index] = baseAmount + (index < remainder ? 1 : 0);
        }

        return amounts;
    }
}
