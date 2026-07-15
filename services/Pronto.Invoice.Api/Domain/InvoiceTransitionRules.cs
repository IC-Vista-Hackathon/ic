namespace Pronto.Invoice.Api.Domain;

/// <summary>
/// The single source of truth for invoice status-transition legality, shared by every
/// <see cref="Pronto.Invoice.Api.Repositories.IInvoiceRepository"/> implementation so the
/// in-memory and Cosmos stores can never drift apart.
/// </summary>
public enum TransitionDecision
{
    /// <summary>The transition is legal and should be applied.</summary>
    Apply,

    /// <summary>The invoice already carries this payment in the target status — replay, no write.</summary>
    IdempotentReplay,

    /// <summary>The invoice is already paid.</summary>
    AlreadyPaid,

    /// <summary>The invoice is scheduled against a different payment; only its owner may act on it.</summary>
    ScheduleLocked,

    /// <summary>Any other illegal transition.</summary>
    Invalid,
}

public static class InvoiceTransitionRules
{
    /// <summary>
    /// Decides whether <paramref name="current"/> (owned by <paramref name="currentPaymentId"/>)
    /// may move to <paramref name="target"/> at the behest of <paramref name="paymentId"/>.
    /// Legal transitions: <c>due→paid</c>, <c>due→scheduled</c>, and <c>scheduled→paid</c>, the
    /// last only when the asserting payment owns the schedule.
    /// </summary>
    public static TransitionDecision Decide(
        InvoiceStatus current,
        string? currentPaymentId,
        InvoiceStatus target,
        string paymentId)
    {
        var ownsCurrent = currentPaymentId is not null
            && string.Equals(currentPaymentId, paymentId, StringComparison.Ordinal);

        // The same payment re-asserting the status it already produced is a no-op replay.
        if (current == target && ownsCurrent)
        {
            return TransitionDecision.IdempotentReplay;
        }

        if (current == InvoiceStatus.Paid)
        {
            return TransitionDecision.AlreadyPaid;
        }

        // A scheduled invoice is bound to the payment that scheduled it. A different payment may
        // neither settle it nor re-schedule it — that is the second-payment / double-settle guard.
        // (Only enforced when an originating payment is recorded; a real due→scheduled transition
        // always records one.)
        if (current == InvoiceStatus.Scheduled && currentPaymentId is not null && !ownsCurrent)
        {
            return TransitionDecision.ScheduleLocked;
        }

        var allowed = (current, target) switch
        {
            (InvoiceStatus.Due, InvoiceStatus.Paid) => true,
            (InvoiceStatus.Due, InvoiceStatus.Scheduled) => true,
            (InvoiceStatus.Scheduled, InvoiceStatus.Paid) => true,
            _ => false,
        };

        return allowed ? TransitionDecision.Apply : TransitionDecision.Invalid;
    }

    /// <summary>Maps a non-applying decision to its repository outcome.</summary>
    public static Repositories.InvoiceTransitionOutcome ToOutcome(this TransitionDecision decision) => decision switch
    {
        TransitionDecision.IdempotentReplay => Repositories.InvoiceTransitionOutcome.Updated,
        TransitionDecision.AlreadyPaid => Repositories.InvoiceTransitionOutcome.AlreadyPaid,
        TransitionDecision.ScheduleLocked => Repositories.InvoiceTransitionOutcome.ScheduleLocked,
        TransitionDecision.Invalid => Repositories.InvoiceTransitionOutcome.InvalidTransition,
        TransitionDecision.Apply => Repositories.InvoiceTransitionOutcome.Updated,
        _ => Repositories.InvoiceTransitionOutcome.InvalidTransition,
    };
}
