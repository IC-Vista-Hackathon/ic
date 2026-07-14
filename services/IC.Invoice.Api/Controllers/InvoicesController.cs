using IC.Invoice.Api.Storage;
using IC.Invoice.Contracts.V1.Invoices;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace IC.Invoice.Api.Controllers;

[ApiController]
[Route("billers/{billerId}/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceStore store;

    public InvoicesController(IInvoiceStore store)
    {
        this.store = store;
    }

    /// <summary>Open invoices (due + scheduled) by default; pass status= for a specific one.</summary>
    [HttpGet]
    public ActionResult<InvoiceListResponse> List(
        string billerId,
        [FromQuery] string? accountNumber,
        [FromQuery] InvoiceStatus? status)
        => new InvoiceListResponse(store.List(billerId, accountNumber, status));

    [HttpGet("{invoiceId}")]
    public ActionResult<InvoiceResponse> Get(string billerId, string invoiceId)
        => store.Find(billerId, invoiceId)
            ?? throw ServiceException.NotFound("not_found", $"invoice {invoiceId} not found");

    [HttpPost("seed")]
    public ActionResult<InvoiceListResponse> Seed(string billerId, SeedInvoicesRequest request)
    {
        if (request.Count is < 1 or > 100)
        {
            throw ServiceException.BadRequest("invalid_count", "count must be between 1 and 100");
        }

        var seeded = store.Seed(billerId, request);
        return Created($"/billers/{billerId}/invoices", new InvoiceListResponse(seeded));
    }

    /// <summary>Internal: Payment Service asserts due→paid/scheduled, scheduled→paid.</summary>
    [HttpPost("{invoiceId}/status")]
    public ActionResult<InvoiceResponse> UpdateStatus(
        string billerId,
        string invoiceId,
        UpdateInvoiceStatusRequest request)
        => store.UpdateStatus(billerId, invoiceId, request);
}
