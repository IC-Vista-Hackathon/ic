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
