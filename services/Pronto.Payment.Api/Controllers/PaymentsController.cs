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
    public async Task<ActionResult<PaymentResponse>> Create(
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

        var idempotencyKey = Normalize(idempotencyHeader) ?? Normalize(request.IdempotencyKey);
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
        var paymentId = PaymentRecord.DeriveId(request.BillerId, idempotencyKey);
        var fingerprint = PaymentRecord.Fingerprint(
            request.InvoiceId, request.Method, request.PayerAccountId, request.ScheduledFor);

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

        if (invoice.Status == InvoiceStatus.Scheduled)
        {
            throw ServiceException.Conflict(
                "invoice_scheduled", $"invoice {request.InvoiceId} already has an active scheduled payment");
        }

        var (feeCents, totalCents) = FeeCalculator.Calculate(config, request.Method, invoice.AmountCents);
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
            AmountCents = invoice.AmountCents,
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
            EnsureSameRequest(begin.Record, fingerprint);
        }

        var finalized = await workflow.DriveInitialAsync(begin.Record, cancellationToken).ConfigureAwait(false);
        LogPaymentCreated(logger, finalized.PaymentId, finalized.BillerId, finalized.InvoiceId, finalized.WireStatus, finalized.TotalCents, Activity.Current?.TraceId.ToString());

        return BuildResult(finalized, created: begin.Created);
    }

    private ActionResult<PaymentResponse> BuildResult(PaymentRecord record, bool created)
    {
        if (record.Lifecycle == PaymentLifecycle.Failed)
        {
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

        var (feeCents, totalCents) = FeeCalculator.Calculate(config, requiredMethod, invoice.AmountCents);
        return new PaymentQuoteResponse(
            requiredBillerId, requiredInvoiceId, requiredMethod, invoice.AmountCents, feeCents, totalCents);
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
