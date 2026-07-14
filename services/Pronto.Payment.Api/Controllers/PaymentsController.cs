using System.Security.Cryptography;
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
public sealed class PaymentsController : ControllerBase
{
    private const string ConfirmationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IPaymentStore store;
    private readonly IInvoiceClient invoices;
    private readonly IBillerConfigClient configs;

    public PaymentsController(IPaymentStore store, IInvoiceClient invoices, IBillerConfigClient configs)
    {
        this.store = store;
        this.invoices = invoices;
        this.configs = configs;
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
        await store.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        return Created($"/payments/{payment.PaymentId}?biller_id={payment.BillerId}", payment);
    }

    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PaymentResponse>> Get(
        string paymentId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
        => await store.FindAsync(billerId, paymentId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"payment {paymentId} not found");

    private static string MintConfirmation()
    {
        Span<char> code = stackalloc char[6];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = ConfirmationAlphabet[RandomNumberGenerator.GetInt32(ConfirmationAlphabet.Length)];
        }

        return $"PRONTO-{new string(code)}";
    }
}
