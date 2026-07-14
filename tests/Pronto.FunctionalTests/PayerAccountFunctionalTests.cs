using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Pronto.FunctionalTests;

[Trait("Category", "Functional")]
[Collection(FunctionalSuite.Name)]
public sealed class PayerAccountFunctionalTests(DeployedEnvironment env)
{
    [Fact]
    public async Task RegisterThenGetAndUpdatePreferences()
    {
        if (!env.Enabled)
        {
            return;
        }

        var biller = env.RunBillerId;
        var email = $"payer-{Guid.NewGuid():N}@example.com";

        using var registerResponse = await env.Client.PostAsJsonAsync(
            "/payers",
            new RegisterPayer(biller, "Test Payer", email, null, ["ACCT-1"]),
            env.Json);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var payer = await registerResponse.Content.ReadFromJsonAsync<PayerResult>(env.Json);
        Assert.NotNull(payer);
        Assert.False(string.IsNullOrWhiteSpace(payer!.PayerId));
        Assert.Equal(email, payer.Email);

        using var getResponse = await env.Client.GetAsync($"/payers/{payer.PayerId}?biller_id={biller}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var prefsResponse = await env.Client.PatchAsJsonAsync(
            $"/payers/{payer.PayerId}/preferences?biller_id={biller}",
            new UpdatePreferences(Autopay: true, Paperless: true, Channels: ["email"]),
            env.Json);
        Assert.Equal(HttpStatusCode.OK, prefsResponse.StatusCode);
        var updated = await prefsResponse.Content.ReadFromJsonAsync<PayerResult>(env.Json);
        Assert.True(updated!.Preferences.Autopay);
        Assert.True(updated.Preferences.Paperless);
    }

    [Fact]
    public async Task DuplicateEmailIsRejected()
    {
        if (!env.Enabled)
        {
            return;
        }

        var biller = env.RunBillerId;
        var email = $"dupe-{Guid.NewGuid():N}@example.com";
        var request = new RegisterPayer(biller, "Dupe", email, null, ["ACCT-9"]);

        using var first = await env.Client.PostAsJsonAsync("/payers", request, env.Json);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await env.Client.PostAsJsonAsync("/payers", request, env.Json);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record RegisterPayer(
        string BillerId, string Name, string Email, string? Phone, IReadOnlyList<string> AccountNumbers);

    private sealed record UpdatePreferences(bool? Autopay, bool? Paperless, IReadOnlyList<string> Channels);

    private sealed record PayerResult(string PayerId, string Email, Preferences Preferences);

    private sealed record Preferences(bool Autopay, bool Paperless);
}
