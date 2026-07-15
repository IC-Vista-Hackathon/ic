using System.Security.Cryptography;
using System.Diagnostics;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Fees;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.Payment.Api.Controllers;

[ApiController]
[Route("payments")]
public sealed partial class PaymentsController : ControllerBase
{
    private const string ConfirmationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 200;

    private readonly IPaymentStore store;
    private readonly IInvoiceClient invoices;
    private readonly IBillerConfigClient configs;
    private readonly ILogger<PaymentsController> logger;

    public PaymentsController(IPaymentStore store, IInvoiceClient invoices, IBillerConfigClient configs, ILogger<PaymentsController> logger)
    {
        this.store = store;
        this.invoices = invoices;
        this.configs = configs;
        this.logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> Create(
        CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = ReadIdempotencyKey();
        if (idempotencyKey is not null)
        {
            // Replay: a repeat with a key we've already seen resolves to the ORIGINAL payment —
            // before any conflict/validation check, so a client retry after a network blip never
            // double-charges or 409s on an invoice its first call already paid.
            var original = await store.FindByIdempotencyKeyAsync(request.BillerId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (original is not null)
            {
                return await ResolveReplayAsync(original, idempotencyKey, cancellationToken).ConfigureAwait(false);
            }
        }

        var config = await configs.GetAsync(request.BillerId, cancellationToken).ConfigureAwait(false);

        if (!config.PaymentMethods.Contains(request.Method))
        {
            throw ServiceException.BadRequest(
                "method_not_enabled", $"payment method '{request.Method}' is not enabled for this biller");
        }

        var invoice = await invoices.GetAsync(request.BillerId, request.InvoiceId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw ServiceException.NotFound("invoice_not_found", $"invoice {request.InvoiceId} not found");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw ServiceException.Conflict("already_paid", $"invoice {request.InvoiceId} is already paid");
        }

        var (feeCents, totalCents) = FeeCalculator.Calculate(config, request.Method, invoice.AmountCents);
        var paymentId = Guid.NewGuid().ToString();
        var scheduled = request.ScheduledFor is not null;

        // 1. PERSIST BEFORE MARK: durably write the payment in a recoverable Pending state (and
        //    reserve the idempotency key) BEFORE asserting the invoice transition. A crash after
        //    this point always leaves an auditable payment row, never an orphaned Paid invoice.
        var pending = new PaymentResponse(
            PaymentId: paymentId,
            BillerId: request.BillerId,
            InvoiceId: request.InvoiceId,
            PayerAccountId: request.PayerAccountId,
            Method: request.Method,
            AmountCents: invoice.AmountCents,
            FeeCents: feeCents,
            TotalCents: totalCents,
            Confirmation: MintConfirmation(),
            Status: PaymentStatus.Pending,
            ScheduledFor: request.ScheduledFor,
            ReceiptMessage: config.ReceiptMessage,
            CreatedAt: DateTimeOffset.UtcNow);

        var creation = await store.CreatePendingAsync(pending, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (creation.IsReplay)
        {
            // Concurrent first-time race on the same key: resolve to whoever won the reservation.
            return await ResolveReplayAsync(creation.Payment, idempotencyKey, cancellationToken).ConfigureAwait(false);
        }

        // 2-3. Assert the invoice transition (atomicity authority; a concurrent duplicate loses
        //      here with 409, leaving only a recoverable Pending row) then mark the payment terminal.
        var payment = await FinalizeAsync(pending, cancellationToken).ConfigureAwait(false);
        return Created($"/payments/{payment.PaymentId}?biller_id={payment.BillerId}", payment);
    }

    /// <summary>
    /// Resolve an idempotent replay. A payment left <see cref="PaymentStatus.Pending"/> by a
    /// mid-flight crash is resumed to completion (the invoice transition is idempotent per
    /// payment id, so re-asserting it is safe); a terminal payment is returned verbatim.
    /// </summary>
    private async Task<ActionResult<PaymentResponse>> ResolveReplayAsync(
        PaymentResponse original, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (original.Status == PaymentStatus.Pending)
        {
            var resumed = await FinalizeAsync(original, cancellationToken).ConfigureAwait(false);
            return Ok(resumed);
        }

        LogPaymentReplayed(logger, original.PaymentId, original.BillerId, idempotencyKey, Activity.Current?.TraceId.ToString());
        return Ok(original);
    }

    /// <summary>
    /// Assert the invoice transition then mark the payment terminal. Re-asserting the same
    /// transition with the same payment id is idempotent on the Invoice Service, so this is safe
    /// to call both for a fresh payment and when resuming a crashed <c>Pending</c> one.
    /// </summary>
    private async Task<PaymentResponse> FinalizeAsync(PaymentResponse pending, CancellationToken cancellationToken)
    {
        var scheduled = pending.ScheduledFor is not null;
        await invoices.UpdateStatusAsync(
            pending.BillerId,
            pending.InvoiceId,
            new UpdateInvoiceStatusRequest(
                scheduled ? InvoiceStatus.Scheduled : InvoiceStatus.Paid, pending.PaymentId),
            cancellationToken).ConfigureAwait(false);

        var payment = pending with
        {
            Status = scheduled ? PaymentStatus.Scheduled : PaymentStatus.Succeeded,
        };
        await store.UpdateAsync(payment, cancellationToken).ConfigureAwait(false);
        LogPaymentCreated(logger, payment.PaymentId, payment.BillerId, payment.InvoiceId, payment.Status, payment.TotalCents, Activity.Current?.TraceId.ToString());
        return payment;
    }

    private string? ReadIdempotencyKey()
    {
        if (Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values))
        {
            var key = values.ToString().Trim();
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (key.Length > MaxIdempotencyKeyLength)
            {
                throw ServiceException.BadRequest(
                    "idempotency_key_too_long",
                    $"{IdempotencyKeyHeader} must be at most {MaxIdempotencyKeyLength} characters");
            }

            return key;
        }

        return null;
    }

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
        => await store.FindAsync(billerId, paymentId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payment {paymentId} not found");

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentResponse>>> List(
        [FromQuery(Name = "biller_id")] string billerId,
        [FromQuery(Name = "payer_account_id")] string? payerAccountId,
        [FromQuery(Name = "invoice_id")] string? invoiceId,
        CancellationToken cancellationToken)
    {
        var results = await store.ListAsync(billerId, payerAccountId, invoiceId, cancellationToken).ConfigureAwait(false);
        LogPaymentsListed(logger, billerId, payerAccountId, invoiceId, results.Count, Activity.Current?.TraceId.ToString());
        return Ok(results);
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
    [LoggerMessage(4102, LogLevel.Information, "Idempotent replay returned payment {PaymentId} for biller {BillerId}, key {IdempotencyKey}; trace {TraceId}")]
    private static partial void LogPaymentReplayed(ILogger logger, string paymentId, string billerId, string? idempotencyKey, string? traceId);
    [LoggerMessage(4101, LogLevel.Information, "Listed {PaymentCount} payments for biller {BillerId}, payer {PayerAccountId}, invoice {InvoiceId}; trace {TraceId}")]
    private static partial void LogPaymentsListed(ILogger logger, string billerId, string? payerAccountId, string? invoiceId, int paymentCount, string? traceId);
}
