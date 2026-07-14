using System.Security.Cryptography;
using System.Diagnostics;
using IC.Invoice.Contracts.V1.Invoices;
using IC.Payment.Api.Clients;
using IC.Payment.Api.Fees;
using IC.Payment.Api.Storage;
using IC.Payment.Contracts.V1.Payments;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace IC.Payment.Api.Controllers;

[ApiController]
[Route("payments")]
public sealed partial class PaymentsController : ControllerBase
{
    private const string ConfirmationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

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

        // The Invoice Service transition is the atomicity authority: a concurrent duplicate
        // pay attempt loses here with 409 before any payment is persisted.
        await invoices.UpdateStatusAsync(
            request.BillerId,
            request.InvoiceId,
            new UpdateInvoiceStatusRequest(
                scheduled ? InvoiceStatus.Scheduled : InvoiceStatus.Paid, paymentId),
            cancellationToken).ConfigureAwait(false);

        var payment = new PaymentResponse(
            PaymentId: paymentId,
            BillerId: request.BillerId,
            InvoiceId: request.InvoiceId,
            PayerAccountId: request.PayerAccountId,
            Method: request.Method,
            AmountCents: invoice.AmountCents,
            FeeCents: feeCents,
            TotalCents: totalCents,
            Confirmation: MintConfirmation(),
            Status: scheduled ? PaymentStatus.Scheduled : PaymentStatus.Succeeded,
            ScheduledFor: request.ScheduledFor,
            ReceiptMessage: config.ReceiptMessage,
            CreatedAt: DateTimeOffset.UtcNow);
        store.Add(payment);
        LogPaymentCreated(logger, payment.PaymentId, payment.BillerId, payment.InvoiceId, payment.Status, payment.TotalCents, Activity.Current?.TraceId.ToString());

        return Created($"/payments/{payment.PaymentId}?biller_id={payment.BillerId}", payment);
    }

    [HttpGet("{paymentId}")]
    public ActionResult<PaymentResponse> Get(string paymentId, [FromQuery(Name = "biller_id")] string billerId)
        => store.Find(billerId, paymentId)
            ?? throw ServiceException.NotFound("not_found", $"payment {paymentId} not found");

    [HttpGet]
    public ActionResult<IReadOnlyList<PaymentResponse>> List(
        [FromQuery(Name = "biller_id")] string billerId,
        [FromQuery(Name = "payer_account_id")] string? payerAccountId,
        [FromQuery(Name = "invoice_id")] string? invoiceId)
    {
        var results = store.List(billerId, payerAccountId, invoiceId);
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

        return $"IC-{new string(code)}";
    }

    [LoggerMessage(4100, LogLevel.Information, "Created payment {PaymentId} for biller {BillerId}, invoice {InvoiceId}, status {Status}, total {TotalCents}; trace {TraceId}")]
    private static partial void LogPaymentCreated(ILogger logger, string paymentId, string billerId, string invoiceId, PaymentStatus status, int totalCents, string? traceId);
    [LoggerMessage(4101, LogLevel.Information, "Listed {PaymentCount} payments for biller {BillerId}, payer {PayerAccountId}, invoice {InvoiceId}; trace {TraceId}")]
    private static partial void LogPaymentsListed(ILogger logger, string billerId, string? payerAccountId, string? invoiceId, int paymentCount, string? traceId);
}
