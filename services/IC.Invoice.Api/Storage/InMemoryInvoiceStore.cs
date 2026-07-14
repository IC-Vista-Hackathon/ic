using IC.Invoice.Contracts.V1.Invoices;
using IC.ServiceDefaults.Errors;

namespace IC.Invoice.Api.Storage;

public sealed class InMemoryInvoiceStore : IInvoiceStore
{
    private static readonly string[] Descriptions =
    [
        "Water & sewer service",
        "Real estate tax",
        "Electric service",
        "Trash & recycling",
        "Stormwater fee",
    ];

    private static readonly string[] PayerNames =
    [
        "Brianne Will",
        "Teresa Cormier",
        "Marcus Chen",
        "Adaeze Okafor",
        "Luis Herrera",
    ];

    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string InvoiceId), InvoiceResponse> invoices = [];
    private readonly Dictionary<(string BillerId, string InvoiceId), string> paymentIds = [];

    public IReadOnlyList<InvoiceResponse> List(string billerId, string? accountNumber, InvoiceStatus? status)
    {
        lock (gate)
        {
            return [.. invoices.Values.Where(invoice =>
                invoice.BillerId == billerId
                && (accountNumber is null || invoice.AccountNumber == accountNumber)
                && (status is null
                    ? invoice.Status != InvoiceStatus.Paid
                    : invoice.Status == status))];
        }
    }

    public InvoiceResponse? Find(string billerId, string invoiceId)
    {
        lock (gate)
        {
            return invoices.GetValueOrDefault((billerId, invoiceId));
        }
    }

    public IReadOnlyList<InvoiceResponse> Seed(string billerId, SeedInvoicesRequest request)
    {
        var seeded = new List<InvoiceResponse>();
        lock (gate)
        {
            for (var index = 0; index < request.Count; index++)
            {
                var id = Guid.NewGuid().ToString();
                var invoice = new InvoiceResponse(
                    InvoiceId: id,
                    BillerId: billerId,
                    AccountNumber: request.AccountNumber
                        ?? $"ACCT-{Random.Shared.Next(100000000, 999999999)}",
                    PayerName: request.PayerName ?? PayerNames[index % PayerNames.Length],
                    Description: Descriptions[index % Descriptions.Length],
                    AmountCents: Random.Shared.Next(15, 500) * 25,
                    DueDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Random.Shared.Next(5, 30)),
                    Status: InvoiceStatus.Due);
                invoices[(billerId, id)] = invoice;
                seeded.Add(invoice);
            }
        }

        return seeded;
    }

    public InvoiceResponse UpdateStatus(string billerId, string invoiceId, UpdateInvoiceStatusRequest request)
    {
        lock (gate)
        {
            var key = (billerId, invoiceId);
            var invoice = invoices.GetValueOrDefault(key)
                ?? throw ServiceException.NotFound("not_found", $"invoice {invoiceId} not found");

            // Idempotent replay: same payment re-asserting the status it already produced.
            if (invoice.Status == request.Status && paymentIds.GetValueOrDefault(key) == request.PaymentId)
            {
                return invoice;
            }

            var allowed = (invoice.Status, request.Status) switch
            {
                (InvoiceStatus.Due, InvoiceStatus.Paid) => true,
                (InvoiceStatus.Due, InvoiceStatus.Scheduled) => true,
                (InvoiceStatus.Scheduled, InvoiceStatus.Paid) => true,
                _ => false,
            };

            if (!allowed)
            {
                throw invoice.Status == InvoiceStatus.Paid
                    ? ServiceException.Conflict("already_paid", $"invoice {invoiceId} is already paid")
                    : ServiceException.Conflict(
                        "invalid_transition",
                        $"invoice {invoiceId} cannot move from {invoice.Status} to {request.Status}");
            }

            var updated = invoice with { Status = request.Status };
            invoices[key] = updated;
            paymentIds[key] = request.PaymentId;
            return updated;
        }
    }
}
