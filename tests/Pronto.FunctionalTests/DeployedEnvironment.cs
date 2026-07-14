using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Pronto.FunctionalTests;

/// <summary>
/// Shared fixture for functional tests that drive the deployed service APIs over HTTP.
///
/// Target is <c>IC_FUNCTIONAL_BASE_URL</c> (the nonprod gateway in CI). When it is unset the
/// fixture is <see cref="Enabled"/> = false and every test skips — so a plain
/// <c>dotnet test</c> (e.g. CI's in-process run) never touches a deployed environment.
///
/// Every biller id a test uses is registered via <see cref="Track"/>; on teardown the fixture
/// calls each service's nonprod-gated <c>DELETE /internal/test-data</c> so the run leaves no
/// data behind in the shared Cosmos.
/// </summary>
public sealed class DeployedEnvironment : IAsyncLifetime
{
    // Gateway path prefixes for the four services (see deploy/kubernetes overlays httproutes).
    private static readonly string[] ServicePrefixes = ["/invoices", "/payments", "/payers", "/api"];

    private readonly ConcurrentDictionary<string, byte> billersToPurge = new(StringComparer.Ordinal);

    public DeployedEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable("IC_FUNCTIONAL_BASE_URL");
        Enabled = !string.IsNullOrWhiteSpace(baseUrl);
        Client = new HttpClient
        {
            BaseAddress = new Uri(Enabled ? baseUrl! : "http://localhost/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        // Unique per run so concurrent PR deploys never collide and all data is attributable.
        RunBillerId = "func-" + Guid.NewGuid().ToString("N")[..12];
    }

    public bool Enabled { get; }

    public HttpClient Client { get; }

    /// <summary>Run-scoped biller id for tests that control their own partition.</summary>
    public string RunBillerId { get; }

    public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Register a biller id to purge from every service during teardown.</summary>
    public void Track(string billerId) => billersToPurge.TryAdd(billerId, 0);

    public Task InitializeAsync()
    {
        Track(RunBillerId);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Enabled)
        {
            foreach (var billerId in billersToPurge.Keys)
            {
                foreach (var prefix in ServicePrefixes)
                {
                    // Best-effort: purge every service for every tracked biller. A service that
                    // never saw the biller just returns 204. Never fail the run on teardown.
                    try
                    {
                        using var response = await Client.DeleteAsync(
                            $"{prefix}/internal/test-data?biller_id={Uri.EscapeDataString(billerId)}");
                    }
                    catch (HttpRequestException)
                    {
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }
        }

        Client.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class FunctionalSuite : ICollectionFixture<DeployedEnvironment>
{
    public const string Name = "functional";
}
