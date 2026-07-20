using System.Text.Json.Nodes;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-11 — The live portal's payer-chat turn resolves a real bill and a grounded payment plan
/// through the MCP router (#92), and a pay-now intent only ever surfaces a confirm control — the
/// assistant never submits payment on its own. These deployed tests drive the anonymous payer-chat
/// endpoint end-to-end over the gateway. See docs/pronto-functional-requirements.md.
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class PayerChatTests
{
    [SkippableFact]
    public async Task OpeningTurnResolvesTheBillAndReturnsAGroundedPlan()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var (billerId, invoice) = await SeedBillWithInvoiceAsync(client);
        var invoiceId = invoice["id"]!.GetValue<string>();
        var amountCents = invoice["amount_cents"]!.GetValue<int>();

        var turn = await client.SendPayerChatAsync(
            billerId, invoiceId, accountNumber: ProntoApiClient.SeededAccountNumber);

        Assert.False(string.IsNullOrWhiteSpace(turn["reply"].AsStringOrNull()), "the turn must return a payer-facing reply");

        var artifacts = turn["artifacts"]!;
        Assert.Equal(invoiceId, artifacts["bill_summary"]!["invoice_id"].AsStringOrNull());
        Assert.Equal(amountCents, artifacts["bill_summary"]!["amount_cents"]!.GetValue<int>());

        var plan = artifacts["payment_plan"]!;
        Assert.False(string.IsNullOrWhiteSpace(plan["method"].AsStringOrNull()), "the plan must recommend a payment method");
        Assert.False(string.IsNullOrWhiteSpace(plan["rationale"].AsStringOrNull()), "the plan must explain its recommendation");
        Assert.True(
            plan["total_cents"]!.GetValue<int>() >= amountCents,
            "the plan total must be the bill amount plus a non-negative server-quoted fee");

        // The opening turn (no payer message) never surfaces a confirm control.
        Assert.Null(artifacts["action"]);
    }

    [SkippableFact]
    public async Task PayNowIntentSurfacesAConfirmActionButNeverSubmits()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var (billerId, invoice) = await SeedBillWithInvoiceAsync(client);
        var invoiceId = invoice["id"]!.GetValue<string>();

        var turn = await client.SendPayerChatAsync(
            billerId, invoiceId,
            accountNumber: ProntoApiClient.SeededAccountNumber,
            payerMessage: "I want to pay it now");

        var artifacts = turn["artifacts"]!;
        var action = artifacts["action"];
        Assert.NotNull(action);
        Assert.Equal("confirm_payment", action!["kind"].AsStringOrNull());

        var plan = artifacts["payment_plan"]!;
        Assert.Equal(plan["method"].AsStringOrNull(), action["method"].AsStringOrNull());
        Assert.Equal(plan["total_cents"]!.GetValue<int>(), action["total_cents"]!.GetValue<int>());
    }

    private static async Task<(string BillerId, JsonNode Invoice)> SeedBillWithInvoiceAsync(ProntoApiClient client)
    {
        var created = await client.CreateBillerAsync(
            "Payer Chat Co", billType: "utility", website: "https://payer-chat.example.com");
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        var invoices = await client.ListSeededInvoicesAsync(billerId);
        Assert.NotEmpty(invoices);
        return (billerId, invoices[0]!);
    }
}
