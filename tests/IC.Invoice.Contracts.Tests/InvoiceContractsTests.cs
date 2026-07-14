using System.Text.Json;
using System.Text.Json.Serialization;
using IC.Invoice.Contracts.V1.Invoices;
using Xunit;

namespace IC.Invoice.Contracts.Tests;

public sealed class InvoiceContractsTests
{
    // Wire policy: camelCase + enums as strings (design/contracts.md).
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void InvoiceResponseRoundTripsThroughJson()
    {
        var invoice = new InvoiceResponse(
            InvoiceId: "2f6e8a1c-4b7d-40e3-9c5a-d8b1e0f2a634",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            AccountNumber: "UTIL-1912723364",
            PayerName: "Brianne Will",
            Description: "July water & sewer",
            AmountCents: 8420,
            DueDate: new DateOnly(2026, 7, 25),
            Status: InvoiceStatus.Due);

        var roundTripped = JsonSerializer.Deserialize<InvoiceResponse>(
            JsonSerializer.Serialize(invoice, Wire), Wire);

        Assert.Equal(invoice, roundTripped);
    }

    [Fact]
    public void InvoiceStatusSerializesAsString()
    {
        var json = JsonSerializer.Serialize(InvoiceStatus.Scheduled, Wire);

        Assert.Equal("\"scheduled\"", json.ToLowerInvariant());
    }

    [Fact]
    public void SeedRequestOptionalFieldsDefaultToNull()
    {
        var request = new SeedInvoicesRequest(Count: 3);

        Assert.Null(request.AccountNumber);
        Assert.Null(request.PayerName);
    }

    [Fact]
    public void UpdateStatusRequestRoundTripsThroughJson()
    {
        var request = new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "p-1");

        var roundTripped = JsonSerializer.Deserialize<UpdateInvoiceStatusRequest>(
            JsonSerializer.Serialize(request, Wire), Wire);

        Assert.Equal(request, roundTripped);
    }
}
