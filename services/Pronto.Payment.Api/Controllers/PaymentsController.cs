using System.Security.Cryptography;
using System.Diagnostics;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Fees;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Controllers;

[ApiController]
[Route("payments")]
[Authorize]
public sealed partial class PaymentsController : ControllerBase
{
    private const string ConfirmationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MaxIdempotencyKeyLength = 200;

    private readonly IPaymentStore store;
    private readonly IInvoiceClient invoices;
    private readonly IBillerConfigClient configs;
    private readonly IPayerAccountValidator payerAccounts;
    private readonly PaymentWorkflow workflow;
    private readonly TimeProvider clock;
    private readonly PaymentProcessingOptions options;
    private readonly ILogger<PaymentsController> logger;

    public PaymentsController(
        IPaymentStore store,
        IInvoiceClient invoices,
        IBillerConfigClient configs,
        IPayerAccountValidator payerAccounts,
        PaymentWorkflow workflow,
        TimeProvider clock,
        IOptions<PaymentProcessingOptions> options,
        ILogger<PaymentsController> logger)
    {
        this.store = store;
        this.invoices = invoices;
        this.configs = configs;
        this.payerAccounts = payerAccounts;
        this.workflow = workflow;
        this.clock = clock;
        this.options = options.Value;
        this.logger = logger;
    }

    [HttpPost]
    [Authorize(Policy = ServiceAuthorization.PaymentsWrite)]
    public async Task<IActionResult> Create(
        CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyHeader,
        CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, request.BillerId);

        var config = await configs.GetAsync(request.BillerId, cancellationToken).ConfigureAwait(false);

        if (!config.PaymentMethods.Contains(request.Method))
        {
            throw ServiceException.BadRequest(
                "method_not_enabled", $"payment method '{request.Method}' is not enabled for this biller");
        }

        ValidateScheduleDate(request.ScheduledFor);
        await payerAccounts.ValidateAsync(request.BillerId, request.PayerAccountId, cancellationToken)
            .ConfigureAwait(false);

        var idempotencyKey = RequireIdempotencyKey(idempotencyHeader, request.IdempotencyKey);

