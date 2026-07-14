using IC.Invoice.Api.Common;
using IC.Invoice.Api.Domain;
using IC.Invoice.Api.Repositories;
using IC.Invoice.Api.Seeding;
using IC.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace IC.Invoice.Api.Controllers;

/// <summary>
/// Invoice endpoints, all partition-scoped under a biller.
/// Routes follow design/contracts.md (<c>/billers/{id}/invoices...</c>).
/// </summary>
[ApiController]
[Route("billers/{billerId}/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceRepository _repository;
    private readonly TimeProvider _timeProvider;

    public InvoicesController(IInvoiceRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Internal: seed fake invoice data at onboarding.
    /// <c>POST /billers/{billerId}/invoices/seed</c>.
    /// </summary>
    [HttpPost("seed")]
    [ProducesResponseType<SeedInvoicesResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Seed(
        string billerId,
        [FromBody] SeedInvoicesRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(ApiError.Of("invalid_biller", "biller_id is required."));
        }

        request ??= new SeedInvoicesRequest();

        var accountNumber = string.IsNullOrWhiteSpace(request.AccountNumber)
            ? GenerateAccountNumber()
            : request.AccountNumber.Trim();

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var invoices = FakeInvoiceFactory.Create(
            billerId, accountNumber, request.Count, request.BillType, today);

        await _repository.AddRangeAsync(invoices, cancellationToken);

        var response = new SeedInvoicesResponse(
            invoices.Count,
            accountNumber,
            invoices.Select(ToResponse).ToList());

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Open invoices for an account. <c>GET /billers/{billerId}/invoices?account_number=</c>.
    /// Unknown account → empty list, not 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<InvoiceListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        string billerId,
        [FromQuery(Name = "account_number")] string? accountNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return BadRequest(ApiError.Of("invalid_account_number", "account_number is required."));
        }

        var invoices = await _repository.GetOpenAsync(billerId, accountNumber.Trim(), cancellationToken);
        return Ok(new InvoiceListResponse(invoices.Select(ToResponse).ToList()));
    }

    /// <summary>Point read. <c>GET /billers/{billerId}/invoices/{invoiceId}</c>.</summary>
    [HttpGet("{invoiceId}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string billerId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await _repository.FindAsync(billerId, invoiceId, cancellationToken);
        return invoice is null
            ? NotFound(ApiError.Of("not_found", $"invoice {invoiceId} not found."))
            : Ok(ToResponse(invoice));
    }

    /// <summary>
    /// Internal: Payment Service asserts <c>due→paid</c>, <c>due→scheduled</c>, or
    /// <c>scheduled→paid</c>. <c>POST /billers/{billerId}/invoices/{invoiceId}/status</c>.
    /// Idempotent per <c>payment_id</c>.
    /// </summary>
    [HttpPost("{invoiceId}/status")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        string billerId,
        string invoiceId,
        [FromBody] UpdateInvoiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var target = InvoiceStatusWire.FromWire(request.Status);
        if (target is null || target == InvoiceStatus.Due)
        {
            return BadRequest(ApiError.Of(
                "invalid_status", "status must be 'scheduled' or 'paid'."));
        }

        if (string.IsNullOrWhiteSpace(request.PaymentId))
        {
            return BadRequest(ApiError.Of("invalid_payment_id", "payment_id is required."));
        }

        var result = await _repository.TryUpdateStatusAsync(
            billerId, invoiceId, target.Value, request.PaymentId, cancellationToken);

        return result.Outcome switch
        {
            InvoiceTransitionOutcome.Updated => Ok(ToResponse(result.Invoice!)),
            InvoiceTransitionOutcome.NotFound =>
                NotFound(ApiError.Of("not_found", $"invoice {invoiceId} not found.")),
            InvoiceTransitionOutcome.AlreadyPaid => Conflict(
                ApiError.Of("already_paid", $"invoice {invoiceId} is already paid.")),
            _ => Conflict(ApiError.Of(
                "invalid_transition",
                $"invoice {invoiceId} cannot move to {request.Status}.")),
        };
    }

    private static InvoiceResponse ToResponse(InvoiceDocument invoice) => new(
        invoice.Id,
        invoice.BillerId,
        invoice.AccountNumber,
        invoice.PayerName,
        invoice.Description,
        invoice.AmountCents,
        invoice.DueDate,
        invoice.Status.ToWire());

    private static string GenerateAccountNumber() =>
        "ACCT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
}
