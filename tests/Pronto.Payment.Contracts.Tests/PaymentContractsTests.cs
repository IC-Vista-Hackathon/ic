using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Payments;
using Xunit;

namespace Pronto.Payment.Contracts.Tests;

public sealed class PaymentContractsTests
{
    // Wire policy: snake_case + lowercase string enums (design/contracts.md).
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void PaymentResponseRoundTripsThroughJson()
    {
        var response = new PaymentResponse(
            PaymentId: "d9f2e6a0-6f0a-4d3e-9a2b-1c8f5e7b4a90",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            InvoiceId: "9c0a4b6e-2d15-47f8-a3e9-70b2c5d8e422",
            PayerAccountId: null,
            Method: "card",
            AmountCents: 8420,
            FeeCents: 211,
            TotalCents: 8631,
            Confirmation: "PRONTO-4F2A9B",
            Status: PaymentStatus.Succeeded,
            ScheduledFor: null,
            ReceiptMessage: "Thanks from the City of Plano!",
            CreatedAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<PaymentResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.Equal(response, roundTripped);
    }

    [Fact]
    public void CreatePaymentRequestOptionalFieldsDefaultToNull()
    {
        var request = new CreatePaymentRequest(
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            InvoiceId: "9c0a4b6e-2d15-47f8-a3e9-70b2c5d8e422",
            Method: "ach");

        Assert.Null(request.PayerAccountId);
        Assert.Null(request.ScheduledFor);
    }

    [Fact]
    public void CreatePaymentRequestDeserializesWithoutOptionalProperties()
    {
        const string json =
            """{"biller_id":"b-1","invoice_id":"i-1","method":"card"}""";

        var request = JsonSerializer.Deserialize<CreatePaymentRequest>(json, CaseInsensitive);

        Assert.NotNull(request);
        Assert.Equal("card", request.Method);
        Assert.Null(request.PayerAccountId);
        Assert.Null(request.ScheduledFor);
    }

    [Fact]
    public void ScheduledForRoundTripsAsDateOnly()
    {
        var request = new CreatePaymentRequest(
            BillerId: "b-1",
            InvoiceId: "i-1",
            Method: "ach",
            ScheduledFor: new DateOnly(2026, 7, 24));

        var roundTripped = JsonSerializer.Deserialize<CreatePaymentRequest>(
            JsonSerializer.Serialize(request, CaseInsensitive), CaseInsensitive);

        Assert.Equal(new DateOnly(2026, 7, 24), roundTripped!.ScheduledFor);
    }
}
