namespace Pronto.Invoice.Api.Domain;

/// <summary>
/// Invoice lifecycle status. Wire values are lowercase (see design/entities.md Invoice):
/// <c>due</c> | <c>scheduled</c> | <c>paid</c>. <c>scheduled</c> mirrors a Payment scheduled
/// against this invoice.
/// </summary>
public enum InvoiceStatus
{
    Due,
    Scheduled,
    Paid,
}
