using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IC.FunctionalTests;

[Trait("Category", "Functional")]
[Collection(FunctionalSuite.Name)]
public sealed class InvoiceFunctionalTests(DeployedEnvironment env)
{
    [Fact]
    public async Task SeedThenLookupAndGetById()
    {
        if (!env.Enabled)
        {
            return;
        }

        var account = "ACME-" + Guid.NewGuid().ToString("N")[..6];
        var baseUrl = $"/invoices/billers/{env.RunBillerId}/invoices";

        using var seedResponse = await env.Client.PostAsJsonAsync(
            $"{baseUrl}/seed", new SeedRequest(3, account, "Utility"), env.Json);
        Assert.Equal(HttpStatusCode.Created, seedResponse.StatusCode);
        var seeded = await seedResponse.Content.ReadFromJsonAsync<SeedResult>(env.Json);
        Assert.NotNull(seeded);
        Assert.Equal(3, seeded!.Seeded);
        Assert.Equal(account, seeded.AccountNumber);

        using var listResponse = await env.Client.GetAsync($"{baseUrl}?account_number={account}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<InvoiceList>(env.Json);
        Assert.NotNull(list);
        Assert.Equal(3, list!.Invoices.Count);
        Assert.All(list.Invoices, invoice => Assert.Equal(account, invoice.AccountNumber));

        var target = list.Invoices[0];
        using var getResponse = await env.Client.GetAsync($"{baseUrl}/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<InvoiceItem>(env.Json);
        Assert.Equal(target.Id, fetched!.Id);
        Assert.Equal("due", fetched.Status);
    }

    [Fact]
    public async Task LookupUnknownAccountReturnsEmpty()
    {
        if (!env.Enabled)
        {
            return;
        }

        var baseUrl = $"/invoices/billers/{env.RunBillerId}/invoices";

        using var response = await env.Client.GetAsync($"{baseUrl}?account_number=NOPE-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<InvoiceList>(env.Json);
        Assert.Empty(list!.Invoices);
    }

    private sealed record SeedRequest(int Count, string AccountNumber, string BillType);

    private sealed record SeedResult(int Seeded, string AccountNumber, IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceList(IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceItem(string Id, string AccountNumber, string Status, int AmountCents);
}
