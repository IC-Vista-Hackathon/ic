using Pronto.Invoice.Api.Common;
using System.Diagnostics;
using Pronto.Invoice.Api.Domain;
using Pronto.Invoice.Api.Repositories;
using Pronto.Invoice.Api.Seeding;
using Pronto.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.Invoice.Api.Controllers;

/// <summary>
/// Invoice endpoints, all partition-scoped under a biller.
/// Routes follow design/contracts.md (<c>/billers/{id}/invoices...</c>).
/// </summary>
[ApiController]
[Route("billers/{billerId}/invoices")]
public sealed partial class InvoicesController : ControllerBase
{
    private readonly IInvoiceRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InvoicesController> _logger;

    public InvoicesController(IInvoiceRepository repository, TimeProvider timeProvider, ILogger<InvoicesController>? logger = null)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InvoicesController>.Instance;
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
        LogInvoicesSeeded(_logger, billerId, accountNumber, invoices.Count, Activity.Current?.TraceId.ToString());

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
        CancellationToken cancellationToken,
        [FromQuery(Name = "include_closed")] bool includeClosed = false)
    {
        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(ApiError.Of("invalid_biller", "biller_id is required."));
        }

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return BadRequest(ApiError.Of("invalid_account_number", "account_number is required."));
        }

        var invoices = includeClosed
            ? await _repository.GetByAccountAsync(billerId, accountNumber.Trim(), cancellationToken)
            : await _repository.GetOpenAsync(billerId, accountNumber.Trim(), cancellationToken);
        LogInvoicesListed(_logger, billerId, accountNumber.Trim(), invoices.Count, includeClosed, Activity.Current?.TraceId.ToString());
        return Ok(new InvoiceListResponse(invoices.Select(ToResponse).ToList()));
    }

    /// <summary>Point read. <c>GET /billers/{billerId}/invoices/{invoiceId}</c>.</summary>
    [HttpGet("{invoiceId}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string billerId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(ApiError.Of("invalid_biller", "biller_id is required."));
        }

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
        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(ApiError.Of("invalid_biller", "biller_id is required."));
        }

        if (request.Status == Contracts.V1.Invoices.InvoiceStatus.Due)
        {
            return BadRequest(ApiError.Of(
                "invalid_status", "status must be 'scheduled' or 'paid'."));
        }

        if (string.IsNullOrWhiteSpace(request.PaymentId))
        {
            return BadRequest(ApiError.Of("invalid_payment_id", "payment_id is required."));
        }

        var result = await _repository.TryUpdateStatusAsync(
            billerId, invoiceId, request.Status.ToDomain(), request.PaymentId, cancellationToken);

        return result.Outcome switch
        {
            InvoiceTransitionOutcome.Updated => Ok(ToResponse(result.Invoice!)),
            InvoiceTransitionOutcome.NotFound =>
                NotFound(ApiError.Of("not_found", $"invoice {invoiceId} not found.")),
            InvoiceTransitionOutcome.AlreadyPaid => Conflict(
                ApiError.Of("already_paid", $"invoice {invoiceId} is already paid.")),
            InvoiceTransitionOutcome.ScheduleLocked => Conflict(ApiError.Of(
                "schedule_locked",
                $"invoice {invoiceId} has an active scheduled payment and cannot be settled by another payment.")),
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

    [LoggerMessage(3100, LogLevel.Information, "Seeded {InvoiceCount} invoices for biller {BillerId}, account {AccountNumber}; trace {TraceId}")]
    private static partial void LogInvoicesSeeded(ILogger logger, string billerId, string accountNumber, int invoiceCount, string? traceId);
    [LoggerMessage(3101, LogLevel.Information, "Listed {InvoiceCount} invoices for biller {BillerId}, account {AccountNumber}, include closed {IncludeClosed}; trace {TraceId}")]
    private static partial void LogInvoicesListed(ILogger logger, string billerId, string accountNumber, int invoiceCount, bool includeClosed, string? traceId);
}
