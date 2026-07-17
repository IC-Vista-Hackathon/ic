using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Contract tests for the payer seeder's HTTP behavior: it posts a well-formed snake_case
/// <c>RegisterPayerRequest</c> to the dedicated <c>/payers/seed</c> endpoint, treats a
/// <c>409 Conflict</c> as an idempotent no-op (re-publishing must not create a second demo payer),
/// and retries transient failures.
/// </summary>
public sealed class HttpPayerSeederTests
{
    private static readonly SeedBillerContext Biller =
        new("biller-42", "Riverside Water", "utility", new Uri("https://riverside.example"));

    private static HttpPayerSeeder Create(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://payer-account.local/") },
            new DeterministicSeedPayerGenerator(),
            NullLogger<HttpPayerSeeder>.Instance);

    [Fact]
    public async Task PostsSnakeCasePayerRequestWithSeededAccountAndDefaults()
    {
        var handler = new RecordingHandler(_ => Ok());
        var seeder = Create(handler);

        await seeder.SeedAsync(Biller, ["4421"], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://payer-account.local/payers/seed", request.Uri);

        using var doc = JsonDocument.Parse(request.Body);
        var root = doc.RootElement;
        Assert.Equal("biller-42", root.GetProperty("biller_id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("email").GetString()));
        var accounts = root.GetProperty("account_numbers").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal("4421", Assert.Single(accounts));

        var preferences = root.GetProperty("preferences");
        Assert.False(preferences.GetProperty("autopay").GetBoolean());
        Assert.False(preferences.GetProperty("paperless").GetBoolean());
        var channels = preferences.GetProperty("channels").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("email", channels);
    }

    [Fact]
    public async Task ConflictIsTreatedAsIdempotentNoOp()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        var seeder = Create(handler);

        // Must not throw and must not retry: the demo payer already exists.
        await seeder.SeedAsync(Biller, ["4421"], CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TransientFailureIsRetriedThenSucceeds()
    {
        var attempt = 0;
        var handler = new RecordingHandler(_ =>
            ++attempt == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok());
        var seeder = Create(handler);

        await seeder.SeedAsync(Biller, ["4421"], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    private static HttpResponseMessage Ok()
    {
        const string payer = """
            {"payer_id":"payer-1","biller_id":"biller-42","name":"Alex Rivera",
             "email":"demo.payer.abc@pronto-demo.example","phone":null,
             "account_numbers":["4421"],
             "preferences":{"autopay":false,"paperless":false,"channels":["email"],"payment_day":null}}
            """;
        return new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(payer, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), body));
            return respond(request);
        }
    }
}
