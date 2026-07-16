using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// Resolves the deployed environment these black-box functional tests drive over HTTP.
///
/// The target is read from <c>PRONTO_FUNCTIONAL_BASE_URL</c> (the public gateway origin, e.g.
/// <c>http://pronto-nonprod.eastus2.cloudapp.azure.com</c>). When it is unset every test is
/// SKIPPED rather than failed, so this project is inert during a normal local build; the
/// nonprod deploy workflow sets it and runs the suite for real. Sub-paths mirror the gateway
/// routes (see deploy/kubernetes/overlays/*/httproutes.yaml): the Biller Experience API is under
/// <c>/api</c> and the Invoice API under <c>/invoices</c>.
/// </summary>
public static class ProntoEnvironment
{
    public const string BaseUrlVariable = "PRONTO_FUNCTIONAL_BASE_URL";

    public static string? BaseUrl =>
        Environment.GetEnvironmentVariable(BaseUrlVariable)?.Trim().TrimEnd('/') is { Length: > 0 } value
            ? value
            : null;

    public static bool IsConfigured => BaseUrl is not null;

    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    /// <summary>Skips the calling test when no deployed environment is configured.</summary>
    public static string RequireBaseUrl()
    {
        Skip.If(!IsConfigured,
            $"{BaseUrlVariable} is not set; functional tests only run against a deployed environment.");
        return BaseUrl!;
    }

    public static HttpClient CreateBillerApiClient() => CreateClient("/api/");

    public static HttpClient CreateInvoiceApiClient() => CreateClient("/invoices/");

    private static HttpClient CreateClient(string prefix)
    {
        var baseUrl = RequireBaseUrl();
        return new HttpClient
        {
            BaseAddress = new Uri(baseUrl + prefix),
            Timeout = TimeSpan.FromMinutes(3),
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}
