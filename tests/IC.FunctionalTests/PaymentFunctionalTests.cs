using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IC.FunctionalTests;

[Trait("Category", "Functional")]
[Collection(FunctionalSuite.Name)]
public sealed class PaymentFunctionalTests(DeployedEnvironment env)
{
    /// <summary>
    /// The cross-service chain: seed an invoice, pay it through the Payment API, and confirm
    /// the Payment service called the Invoice service to flip the invoice to paid, then read
    /// back the receipt.
    /// </summary>
    [Fact]
    public async Task PayingAnInvoiceMarksItPaidAndReturnsAReceipt()
    {
        if (!env.Enabled)
        {
            return;
        }

        var biller = env.RunBillerId;
        var account = "PAY-" + Guid.NewGuid().ToString("N")[..6];
        var invoiceBase = $"/invoices/billers/{biller}/invoices";

        using var seedResponse = await env.Client.PostAsJsonAsync(
            $"{invoiceBase}/seed", new SeedRequest(1, account, "Utility"), env.Json);
        var seeded = await seedResponse.Content.ReadFromJsonAsync<SeedResult>(env.Json);
        var invoice = seeded!.Invoices[0];
        Assert.Equal("due", invoice.Status);

        using var payResponse = await env.Client.PostAsJsonAsync(
            "/payments", new CreatePayment(biller, invoice.Id, "card"), env.Json);
        Assert.Equal(HttpStatusCode.Created, payResponse.StatusCode);
        var payment = await payResponse.Content.ReadFromJsonAsync<PaymentResult>(env.Json);
        Assert.NotNull(payment);
        Assert.False(string.IsNullOrWhiteSpace(payment!.PaymentId));
        Assert.False(string.IsNullOrWhiteSpace(payment.Confirmation));
        Assert.True(payment.TotalCents >= invoice.AmountCents);

        // Cross-service effect: the invoice is now paid.
        using var afterResponse = await env.Client.GetAsync($"{invoiceBase}/{invoice.Id}");
        var after = await afterResponse.Content.ReadFromJsonAsync<InvoiceItem>(env.Json);
        Assert.Equal("paid", after!.Status);

        // Receipt lookup (single-partition read).
        using var receiptResponse = await env.Client.GetAsync($"/payments/{payment.PaymentId}?biller_id={biller}");
        Assert.Equal(HttpStatusCode.OK, receiptResponse.StatusCode);
        var receipt = await receiptResponse.Content.ReadFromJsonAsync<PaymentResult>(env.Json);
        Assert.Equal(payment.PaymentId, receipt!.PaymentId);
    }

    [Fact]
    public async Task PurchaseSucceedsForANewBiller()
    {
        if (!env.Enabled)
        {
            return;
        }

        // One purchase per biller, so use a fresh id (tracked for cleanup) rather than the shared run id.
        var biller = "func-buy-" + Guid.NewGuid().ToString("N")[..8];
        env.Track(biller);

        using var response = await env.Client.PostAsJsonAsync(
            "/purchases", new CreatePurchase(biller, "shared"), env.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = await response.Content.ReadFromJsonAsync<PurchaseResult>(env.Json);
        Assert.Equal(biller, purchase!.BillerId);
    }

    private sealed record SeedRequest(int Count, string AccountNumber, string BillType);

    private sealed record SeedResult(IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceItem(string Id, string Status, int AmountCents);

    private sealed record CreatePayment(string BillerId, string InvoiceId, string Method);

    private sealed record PaymentResult(string PaymentId, string Confirmation, int TotalCents);

    private sealed record CreatePurchase(string BillerId, string Plan);

    private sealed record PurchaseResult(string PurchaseId, string BillerId);
}