        // An installment plan is a structurally different journey (a schedule of scheduled partials),
        // so it has its own enrollment path; a one-time payment (full or partial) stays here.
        if (request.InstallmentCount is not null)
        {
            return await EnrollInstallmentPlanAsync(request, config, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var paymentId = PaymentRecord.DeriveId(request.BillerId, idempotencyKey);
        var fingerprint = PaymentRecord.Fingerprint(
            request.InvoiceId, request.Method, request.PayerAccountId, request.ScheduledFor,
            request.AmountCents ?? 0);

        // Fast path: a retried request with a known key replays the original outcome (finishing a
        // still-pending record if the first attempt crashed mid-workflow) without re-reading state.
        var existing = await store.FindAsync(request.BillerId, paymentId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureSameRequest(existing, fingerprint);
            var replayed = await workflow.DriveInitialAsync(existing, cancellationToken).ConfigureAwait(false);
            return BuildResult(replayed, created: false);
        }

        var invoice = await invoices.GetAsync(request.BillerId, request.InvoiceId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw ServiceException.NotFound("invoice_not_found", $"invoice {request.InvoiceId} not found");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw ServiceException.Conflict("already_paid", $"invoice {request.InvoiceId} is already paid");
        }

        // A second payment cannot target an invoice with an active scheduled payment; the Invoice
        // Service's scheduled→paid binding is the authority, this is the fast, clear rejection.
        if (invoice.Status == InvoiceStatus.Scheduled)
        {
            throw ServiceException.Conflict(
                "invoice_scheduled", $"invoice {request.InvoiceId} already has an active scheduled payment");
        }

        // The server is authoritative on the balance: it sums the biller's own committed payments
        // (never a client field) and derives the charge from the invoice it looked up.
        var outstanding = invoice.AmountCents
            - await CommittedCentsAsync(request.BillerId, request.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (outstanding <= 0)
        {
            throw ServiceException.Conflict("no_balance_due", $"invoice {request.InvoiceId} has no balance due");
        }

        var amountToCharge = ResolveChargeAmount(request, config, outstanding);
        var (feeCents, totalCents) = FeeCalculator.Calculate(config, request.Method, amountToCharge);
        var now = clock.GetUtcNow();

        // Payment-first ordering: persist a durable pending record BEFORE the invoice transition,
        // so a crash between the two never leaves a paid/scheduled invoice with no payment.
        var pending = new PaymentRecord
        {
            PaymentId = paymentId,
            BillerId = request.BillerId,
            InvoiceId = request.InvoiceId,
            PayerAccountId = request.PayerAccountId,
            Method = request.Method,
            AmountCents = amountToCharge,
            FeeCents = feeCents,
            TotalCents = totalCents,
            Confirmation = MintConfirmation(),
            ScheduledFor = request.ScheduledFor,
            ReceiptMessage = config.ReceiptMessage,
            Lifecycle = PaymentLifecycle.Pending,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var begin = await store.BeginAsync(pending, cancellationToken).ConfigureAwait(false);
        if (!begin.Created)
        {
            // Lost the insert race to a concurrent request for the same key: it must be the same
            // request, and we drive/return its record rather than creating a duplicate payment.
            EnsureSameRequest(begin.Record, fingerprint);
        }

        var finalized = await workflow.DriveInitialAsync(begin.Record, cancellationToken).ConfigureAwait(false);
        LogPaymentCreated(logger, finalized.PaymentId, finalized.BillerId, finalized.InvoiceId, finalized.WireStatus, finalized.TotalCents, Activity.Current?.TraceId.ToString());

        return BuildResult(finalized, created: begin.Created);
    }

    /// <summary>
    /// Resolve the amount to charge for a one-time payment. Omitting <c>amount_cents</c> pays the
    /// full outstanding balance (the default path). A requested amount is a partial payment and is
    /// validated against the biller's policy and the server-computed balance: partials must be
    /// enabled, above the minimum, and no greater than the balance — never trust the client.
    /// </summary>
    private static int ResolveChargeAmount(CreatePaymentRequest request, BillerPaymentConfig config, int outstanding)
    {
        if (request.AmountCents is not { } requested)
        {
            return outstanding;
        }

        if (requested <= 0)
        {
            throw ServiceException.BadRequest("invalid_amount", "amount_cents must be a positive number of cents.");
        }

        if (requested > outstanding)
        {
            throw ServiceException.BadRequest(
                "amount_exceeds_balance",
                $"amount_cents {requested} exceeds the outstanding balance of {outstanding}.");
        }

        // Paying the exact remaining balance is always allowed; the partial-payment policy only
        // gates leaving a balance behind.
        if (requested < outstanding)
        {
            if (!config.PartialPaymentsAllowed)
            {
                throw ServiceException.BadRequest(
                    "partial_payments_not_allowed", "this biller does not accept partial payments.");
            }

            if (requested <= config.MinPartialPaymentCents)
            {
                throw ServiceException.BadRequest(
                    "amount_below_minimum",
                    $"a partial payment must be more than the minimum of {config.MinPartialPaymentCents} cents.");
            }

            if (request.ScheduledFor is not null)
            {
                throw ServiceException.BadRequest(
                    "partial_payment_not_schedulable", "a partial payment cannot be scheduled for a future date.");
            }
        }

        return requested;
    }

    /// <summary>
    /// Enroll an installment plan: validate the plan against the biller's policy, split the
    /// server-computed balance into a schedule of scheduled partial payments (summing exactly to
    /// the balance), and persist them idempotently. The invoice is not reserved at enrollment; each
    /// installment settles on its date and only the one that clears the balance marks it paid.
    /// </summary>
    private async Task<IActionResult> EnrollInstallmentPlanAsync(
        CreatePaymentRequest request, BillerPaymentConfig config, string idempotencyKey, CancellationToken cancellationToken)
    {
        var count = request.InstallmentCount!.Value;

        if (!config.InstallmentsAllowed)
        {
            throw ServiceException.BadRequest(
                "installments_not_allowed", "this biller does not offer installment plans.");
        }

        if (count < 2)
        {
            throw ServiceException.BadRequest(
                "invalid_installment_count", "an installment plan needs at least 2 installments.");
        }

        if (config.MaxInstallments > 0 && count > config.MaxInstallments)
        {
            throw ServiceException.BadRequest(
                "installment_count_exceeds_max",
                $"this biller allows at most {config.MaxInstallments} installments.");
        }

        if (request.AmountCents is not null)
        {
            throw ServiceException.BadRequest(
                "amount_with_installments_unsupported",
                "an installment plan covers the full balance; omit amount_cents.");
        }

        if (request.ScheduledFor is not null)
        {
            throw ServiceException.BadRequest(
                "scheduled_with_installments_unsupported", "an installment plan sets its own dates.");
        }

        var planId = PaymentRecord.DeriveId(request.BillerId, $"{idempotencyKey}\u001fplan");

        // Fast path: replay an already-enrolled plan without recomputing the balance.
        var firstInstallmentId = PaymentRecord.DeriveId(request.BillerId, InstallmentKey(idempotencyKey, 0));
        var existingFirst = await store.FindAsync(request.BillerId, firstInstallmentId, cancellationToken)
            .ConfigureAwait(false);
        if (existingFirst is not null)
        {
            EnsureSameRequest(existingFirst, InstallmentFingerprint(request, existingFirst.ScheduledFor, existingFirst.AmountCents, 0));
            if (existingFirst.InstallmentCount != count)
            {
                throw ServiceException.Conflict(
                    "idempotency_key_conflict",
                    "the idempotency key was already used for a different installment plan.");
            }

            var replayed = await GatherPlanAsync(request.BillerId, request.InvoiceId, planId, cancellationToken)
                .ConfigureAwait(false);
            return BuildPlanResult(planId, request, count, replayed, created: false);
        }

        var invoice = await invoices.GetAsync(request.BillerId, request.InvoiceId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw ServiceException.NotFound("invoice_not_found", $"invoice {request.InvoiceId} not found");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw ServiceException.Conflict("already_paid", $"invoice {request.InvoiceId} is already paid");
        }

        if (invoice.Status == InvoiceStatus.Scheduled)
        {
            throw ServiceException.Conflict(
                "invoice_scheduled", $"invoice {request.InvoiceId} already has an active scheduled payment");
        }

        var outstanding = invoice.AmountCents
            - await CommittedCentsAsync(request.BillerId, request.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (outstanding <= 0)
        {
            throw ServiceException.Conflict("no_balance_due", $"invoice {request.InvoiceId} has no balance due");
        }

        if (outstanding < count)
        {
            throw ServiceException.BadRequest(
                "installments_exceed_balance",
                $"the balance of {outstanding} cents is too small to split into {count} installments.");
        }

        var amounts = PaymentAmounts.SplitIntoInstallments(outstanding, count);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var maxDate = today.AddDays(options.MaxScheduleDays);
        var now = clock.GetUtcNow();

        var installments = new List<PaymentRecord>(count);
        for (var sequence = 0; sequence < count; sequence++)
        {
            var dueDate = today.AddMonths(sequence);
            if (dueDate > maxDate)
            {
                throw ServiceException.BadRequest(
                    "installment_schedule_too_long",
                    $"installment {sequence + 1} would fall beyond the {options.MaxScheduleDays}-day scheduling window.");
            }

            var (feeCents, totalCents) = FeeCalculator.Calculate(config, request.Method, amounts[sequence]);
            var installmentKey = InstallmentKey(idempotencyKey, sequence);
            var record = new PaymentRecord
            {
                PaymentId = PaymentRecord.DeriveId(request.BillerId, installmentKey),
                BillerId = request.BillerId,
                InvoiceId = request.InvoiceId,
                PayerAccountId = request.PayerAccountId,
                Method = request.Method,
                AmountCents = amounts[sequence],
                FeeCents = feeCents,
                TotalCents = totalCents,
                Confirmation = MintConfirmation(),
                ScheduledFor = dueDate,
                InstallmentPlanId = planId,
                InstallmentSequence = sequence,
                InstallmentCount = count,
                ReceiptMessage = config.ReceiptMessage,
                // Persisted directly as scheduled: an installment reserves no invoice state at
                // enrollment, so there is no invoice transition to order a pending record before.
                Lifecycle = PaymentLifecycle.Scheduled,
                IdempotencyKey = installmentKey,
                RequestFingerprint = InstallmentFingerprint(request, dueDate, amounts[sequence], sequence),
                CreatedAt = now,
                UpdatedAt = now,
            };

            var begin = await store.BeginAsync(record, cancellationToken).ConfigureAwait(false);
            installments.Add(begin.Record);
        }

        LogPaymentCreated(logger, planId, request.BillerId, request.InvoiceId, PaymentStatus.Scheduled, outstanding, Activity.Current?.TraceId.ToString());
        return BuildPlanResult(planId, request, count, installments, created: true);
    }

    private static string InstallmentKey(string idempotencyKey, int sequence)
        => $"{idempotencyKey}\u001finst{sequence}";

    private static string InstallmentFingerprint(CreatePaymentRequest request, DateOnly? dueDate, int amountCents, int sequence)
        => PaymentRecord.Fingerprint(
            request.InvoiceId, request.Method, request.PayerAccountId, dueDate, amountCents, sequence);

    private async Task<int> CommittedCentsAsync(string billerId, string invoiceId, CancellationToken cancellationToken)
    {
        // Committed = every non-failed payment recorded against the invoice (succeeded + scheduled).
        var recorded = await store.ListAsync(billerId, payerAccountId: null, invoiceId: invoiceId, cancellationToken)
            .ConfigureAwait(false);
        return recorded
            .Where(payment => payment.Lifecycle != PaymentLifecycle.Failed)
            .Sum(payment => payment.AmountCents);
    }

    private async Task<IReadOnlyList<PaymentRecord>> GatherPlanAsync(
        string billerId, string invoiceId, string planId, CancellationToken cancellationToken)
    {
        var recorded = await store.ListAsync(billerId, payerAccountId: null, invoiceId: invoiceId, cancellationToken)
            .ConfigureAwait(false);
        return recorded
            .Where(payment => string.Equals(payment.InstallmentPlanId, planId, StringComparison.Ordinal))
            .OrderBy(payment => payment.InstallmentSequence)
            .ToArray();
    }

    private IActionResult BuildPlanResult(
        string planId, CreatePaymentRequest request, int count, IReadOnlyList<PaymentRecord> installments, bool created)
    {
        var response = new InstallmentPlanResponse(
            InstallmentPlanId: planId,
            BillerId: request.BillerId,
            InvoiceId: request.InvoiceId,
            InstallmentCount: count,
            TotalAmountCents: installments.Sum(record => record.AmountCents),
            Installments: installments.Select(record => record.ToResponse()).ToArray());

        return created
            ? Created($"/payments?biller_id={response.BillerId}&invoice_id={response.InvoiceId}", response)
            : Ok(response);
    }

    private static string RequireIdempotencyKey(string? idempotencyHeader, string? bodyKey)
    {
        // The header wins over the body field; either provides durable client idempotency.
        var idempotencyKey = Normalize(idempotencyHeader) ?? Normalize(bodyKey);
        if (idempotencyKey is null)
        {
            throw ServiceException.BadRequest(
                "idempotency_key_required",
                "Idempotency-Key header or idempotency_key body field is required.");
        }

        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw ServiceException.BadRequest(
                "idempotency_key_too_long",
                $"Idempotency keys must be at most {MaxIdempotencyKeyLength} characters.");
        }

        return idempotencyKey;
    }

    private IActionResult BuildResult(PaymentRecord record, bool created)
    {
        if (record.Lifecycle == PaymentLifecycle.Failed)
        {
            // Replaying a request whose invoice transition was refused; surface the original 409.
            throw ServiceException.Conflict(
                record.FailureReason ?? "payment_failed",
                $"payment {record.PaymentId} could not be completed: {record.FailureReason ?? "invoice refused the transition"}");
        }

        var response = record.ToResponse();
        return created
            ? Created($"/payments/{response.PaymentId}?biller_id={response.BillerId}", response)
            : Ok(response);
    }

    private void ValidateScheduleDate(DateOnly? scheduledFor)
    {
        if (scheduledFor is not { } date)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (date < today)
        {
            throw ServiceException.BadRequest(
                "invalid_schedule_date", "scheduled_for cannot be in the past.");
        }

        var maxDate = today.AddDays(options.MaxScheduleDays);
        if (date > maxDate)
        {
            throw ServiceException.BadRequest(
                "invalid_schedule_date", $"scheduled_for cannot be more than {options.MaxScheduleDays} days in the future.");
        }
    }

    private static void EnsureSameRequest(PaymentRecord existing, string fingerprint)
    {
        if (existing.RequestFingerprint is not null
            && !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw ServiceException.Conflict(
                "idempotency_key_conflict",
                "the idempotency key was already used for a different payment request.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Pre-confirmation quote using the same config + FeeCalculator as payment creation,
    /// so the displayed total always matches the charged total.
    /// </summary>
    [HttpGet("quote")]
    public async Task<ActionResult<PaymentQuoteResponse>> Quote(
        [FromQuery(Name = "biller_id")] string? billerId,
        [FromQuery(Name = "invoice_id")] string? invoiceId,
        [FromQuery] string? method,
        [FromQuery(Name = "amount_cents")] int? amountCents,
        CancellationToken cancellationToken)
    {
        var requiredBillerId = RequireQueryValue(billerId, "biller_id");
        var requiredInvoiceId = RequireQueryValue(invoiceId, "invoice_id");
        var requiredMethod = RequireQueryValue(method, "method");
        BillerClaims.RequireBillerAccess(User, requiredBillerId);
        var config = await configs.GetAsync(requiredBillerId, cancellationToken).ConfigureAwait(false);
        if (!config.PaymentMethods.Contains(requiredMethod))
        {
            throw ServiceException.BadRequest(
                "method_not_enabled", $"payment method '{requiredMethod}' is not enabled for this biller");
        }

        var invoice = await invoices.GetAsync(requiredBillerId, requiredInvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("invoice_not_found", $"invoice {requiredInvoiceId} not found");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw ServiceException.Conflict("already_paid", $"invoice {requiredInvoiceId} is already paid");
        }

        // Quote the fee on the balance the payment would actually charge: the full outstanding
        // balance by default, or a requested partial amount (validated against that balance).
        var outstanding = invoice.AmountCents
            - await CommittedCentsAsync(requiredBillerId, requiredInvoiceId, cancellationToken).ConfigureAwait(false);
        if (outstanding <= 0)
        {
            throw ServiceException.Conflict("no_balance_due", $"invoice {requiredInvoiceId} has no balance due");
        }

        var amountToQuote = amountCents ?? outstanding;
        if (amountToQuote <= 0)
        {
            throw ServiceException.BadRequest("invalid_amount", "amount_cents must be a positive number of cents.");
        }

        if (amountToQuote > outstanding)
        {
            throw ServiceException.BadRequest(
                "amount_exceeds_balance",
                $"amount_cents {amountToQuote} exceeds the outstanding balance of {outstanding}.");
        }

        var (feeCents, totalCents) = FeeCalculator.Calculate(config, requiredMethod, amountToQuote);
        return new PaymentQuoteResponse(
            requiredBillerId, requiredInvoiceId, requiredMethod, amountToQuote, feeCents, totalCents, outstanding);
    }

    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PaymentResponse>> Get(
        string paymentId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);
        var record = await store.FindAsync(billerId, paymentId, cancellationToken).ConfigureAwait(false);

        if (record is null || !record.IsFinalized)
        {
            throw ServiceException.NotFound("not_found", $"payment {paymentId} not found");
        }

        return record.ToResponse();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentResponse>>> List(
        [FromQuery(Name = "biller_id")] string billerId,
        [FromQuery(Name = "payer_account_id")] string? payerAccountId,
        [FromQuery(Name = "invoice_id")] string? invoiceId,
        CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);
        var results = await store.ListAsync(billerId, payerAccountId, invoiceId, cancellationToken).ConfigureAwait(false);
        LogPaymentsListed(logger, billerId, payerAccountId, invoiceId, results.Count, Activity.Current?.TraceId.ToString());
        return Ok(results.Select(record => record.ToResponse()).ToArray());
    }

    private static string MintConfirmation()
    {
        Span<char> code = stackalloc char[6];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = ConfirmationAlphabet[RandomNumberGenerator.GetInt32(ConfirmationAlphabet.Length)];
        }

        return $"PRONTO-{new string(code)}";
    }

    private static string RequireQueryValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ServiceException.BadRequest($"{name}_required", $"{name} is required");
        }

        return value.Trim();
    }

    [LoggerMessage(4100, LogLevel.Information, "Created payment {PaymentId} for biller {BillerId}, invoice {InvoiceId}, status {Status}, total {TotalCents}; trace {TraceId}")]
    private static partial void LogPaymentCreated(ILogger logger, string paymentId, string billerId, string invoiceId, PaymentStatus status, int totalCents, string? traceId);
    [LoggerMessage(4101, LogLevel.Information, "Listed {PaymentCount} payments for biller {BillerId}, payer {PayerAccountId}, invoice {InvoiceId}; trace {TraceId}")]
    private static partial void LogPaymentsListed(ILogger logger, string billerId, string? payerAccountId, string? invoiceId, int paymentCount, string? traceId);
}
